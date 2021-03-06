using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Win32;
using System.Linq;

using Au.Types;

using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Globalization;

namespace Au.Compiler
{
	/// <summary>
	/// Resolves assembly metadata references (string to PortableExecutableReference).
	/// For each compilation create a MetaReferences variable, call Resolve if need non-default references, then use Refs list.
	/// </summary>
	/// <remarks>
	/// Temporarily keeps PortableExecutableReference objects in a cache. Except Au and Core reference assemblies; Core assemblies are small (without code).
	/// Single static cache is used by all MetaReferences variables.
	/// Cache may require many MB of unmanaged memory, therefore PortableExecutableReference objects are removed and GC-collected when not used anywhere for some time. Reloading is quite fast.
	///		A reference to a PortableExecutableReference variable prevents removing it from cache and GC-collecting.
	///		A reference to a MetaReferences variable prevents removing/disposing is Refs items.
	///		CodeAnalysis Project etc objects also have references, therefore need to manage their lifetimes too.
	/// MetaReferences variables can be created in different threads, but a variable must be used in a single thread.
	/// </remarks>
	class MetaReferences
	{
		/// <summary>
		/// Contains info used to load a PortableExecutableReference. Loads when need. Supports XML documentation.
		/// </summary>
		class _MR
		{
			public readonly string name, path;
			WeakReference<PortableExecutableReference> _wr;
			PortableExecutableReference _refKeeper;
			long _timeout;

			const int c_timerPeriod = 30_000;

			public _MR(string name, string path)
			{
				//AOutput.Write(name);
				this.name = name;
				this.path = path;
				_wr = new WeakReference<PortableExecutableReference>(null);
			}

			public PortableExecutableReference Ref {
				get {
					if(!_wr.TryGetTarget(out var r)) {
						//for(int i=0;i<10;i++) //tested: process memory does not grow when loading same file several times
						//APerf.First();
						r = MetadataReference.CreateFromFile(path, (this as _MR2)?.Prop ?? default, _DocumentationProvider.Create(path));
						//APerf.NW();

						_wr.SetTarget(r);
						//AOutput.Write("LOADED", name, this is _MR2);

						//prevent GC too early, eg in the middle of compiling many files
						if(s_timer == null) {
							s_timer = new Timer(_ => {
								int nKeep = 0;
								lock(s_cache) {
									long timeNow = ATime.WinMilliseconds;
									foreach(var v in s_cache) {
										if(v._refKeeper == null) continue;
										long t = v._timeout;
										if(timeNow >= t) v._refKeeper = null;
										else nKeep++;
									}
									//AOutput.Write("timer", nRemoved);
									s_isTimer = nKeep > 0 ? s_timer.Change(c_timerPeriod, -1) : false;
								}
								if(nKeep == 0) GC.Collect();
							});
						}
					} //else AOutput.Write("cached", name, this is _MR2);

					if(!s_isTimer) s_isTimer = s_timer.Change(c_timerPeriod, -1);
					_timeout = ATime.WinMilliseconds + c_timerPeriod - 1000;
					_refKeeper = r;
					return r;
				}
			}

			//public static void CompactCache()
			//{
			//	lock(s_cache) {
			//		foreach(var v in s_cache) v._refKeeper = null;
			//		s_isTimer = s_timer.Change(1000, -1);
			//	}
			//}

#if DEBUG
			public bool IsCached => _wr.TryGetTarget(out _);
#endif
		}

		class _MR2 : _MR
		{
			string _alias;
			bool _isCOM;

			public _MR2(string name, string path, string alias, bool isCOM) : base(name, path)
			{
				_alias = alias;
				_isCOM = isCOM;
			}

			public MetadataReferenceProperties Prop
				=> new MetadataReferenceProperties(aliases: _alias == null ? default : ImmutableArray.Create(_alias), embedInteropTypes: _isCOM);

			public bool PropEq(string alias, bool isCOM) => isCOM == _isCOM && alias == _alias;
		}

		static List<_MR> s_cache = new List<_MR>();
		static Timer s_timer; //SHOULDDO: ATimer. Previously Timer used less CPU, but now several times more. Maybe in Core.
		static bool s_isTimer;

		/// <summary>
		/// List containing <see cref="DefaultReferences"/> + references for which was called <see cref="Resolve"/> of this MetaReferences variable.
		/// </summary>
		public List<PortableExecutableReference> Refs { get; }

		/// <summary>
		/// These references are added when compiling any script/library.
		/// Au.dll and all .NET Core design-time assemblies.
		/// </summary>
		public static readonly Dictionary<string, PortableExecutableReference> DefaultReferences;

		static MetaReferences()
		{
			//var p1 = APerf.Create();
			s_netDocProvider = new _NetDocumentationProvider();
			//p1.Next('d');

			DefaultReferences = new Dictionary<string, PortableExecutableReference>(300, StringComparer.OrdinalIgnoreCase);

			using var db = EdDatabases.OpenRef();
			using var stat = db.Statement("SELECT * FROM ref");
			while(stat.Step()) {
				var asmName = stat.GetText(0);
				var doc = s_netDocProvider.HaveRef(asmName) ? s_netDocProvider : null;
				var r = MetadataReference.CreateFromImage(stat.GetArray<byte>(1), documentation: doc, filePath: asmName);
				DefaultReferences.Add(asmName, r);
			}
			//p1.Next('c');

			var auPath = AFolders.ThisAppBS + "Au.dll";
			DefaultReferences.Add("Au", MetadataReference.CreateFromFile(auPath, documentation: _DocumentationProvider.Create(auPath)));
			//p1.NW('a');
		}

		public MetaReferences()
		{
			var def = DefaultReferences;
			Refs = new List<PortableExecutableReference>(def.Count + 10);
			foreach(var v in def) Refs.Add(v.Value);
		}

		/// <summary>
		/// Finds reference assembly file, creates PortableExecutableReference and adds to the cache.
		/// Returns false if file not found.
		/// </summary>
		/// <param name="reference">Assembly filename (like "My.dll" or "My"), relative path or full path. If not full path, must be in AFolders.ThisApp.</param>
		/// <exception cref="Exception">Some unexpected exception, eg failed to load the found file.</exception>
		/// <remarks>
		/// Can be "Alias=X.dll" - assembly reference that can be used with C# keyword 'extern alias'.
		/// Loads the file but does not parse. If bad format, error later when compiling.
		/// </remarks>
		public bool Resolve(string reference, bool isCOM)
		{
			string alias = null;
			int i = reference.IndexOf('=');
			if(i > 0) {
				alias = reference.Remove(i);
				reference = reference.Substring(i + 1);
			}

			var a = s_cache;
			lock(a) {
				for(i = 0; i < a.Count; i++) {
					if(a[i].name.Eqi(reference) && _PropEq(a[i], alias, isCOM)) {
						_AddRef(i);
						return true;
					}
				}

				var path = _ResolvePath(reference, isCOM);
				if(path == null) return false;
				path = APath.Normalize_(path);

				for(i = 0; i < a.Count; i++) {
					if(a[i].path.Eqi(path) && _PropEq(a[i], alias, isCOM)) goto g1;
				}
				_MR k;
				if(isCOM || alias != null) k = new _MR2(reference, path, alias, isCOM);
				else k = new _MR(reference, path);
				a.Add(k);
				g1:
				_AddRef(i);
			}

			return true;

			void _AddRef(int iCache)
			{
				var r = s_cache[iCache];
				Refs.Add(r.Ref);
			}

			bool _PropEq(_MR u, string alias, bool isCOM)
			{
				if(u is _MR2 m2) return m2.PropEq(alias, isCOM);
				return !isCOM && alias == null;
			}
		}

		static string _ResolvePath(string re, bool isCOM)
		{
			if(re.NE()) return null;
			bool isFull = APath.IsFullPathExpandEnvVar(ref re);
			if(!isFull && isCOM) { isFull = true; re = AFolders.Workspace + @".interop\" + re; }
			if(isFull) return AFile.ExistsAsFile(re) ? re : null;

			if(!re.Ends(".dll", true)) re += ".dll";
			re = AFolders.ThisAppBS + re;
			return AFile.ExistsAsFile(re) ? re : null;

			//note: we don't use Microsoft.CodeAnalysis.Scripting.ScriptMetadataResolver. It is very slow, makes compiling many times slower.
		}

		/// <summary>
		/// Extracts path from compiler error message CS0009 and removes the reference from cache.
		/// </summary>
		/// <param name="errorMessage">"Metadata file '....dll' could not be opened ..."</param>
		public static void RemoveBadReference(string errorMessage)
		{
			if(errorMessage.RegexMatch(@"'(.+?)'", 1, out string path))
				lock(s_cache) { s_cache.RemoveAll(v => v.path.Eqi(path)); }
		}

		//public static void CompactCache() => _MR.CompactCache();

		public static bool IsDotnetAssembly(string path)
		{
			using var stream = AFile.WaitIfLocked(() => File.OpenRead(path));
			using var pr = new System.Reflection.PortableExecutable.PEReader(stream);
			return pr.HasMetadata;
		}

#if DEBUG
		internal static void DebugPrintCachedRefs()
		{
			foreach(var v in s_cache) if(v.IsCached) AOutput.Write(v.name);
		}
#endif

		/// <summary>
		/// Gets XML documentation for an assembly.
		/// Uses a 2-column SQLite database auto-created from XML file by <see cref="Create"/>.
		/// Not XML file directly because it uses much memory.
		/// </summary>
		class _DocumentationProvider : DocumentationProvider
		{
			protected ASqlite _db;
			ASqliteStatement _stat;

			/// <summary>
			/// Creates documentation provider for assembly <i>asmPath</i>.
			/// Returns null if its xml file does not exist.
			/// Returns _DocumentationProvider if xml file is big and found or successfully created and successfully loaded database for it.
			/// Else returns XmlDocumentationProvider.
			/// </summary>
			public static DocumentationProvider Create(string asmPath)
			{
				if(s_d.TryGetValue(asmPath, out var dp)) return dp;

				var xmlPath = Path.ChangeExtension(asmPath, "xml");
				if(!AFile.GetProperties(xmlPath, out var px)) return null;

				if(px.Size >= 10_000) {
					var md5 = new Au.Util.AHash.MD5(); md5.Add(xmlPath.Lower());
					var dbPath = AFolders.ThisAppTemp + md5.Hash.ToString() + ".db";
					try {
						if(!AFile.GetProperties(dbPath, out var pd) || pd.LastWriteTimeUtc != px.LastWriteTimeUtc) {
							//ADebug.Print($"creating db: {asmPath}  ->  {dbPath}");
							AFile.Delete(dbPath);
							using(var d = new ASqlite(dbPath)) {
								using var trans = d.Transaction();
								d.Execute("CREATE TABLE doc (name TEXT PRIMARY KEY, xml TEXT)");
								using var statInsert = d.Statement("INSERT INTO doc VALUES (?, ?)");

								var xr = AExtXml.LoadElem(xmlPath);
								foreach(var e in xr.Descendants("member")) {
									var name = e.Attr("name");

									//remove <remarks> and <example>.
									foreach(var v in e.Descendants("remarks").ToArray()) v.Remove();
									foreach(var v in e.Descendants("example").ToArray()) v.Remove();

									using var reader = e.CreateReader();
									reader.MoveToContent();
									var xml = reader.ReadInnerXml();
									//AOutput.Write(name, xml);

									statInsert.BindAll(name, xml).Step();
									statInsert.Reset();
								}
								trans.Commit();
								d.Execute("VACUUM");
							}
							File.SetLastWriteTimeUtc(dbPath, px.LastWriteTimeUtc);
						}
						var db = new ASqlite(dbPath, SLFlags.SQLITE_OPEN_READONLY); //never mind: we don't dispose it on process exit
						s_d[asmPath] = dp = new _DocumentationProvider { _db = db };
						return dp;
					}
					catch(Exception ex) { ADebug.Print(ex.ToStringWithoutStack()); }
				}
				return XmlDocumentationProvider.CreateFromFile(xmlPath);
			}
			static System.Collections.Concurrent.ConcurrentDictionary<string, _DocumentationProvider> s_d = new System.Collections.Concurrent.ConcurrentDictionary<string, _DocumentationProvider>(StringComparer.OrdinalIgnoreCase);

			protected internal override string GetDocumentationForSymbol(string documentationMemberID, CultureInfo preferredCulture, CancellationToken cancellationToken = default)
			{
				if(_db != null) {
					lock(_db) { //sometimes not in main thread
						try {
							_stat ??= _db.Statement("SELECT xml FROM doc WHERE name=?");
							if(_stat.Bind(1, documentationMemberID).Step()) return _stat.GetText(0);
							//ADebug.Print(documentationMemberID);
						}
						catch(SLException ex) { ADebug.Print(ex.Message); }
						finally { _stat?.Reset(); }
					}
				}
				return null;
			}

			public override bool Equals(object obj)
			{
				ADebug.PrintIf(obj != this, "Equals");
				return obj == this;
			}

			public override int GetHashCode()
			{
				ADebug.Print("GetHashCode");
				return 1;
			}
		}

		/// <summary>
		/// Gets XML documentation for .NET Core assemblies.
		/// Uses a 2-column SQLite database created from XML files by <see cref="EdDatabases.CreateRefAndDoc"/>.
		/// Not XML files directly because it uses about 150 MB of memory.
		/// </summary>
		class _NetDocumentationProvider : _DocumentationProvider
		{
			HashSet<string> _refs;

			public _NetDocumentationProvider()
			{
				try {
					_db = EdDatabases.OpenDoc(); //never mind: we don't dispose it on process exit
					if(_db.Get(out string s, "SELECT xml FROM doc WHERE name='.'")) _refs = new HashSet<string>(s.SegSplit("\n"));
				}
				catch(SLException ex) { ADebug.Print(ex.Message); }
			}

			/// <summary>
			/// Returns true if the database contains XML doc of the reference assembly.
			/// </summary>
			/// <param name="refName">Like "mscorlib" or "System.Drawing" or "Au".</param>
			public bool HaveRef(string refName) => _refs?.Contains(refName) ?? false;
		}
		static _NetDocumentationProvider s_netDocProvider;
	}
}
