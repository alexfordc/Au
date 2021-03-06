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
//using System.Linq;

using Au.Types;
using Au.Util;

namespace Au
{
	/// <summary>
	/// A SQLite database connection.
	/// Creates/opens/closes database file or in-memory database. Executes SQL, etc.
	/// </summary>
	/// <remarks>
	/// This class wraps a SQLite API object sqlite3* and related sqlite3_x functions. They are documented perfectly in the SQLite website.
	/// Uses this unmanaged dll: AFolders.ThisApp + @"64\sqlite3.dll". In 32-bit process - "32" instead of "64".
	/// 
	/// To correctly close the database file, at first need to dipose all child objects, such as <see cref="ASqliteStatement"/>, then dispose the <b>ASqlite</b> object. To dispose a static <b>ASlite</b> variable, you may want to use <see cref="AProcess.Exit"/> event. Although this class has a finalizer that disposes the object (closes database), you should always dispose explicitly. Finalizers don't run on process exit.
	/// </remarks>
	/// <seealso cref="ASqliteStatement"/>
	/// <example>
	/// <code><![CDATA[
	/// //open database file
	/// using var db = new ASqlite(@"Q:\test\sqlite.db");
	/// //create table
	/// db.Execute("CREATE TABLE IF NOT EXISTS test(id INTEGER PRIMARY KEY, name TEXT, x INT, guid BLOB, array BLOB)");
	/// 
	/// //add 2 rows of data
	/// using(var trans = db.Transaction()) { //optional, but makes much faster when making multiple changes, and ensures that all or none of these changes are written to the database
	/// 	using(var p = db.Statement("INSERT OR REPLACE INTO test VALUES(?, ?, :x, ?, ?)")) {
	/// 		//assume we want to add values of these variables to the database table
	/// 		int id = 1; string name = "TEXT"; long x = -10; Guid guid = Guid.NewGuid(); int[] arr = new int[] { 1, 2, 3 };
	/// 		//add first row
	/// 		p.Bind(1, id);
	/// 		p.Bind(2, name).BindStruct(4, guid).Bind(5, arr);
	/// 		p.Bind(":x", x);
	/// 		p.Step();
	/// 		//add second row
	/// 		p.Reset().Bind(1, 2).Bind(":x", 123456789012345).Step(); //unbound columns are null
	/// 	}
	/// 	//update single row
	/// 	db.Execute("UPDATE test SET name=?2 WHERE id=?1", 2, "two");
	/// 	//write all this to database
	/// 	trans.Commit();
	/// }
	/// 
	/// //get data
	/// using(var p = db.Statement("SELECT * FROM test")) {
	/// 	while(p.Step()) { //for each row of results
	/// 		AOutput.Write(p.GetInt(0), p.GetText(1), p.GetLong(2));
	/// 		AOutput.Write(p.GetStruct<Guid>("guid"));
	/// 		AOutput.Write(p.GetArray<int>("array"));
	/// 		AOutput.Write("----");
	/// 	}
	/// }
	/// //get single value
	/// if(db.Get(out string s1, "SELECT name FROM test WHERE id=?", 1)) AOutput.Write(s1); else AOutput.Write("not found");
	/// if(db.Get(out int i1, "PRAGMA page_size")) AOutput.Write(i1);
	/// ]]></code>
	/// </example>
	public unsafe class ASqlite : IDisposable
	{
		IntPtr _db;

		/// <summary>
		/// Opens or creates a database file.
		/// </summary>
		/// <param name="file">
		/// Database file. Can be:
		/// - Full path. Supports environment variables etc, see <see cref="APath.ExpandEnvVar"/>
		/// - ":memory:" - create a private, temporary in-memory database.
		/// - "" - create a private, temporary on-disk database.
		/// - Starts with "file:" - see sqlite3_open_v2 documentation in SQLite website.
		/// </param>
		/// <param name="flags">sqlite3_open_v2 flags, documanted in SQLite website. Default: read-write, create file if does not exist (and parent directory).</param>
		/// <param name="sql">
		/// SQL to execute. For example, one or more ;-separated PRAGMA statements to configure the database connection. Or even "CREATE TABLE IF NOT EXISTS ...".
		/// This function also always executes "PRAGMA foreign_keys=ON;PRAGMA secure_delete=ON;".
		/// </param>
		/// <exception cref="ArgumentException">Not full path.</exception>
		/// <exception cref="SLException">Failed to open database or execute sql.</exception>
		/// <remarks>
		/// Calls sqlite3_open_v2.
		/// <note>If a variable of this class is used by multiple threads, use <c>lock(variable) {  }</c> where need.</note>
		/// </remarks>
		public ASqlite(string file, SLFlags flags = SLFlags.ReadWriteCreate, string sql = null)
		{
			bool isSpec = file != null && (file.Length == 0 || file == ":memory:" || file.Starts("file:"));
			if(!isSpec) {
				file = APath.Normalize(file);
				if(flags.Has(SLFlags.SQLITE_OPEN_CREATE) && !AFile.ExistsAsFile(file, true)) AFile.CreateDirectoryFor(file);
			}
			var r = SLApi.sqlite3_open_v2(AConvert.ToUtf8(file), ref _db, flags, null);
			if(r != 0) {
				Dispose();
				throw new SLException(r, "sqlite3_open " + file);
			}
			Execute("PRAGMA foreign_keys=ON;PRAGMA secure_delete=ON;" + sql);
		}

		///
		protected virtual void Dispose(bool disposing)
		{
			if(_db != default) {
				var r = SLApi.sqlite3_close_v2(_db);
				Debug.Assert(r == 0); SLUtil.Warn(r, "sqlite3_close");
				_db = default;
			}
		}

		/// <summary>
		/// Calls sqlite3_close_v2.
		/// If fails, writes warning to the output.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		///
		~ASqlite() => Dispose(false);

		/// <summary>sqlite3*</summary>
		public static implicit operator IntPtr(ASqlite c) => c._db;

		/// <summary>sqlite3*</summary>
		public IntPtr Handle => _db;

		/// <summary>
		/// Calls sqlite3_exec to execute one or more SQL statements that don't return data.
		/// </summary>
		/// <param name="sql">SQL statement, or several ;-separated statements.</param>
		/// <exception cref="SLException">Failed to execute sql.</exception>
		public void Execute(string sql)
		{
			var b = AConvert.ToUtf8(sql);
			byte* es = null; //gets better error text than sqlite3_errstr; sqlite3_errmsg gets nothing after sqlite3_exec.
			var r = SLApi.sqlite3_exec(_db, b, default, default, &es);
			if(r != 0) throw new SLException(r, "sqlite3_exec", SLUtil.ToStringAndFree(es));
		}

		/// <summary>
		/// Executes single SQL statement that does not return data. Binds values.
		/// </summary>
		/// <param name="sql">Single SQL statement.</param>
		/// <param name="bind">
		/// Values that will replace <c>?</c> characters in sql.
		/// Read about SQL parameters in SQLite website. Supported types: <see cref="ASqliteStatement.BindAll"/>. Example: <see cref="ASqlite"/>.
		/// </param>
		/// <exception cref="SLException">Failed.</exception>
		/// <exception cref="NotSupportedException">sql contains more than single SQL statement.</exception>
		public void Execute(string sql, params object[] bind)
		{
			using var p = Statement(sql, bind);
			p.Step();
		}

		/// <summary>
		/// Returns <c>new Statement(this, sql)</c>.
		/// </summary>
		/// <param name="sql">Single SQL statement. This function does not execute it.</param>
		/// <seealso cref="ASqliteStatement"/>
		public ASqliteStatement Statement(string sql) => new ASqliteStatement(this, sql);

		/// <summary>
		/// Returns <c>new Statement(this, sql).BindAll(bind)</c>.
		/// </summary>
		/// <param name="sql">Single SQL statement. This function does not execute it.</param>
		/// <param name="bind">
		/// Values that will replace <c>?</c> characters in sql. Optional.
		/// Read about SQL parameters in SQLite website. Supported types: <see cref="ASqliteStatement.BindAll"/>. Example: <see cref="ASqlite"/>.
		/// </param>
		/// <seealso cref="ASqliteStatement"/>
		/// <seealso cref="ASqliteStatement.BindAll"/>
		public ASqliteStatement Statement(string sql, params object[] bind) => new ASqliteStatement(this, sql).BindAll(bind);

		#region get

		/// <summary>
		/// Executes single SQL statement and gets single value.
		/// Returns false if the statement returned no data.
		/// </summary>
		/// <param name="value">Receives data.</param>
		/// <param name="sql">SQL statement.</param>
		/// <param name="bind">
		/// Values that will replace <c>?</c> characters in sql.
		/// Read about SQL parameters in SQLite website. Supported types: <see cref="ASqliteStatement.BindAll"/>. Example: <see cref="ASqlite"/>.
		/// </param>
		/// <exception cref="SLException">Failed.</exception>
		/// <exception cref="NotSupportedException">sql contains more than single SQL statement.</exception>
		/// <remarks>
		/// The Get(out int, ...) overload also can be used to get uint, short, ushort, byte, sbyte, enum. Will need to cast from int.
		/// The Get(out long, ...) overload also can be used to get ulong, 64-bit enum, maybe DateTime.
		/// The Get(out double, ...) overload also can be used to get float.
		/// Use <see cref="GetStruct"/> for other value types - decimal, Guid, Rect, etc.
		/// </remarks>
		public bool Get(out int value, string sql, params object[] bind)
		{
			using var p = Statement(sql, bind);
			bool R = p.Step();
			value = R ? p.GetInt(0) : default;
			return R;
		}

		/// <exception cref="SLException">Failed.</exception>
		/// <exception cref="NotSupportedException">sql contains more than single SQL statement.</exception>
		public bool Get(out long value, string sql, params object[] bind)
		{
			using var p = Statement(sql, bind);
			bool R = p.Step();
			value = R ? p.GetLong(0) : default;
			return R;
		}

		///// <exception cref="SLException">Failed.</exception>
		///// <exception cref="NotSupportedException">sql contains more than single SQL statement.</exception>
		//public bool Get(out DateTime value, bool convertToLocal, string sql, params object[] bind)
		//{
		//	using var p = Prepare(sql, bind);
		//	bool R = p.Step();
		//	value = R ? p.GetDateTime(0, convertToLocal) : default;
		//	return R;
		//}

		/// <exception cref="SLException">Failed.</exception>
		/// <exception cref="NotSupportedException">sql contains more than single SQL statement.</exception>
		public bool Get(out bool value, string sql, params object[] bind)
		{
			using var p = Statement(sql, bind);
			bool R = p.Step();
			value = R ? p.GetBool(0) : default;
			return R;
		}

		/// <exception cref="SLException">Failed.</exception>
		/// <exception cref="NotSupportedException">sql contains more than single SQL statement.</exception>
		public bool Get(out double value, string sql, params object[] bind)
		{
			using var p = Statement(sql, bind);
			bool R = p.Step();
			value = R ? p.GetDouble(0) : default;
			return R;
		}

		/// <exception cref="SLException">Failed.</exception>
		/// <exception cref="NotSupportedException">sql contains more than single SQL statement.</exception>
		public bool Get(out string value, string sql, params object[] bind)
		{
			using var p = Statement(sql, bind);
			bool R = p.Step();
			value = R ? p.GetText(0) : default;
			return R;
		}

		/// <exception cref="SLException">Failed.</exception>
		/// <exception cref="NotSupportedException">sql contains more than single SQL statement.</exception>
		public bool Get<T>(out T[] value, string sql, params object[] bind) where T : unmanaged
		{
			using var p = Statement(sql, bind);
			bool R = p.Step();
			value = R ? p.GetArray<T>(0) : default;
			return R;
		}

		/// <exception cref="SLException">Failed.</exception>
		/// <exception cref="NotSupportedException">sql contains more than single SQL statement.</exception>
		public bool Get<T>(out List<T> value, string sql, params object[] bind) where T : unmanaged
		{
			using var p = Statement(sql, bind);
			bool R = p.Step();
			value = R ? p.GetList<T>(0) : default;
			return R;
		}

		/// <summary>See <see cref="Get(out int, string, object[])"/>.</summary>
		/// <exception cref="SLException">Failed.</exception>
		/// <exception cref="NotSupportedException">sql contains more than single SQL statement.</exception>
		public bool GetStruct<T>(out T value, string sql, params object[] bind) where T : unmanaged
		{
			using var p = Statement(sql, bind);
			bool R = p.Step();
			value = R ? p.GetStruct<T>(0) : default;
			return R;
		}

		/// <summary>
		/// Executes single SQL statement and returns true if it returns at least one row of data.
		/// More info: <see cref="Get(out int, string, object[])"/>.
		/// </summary>
		/// <remarks>This function is similar to the <b>GetX</b> functions, but it does not retrieve the data.</remarks>
		public bool Any(string sql, params object[] bind)
		{
			using var p = Statement(sql, bind);
			return p.Step();
		}

		#endregion

		/// <summary>
		/// sqlite3_last_insert_rowid.
		/// </summary>
		public long LastInsertRowid => SLApi.sqlite3_last_insert_rowid(_db);

		/// <summary>
		/// sqlite3_changes.
		/// </summary>
		public int Changes => SLApi.sqlite3_changes(_db);

		/// <summary>
		/// 0 == sqlite3_get_autocommit.
		/// </summary>
		public bool IsInTransaction => 0 == SLApi.sqlite3_get_autocommit(_db);

		/// <summary>
		/// Returns <c>new SLTransaction(this, sql, sqlOfDispose)</c>.
		/// See <see cref="SLTransaction(ASqlite, string, string)"/>.
		/// </summary>
		/// <param name="sql">SQL to execute now. Default "BEGIN".</param>
		/// <param name="sqlOfDispose">SQL to execute when disposing the <b>SLTransaction</b> variable. Default "ROLLBACK".</param>
		public SLTransaction Transaction(string sql = "BEGIN", string sqlOfDispose = "ROLLBACK")
			=> new SLTransaction(this, sql, sqlOfDispose);

		/// <summary>
		/// Returns true if the table exists.
		/// </summary>
		/// <param name="table">Table name.</param>
		public bool TableExists(string table)
		{
			return Any($"SELECT 1 FROM sqlite_master WHERE type='table' AND name=?", table);
		}

		#region util

		/// <summary>
		/// Returns true if default database text encoding is not UTF-8.
		/// </summary>
		internal bool IsUtf16 {
			get {
				if(__isUtf16 == 0) {
					var t = _RawGetText("PRAGMA encoding") ?? "";
					__isUtf16 = (byte)(t.Starts("UTF-16") ? 2 : 1);
				}
				return __isUtf16 == 2;
			}
		}
		byte __isUtf16; //0 not queried, 1 utf8 or failed, 2 utf16

		string _RawGetText(string sql)
		{
			string r = null;
			fixed (char* p = sql) {
				IntPtr x = default;
				if(0 == SLApi.sqlite3_prepare16_v3(_db, p, -1, 0, ref x, null)) {
					var e = SLApi.sqlite3_step(x);
					Debug.Assert(e == SLError.Row || e == SLError.Done);
					if(e == SLError.Row) r = AConvert.FromUtf8(SLApi.sqlite3_column_text(x, 0));
					SLApi.sqlite3_finalize(x);
				}
			}
			return r;
		}

		#endregion
	}

	/// <summary>
	/// Creates and executes a SQLite prepared statement.
	/// </summary>
	/// <threadsafety static="true" instance="false"/>
	/// <remarks>
	/// This class wraps a SQLite API object sqlite3_stmt* and related sqlite3_x functions. They are documented perfectly in the SQLite website.
	/// More info and example: <see cref="ASqlite"/>.
	/// <note type="important">A variable of this class can be used by multiple threads, but not simultaneously. Use <c>lock(database) {  }</c> where need.</note>
	/// </remarks>
	public unsafe class ASqliteStatement : IDisposable
	{
		ASqlite _db;
		IntPtr _st;

		/// <summary>
		/// Calls sqlite3_prepare16_v3.
		/// </summary>
		/// <param name="db"></param>
		/// <param name="sql">Single SQL statement.</param>
		/// <param name="persistent">Use flag SQLITE_PREPARE_PERSISTENT.</param>
		/// <exception cref="ArgumentNullException">db is null.</exception>
		/// <exception cref="SLException">Failed.</exception>
		/// <exception cref="NotSupportedException">sql contains more than single SQL statement.</exception>
		public ASqliteStatement(ASqlite db, string sql, bool persistent = false)
		{
			_db = db ?? throw new ArgumentNullException();
			int flags = persistent ? 1 : 0; //SQLITE_PREPARE_PERSISTENT
			fixed (char* p = sql) {
				char* tail = null;
				_Err(SLApi.sqlite3_prepare16_v3(db, p, sql.Length * 2, flags, ref _st, &tail), "sqlite3_prepare");
				if(tail != null && tail - p != sql.Length) throw new NotSupportedException("sql contains more than single SQL statement");
			}
		}

		///
		protected virtual void Dispose(bool disposing)
		{
			if(_st != default) {
				//note: don't throw, because sqlite3_finalize can return error of previous sqlite3_step etc
				SLApi.sqlite3_finalize(_st);
				_st = default;
			}
		}

		/// <summary>
		/// Calls sqlite3_finalize.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		///
		~ASqliteStatement() => Dispose(false);

		/// <summary>sqlite3_stmt*</summary>
		public static implicit operator IntPtr(ASqliteStatement s) => s._st;

		/// <summary>sqlite3_stmt*</summary>
		public IntPtr Handle => _st;

		///
		public ASqlite DB => _db;

		/// <summary>
		/// Calls sqlite3_step.
		/// Returns true if results data available (sqlite3_step returned SQLITE_ROW).
		/// </summary>
		/// <exception cref="SLException">Failed.</exception>
		public bool Step()
		{
			var r = SLApi.sqlite3_step(_st);
			if(r == SLError.Row) return true;
			if(r != SLError.Done) _Err(r, "sqlite3_step");
			return false;
		}

		/// <summary>
		/// Calls sqlite3_reset and/or sqlite3_clear_bindings. Returns self.
		/// </summary>
		/// <param name="resetStatement">Call sqlite3_reset. Default true.</param>
		/// <param name="clearBindings">Call sqlite3_clear_bindings. Default true.</param>
		public ASqliteStatement Reset(bool resetStatement = true, bool clearBindings = true)
		{
			//note: don't throw, because sqlite3_reset can return error of previous sqlite3_step
			if(resetStatement) SLApi.sqlite3_reset(_st);
			if(clearBindings) SLApi.sqlite3_clear_bindings(_st);
			return this;
		}

		#region bind

		int _B(SLIndexOrName p)
		{
			if(p.name == null) return p.index;
			int r = SLApi.sqlite3_bind_parameter_index(_st, AConvert.ToUtf8(p.name));
			if(r == 0) throw new SLException($"Parameter '{p.name}' does not exist in the SQL statement.");
			return r;
		}

		/// <summary>Calls sqlite3_bind_int. Returns self.</summary>
		/// <exception cref="SLException">Failed.</exception>
		public ASqliteStatement Bind(SLIndexOrName sqlParam, int value)
			=> _Err(SLApi.sqlite3_bind_int(_st, _B(sqlParam), value), "sqlite3_bind_int");

		/// <summary>Calls sqlite3_bind_int. Returns self.</summary>
		/// <exception cref="SLException">Failed.</exception>
		public ASqliteStatement Bind(SLIndexOrName sqlParam, uint value)
			=> Bind(sqlParam, (int)value);

		/// <summary>Calls sqlite3_bind_int64. Returns self.</summary>
		/// <exception cref="SLException">Failed.</exception>
		public ASqliteStatement Bind(SLIndexOrName sqlParam, long value)
			=> _Err(SLApi.sqlite3_bind_int64(_st, _B(sqlParam), value), "sqlite3_bind_int64");

		/// <summary>Calls sqlite3_bind_int64. Returns self.</summary>
		/// <exception cref="SLException">Failed.</exception>
		public ASqliteStatement Bind(SLIndexOrName sqlParam, ulong value)
			=> Bind(sqlParam, (long)value);

		/// <summary>Calls sqlite3_bind_int(value ? 1 : 0). Returns self.</summary>
		/// <exception cref="SLException">Failed.</exception>
		public ASqliteStatement Bind(SLIndexOrName sqlParam, bool value)
			=> Bind(sqlParam, value ? 1 : 0);

		/// <summary>Binds an enum value as int or long. Calls sqlite3_bind_int or sqlite3_bind_int64. Returns self.</summary>
		/// <exception cref="SLException">Failed.</exception>
		[MethodImpl(MethodImplOptions.NoInlining)] //ensure that value is copied to the parameter, because must not be smaller than int
		public ASqliteStatement Bind<T>(SLIndexOrName sqlParam, T value) where T : unmanaged, Enum
			=> Bind(sqlParam, sizeof(T) == 8 ? *(long*)&value : *(int*)&value);

		/// <summary>Calls sqlite3_bind_double. Returns self.</summary>
		/// <exception cref="SLException">Failed.</exception>
		public ASqliteStatement Bind(SLIndexOrName sqlParam, double value)
			=> _Err(SLApi.sqlite3_bind_double(_st, _B(sqlParam), value), "sqlite3_bind_double");

		/// <summary>Calls sqlite3_bind_text16. Returns self.</summary>
		/// <exception cref="SLException">Failed.</exception>
		public ASqliteStatement Bind(SLIndexOrName sqlParam, string value)
			=> _Err(SLApi.sqlite3_bind_text16(_st, _B(sqlParam), value, (value?.Length ?? 0) * 2), "sqlite3_bind_text16");

		/// <summary>Calls sqlite3_bind_blob64. Returns self.</summary>
		/// <exception cref="SLException">Failed.</exception>
		public ASqliteStatement Bind(SLIndexOrName sqlParam, void* blob, long nBytes)
			=> _Err(SLApi.sqlite3_bind_blob64(_st, _B(sqlParam), blob, nBytes), "sqlite3_bind_blob64");

		/// <summary>Calls sqlite3_bind_blob64. Returns self.</summary>
		/// <exception cref="SLException">Failed.</exception>
		public ASqliteStatement Bind<T>(SLIndexOrName sqlParam, T[] array) where T : unmanaged
		{
			fixed (T* p = array) return Bind(sqlParam, p, (array?.LongLength ?? 0) * sizeof(T));
		}

		/// <summary>Calls sqlite3_bind_blob64. Returns self.</summary>
		/// <exception cref="SLException">Failed.</exception>
		public ASqliteStatement Bind<T>(SLIndexOrName sqlParam, List<T> list) where T : unmanaged
			=> Bind(sqlParam, list?.ToArray());

		/// <summary>Binds a value as blob. Calls sqlite3_bind_blob64. Returns self.</summary>
		/// <exception cref="SLException">Failed.</exception>
		/// <remarks>Can be any value type that does not contain fields of reference types. Examples: Guid, Point, int, decimal.</remarks>
		public ASqliteStatement BindStruct<T>(SLIndexOrName sqlParam, T value) where T : unmanaged
			=> Bind(sqlParam, &value, sizeof(T));

		/// <summary>Calls sqlite3_bind_null. Returns self.</summary>
		/// <exception cref="SLException">Failed.</exception>
		/// <remarks>Usually don't need to call this function. Unset parameter values are null. The Bind(string/void*/Array/List) functions set null too if the value is null.</remarks>
		public ASqliteStatement BindNull(SLIndexOrName sqlParam)
			=> _Err(SLApi.sqlite3_bind_null(_st, _B(sqlParam)), "sqlite3_bind_null");

		//rejected. 1. Currently we don't have a Blob class. 2. Can do in SQL: zeroblob(nBytes).
		///// <summary>Calls sqlite3_bind_zeroblob. Returns self.</summary>
		///// <exception cref="SLException">Failed.</exception>
		//public Statement BindZeroBlob(SLIndexOrName sqlParam, int nBytes)
		//	=> _Err(SLApi.sqlite3_bind_zeroblob(_st, _B(sqlParam), nBytes), "sqlite3_bind_zeroblob");

		//rejected. DateTime can be stored in many ways. Let users decide how they want to store it, and explicitly convert to long, string, etc.
		///// <summary>Calls sqlite3_bind_int64(value.ToBinary()). Returns self.</summary>
		///// <exception cref="SLException">Failed.</exception>
		//public Statement Bind(SLIndexOrName sqlParam, DateTime value, bool convertToUtc = false)
		//	=> Bind(sqlParam, (convertToUtc ? value.ToUniversalTime() : value).ToBinary());

		/// <summary>
		/// Used by <see cref="BindAll"/>.
		/// </summary>
		internal void BindObject(int i, object v)
		{
			int k = 0;
			switch(v) {
			case null: BindNull(i); break;
			case int x: k = x; goto gi;
			case uint x: k = (int)x; goto gi;
			case bool x: k = x ? 1 : 0; goto gi;
			case long x: Bind(i, x); break;
			case ulong x: Bind(i, x); break;
			case double x: Bind(i, x); break;
			case float x: Bind(i, x); break;
			case string x: Bind(i, x); break;
			case byte x: k = x; goto gi;
			case sbyte x: k = x; goto gi;
			case short x: k = x; goto gi;
			case ushort x: k = x; goto gi;
			case decimal x: Bind(i, &x, sizeof(decimal)); break;
			case Guid x: Bind(i, &x, sizeof(Guid)); break;
			case Enum x:
				switch(x.GetTypeCode()) {
				case TypeCode.Int64: case TypeCode.UInt64: Bind(i, Convert.ToInt64(v)); break;
				default: k = Convert.ToInt32(v); goto gi;
				}
				break;
			//case Array a:
			//	break;
			default:
				//never mind: this func does not support other types supported by other BindX functions. Quite difficult.
				//	To get address: GCHandle.AddrOfPinnedObject.
				//	To get type size, probably need the Unsafe dll or DynamicMethod/ILGenerator.
				//	Or try serialization. Probably slow and limited.
				var t = v.GetType();
				throw new NotSupportedException(t.Name);
				//case DateTime x: Bind(i, x); break;
			}
			return;
			gi: Bind(i, k);
		}

		/// <summary>
		/// Binds multiple values of any supported types.
		/// Returns self.
		/// </summary>
		/// <param name="values">
		/// Values that will replace <c>?</c> characters in sql.
		/// Read about SQL parameters in SQLite website. Example: <see cref="ASqlite"/>.
		/// Supported types:
		/// - int, uint, byte, sbyte, short, ushort - calls sqlite3_bind_int.
		/// - bool - calls sqlite3_bind_int(true?1:0).
		/// - long, ulong - calls sqlite3_bind_int64.
		/// - double, float - calls sqlite3_bind_double.
		/// - string - calls sqlite3_bind_text16.
		/// - decimal - calls sqlite3_bind_blob64.
		/// - Guid - calls sqlite3_bind_blob64.
		/// - An enum type - calls sqlite3_bind_int or sqlite3_bind_int64.
		/// </param>
		/// <exception cref="NotSupportedException">A value is of an unsupported type.</exception>
		/// <exception cref="SLException">Failed.</exception>
		/// <remarks>
		/// For each parameter calls a <b>sqlite3_bind_x</b> function depending on type. Uses index 1, 2 and so on.
		/// This function is an alternative to calling <b>BindX</b> functions for each parameter. However it supports less types and adds boxing overhead.
		/// Does not call sqlite3_reset and sqlite3_clear_bindings. If need, call <see cref="Reset"/> before.
		/// </remarks>
		public ASqliteStatement BindAll(params object[] values)
		{
			if(values != null)
				for(int i = 0; i < values.Length;) {
					object v = values[i];
					BindObject(++i, v);
				}
			return this;
		}
		///// DateTime - calls sqlite3_bind_int64(value.ToBinary()).

		#endregion

		#region get

		int _C(SLIndexOrName p)
		{
			if(p.name == null) return p.index;
			int r = ColumnIndex(p.name);
			if(r < 0) throw new SLException($"Column '{p.name}' does not exist in query results.");
			return r;
		}

		/// <summary>
		/// Returns <c>sqlite3_column_int(column)</c>.
		/// </summary>
		/// <remarks>
		/// Use this function to get integer values of size 4, 2 or 1 bytes: int, uint, short, ushort, byte, sbyte, enum.
		/// </remarks>
		public int GetInt(SLIndexOrName column)
		{
			int r = SLApi.sqlite3_column_int(_st, _C(column));
			if(r == 0) _WarnGet();
			return r;
		}

		/// <summary>
		/// Returns <c>sqlite3_column_int64(column)</c>.
		/// </summary>
		/// <remarks>
		/// Use this function to get integer values of size 8 bytes: long, ulong, 64-bit enum, maybe DateTime.
		/// </remarks>
		public long GetLong(SLIndexOrName column)
		{
			long r = SLApi.sqlite3_column_int64(_st, _C(column));
			if(r == 0) _WarnGet();
			return r;
		}

		/// <summary>
		/// Returns <c>sqlite3_column_int64(column) != 0</c>.
		/// </summary>
		public bool GetBool(SLIndexOrName column) => GetLong(column) != 0;

		///// <summary>
		///// Returns <c>DateTime.FromBinary(sqlite3_column_int64(column))</c>.
		///// </summary>
		///// <param name="column"></param>
		///// <param name="convertToLocal">If the value in database is stored as UTC, convert to local.</param>
		///// <exception cref="ArgumentException">The value in database is not in the valid DateTime range.</exception>
		///// <seealso cref="Bind(int, DateTime, bool)"/>
		//public DateTime GetDateTime(SLIndexOrName column, bool convertToLocal = false)
		//{
		//	var r = DateTime.FromBinary(GetLong(column)); //info: it's OK if 0/null
		//	if(convertToLocal && r.Kind == DateTimeKind.Utc) r = r.ToLocalTime();
		//	return r;
		//}

		/// <summary>
		/// Returns <c>sqlite3_column_double(column)</c>.
		/// </summary>
		public double GetDouble(SLIndexOrName column)
		{
			double r = SLApi.sqlite3_column_double(_st, _C(column));
			if(r == 0) _WarnGet();
			return r;
		}

		/// <summary>
		/// Returns <c>sqlite3_column_text(column)</c> as string.
		/// </summary>
		public string GetText(SLIndexOrName column)
		{
			int icol = _C(column);
			if(_db.IsUtf16) { //both these codes would work, but with long strings can be significantly slower if SQLite has to convert text encoding
				char* t = SLApi.sqlite3_column_text16(_st, icol);
				if(t != null) return new string(t, 0, SLApi.sqlite3_column_bytes16(_st, icol));
			} else {
				byte* t = SLApi.sqlite3_column_text(_st, icol);
				if(t != null) return AConvert.FromUtf8(t, SLApi.sqlite3_column_bytes(_st, icol));
			}
			_WarnGet();
			return null;
		}

		/// <summary>
		/// Returns <c>sqlite3_column_blob(column)</c> and gets blob size.
		/// </summary>
		/// <param name="column"></param>
		/// <param name="nBytes">Blob size.</param>
		public void* GetBlob(SLIndexOrName column, out int nBytes)
		{
			int icol = _C(column);
			void* r = SLApi.sqlite3_column_blob(_st, icol);
			if(r == null) { nBytes = 0; _WarnGet(); } else nBytes = SLApi.sqlite3_column_bytes(_st, icol);
			return r;
		}

		/// <summary>
		/// Returns <c>sqlite3_column_blob(column)</c> as array.
		/// </summary>
		public T[] GetArray<T>(SLIndexOrName column) where T : unmanaged
		{
			var t = GetBlob(column, out int nb);
			if(t == null) return null;
			if(nb == 0) return Array.Empty<T>();
			int size = sizeof(T);
			int ne = nb / size; nb = ne * size;
			var r = new T[ne];
			fixed (T* p = r) Buffer.MemoryCopy(t, p, nb, nb);
			return r;
		}

		/// <summary>
		/// Returns <c>sqlite3_column_blob(column)</c> as List.
		/// </summary>
		public List<T> GetList<T>(SLIndexOrName column) where T : unmanaged
		{
			var t = (T*)GetBlob(column, out int nb);
			if(t == null) return null;
			int ne = nb / sizeof(T);
			var r = new List<T>(ne);
			for(int i = 0; i < ne; i++) r.Add(t[i]);
			return r;
		}

		/// <summary>
		/// Returns <c>sqlite3_column_blob(column)</c> as a variable of any value type that does not have fields of reference types.
		/// </summary>
		public T GetStruct<T>(SLIndexOrName column) where T : unmanaged
		{
			var t = (T*)GetBlob(column, out int nb);
			T r = default;
			if(t != null && nb == sizeof(T)) Buffer.MemoryCopy(t, &r, nb, nb);
			return r;
		}

		/// <summary>
		/// sqlite3_column_count.
		/// </summary>
		public int ColumnCount => SLApi.sqlite3_column_count(_st);

		/// <summary>
		/// sqlite3_column_name.
		/// </summary>
		public string ColumnName(int index) => AConvert.FromUtf8(SLApi.sqlite3_column_name(_st, index));

		/// <summary>
		/// Finds column by name in results.
		/// Returns 0-based index, or -1 if not found.
		/// </summary>
		/// <param name="name">Column name in results, as returned by sqlite3_column_name. Case-sensitive.</param>
		public int ColumnIndex(string name)
		{
			int n = ColumnCount;
			if(n > 0 && !name.NE()) {
				if(AStringUtil.IsAscii(name)) {
					for(int i = 0; i < n; i++) {
						byte* b = SLApi.sqlite3_column_name(_st, i);
						if(BytePtr_.AsciiEq(b, name)) return i;
					}
				} else {
					var bname = AConvert.ToUtf8(name);
					for(int i = 0; i < n; i++) {
						byte* b = SLApi.sqlite3_column_name(_st, i);
						if(BytePtr_.Eq(b, bname)) return i;
					}
				}
			}
			return -1;
		}

		#endregion

		#region util

		[DebuggerStepThrough]
		ASqliteStatement _Err(SLError r, string func)
		{
			if(r != 0 && r != SLError.Row) throw new SLException(r, _db, func);
			return this;
		}

		//rejected: not thread-safe. Other .NET wrappers ignore it too.
		//void _Err(string func)
		//{
		//	var r = SLApi.sqlite3_errcode(_db);
		//	if(r != 0 && r != SLError.Row) throw new SLException(r, _db, func);
		//}

		/// <summary>
		/// Called by GetX functions when sqlite3_column_x returns null/0.
		/// Shows warning if sqlite3_errcode is not 0 or Row.
		/// Does not throw exception because it is not thread-safe.
		/// </summary>
		/// <param name="func"></param>
		void _WarnGet([CallerMemberName] string func = null)
		{
			var r = SLApi.sqlite3_errcode(_db);
			if(r != 0 && r != SLError.Row) SLUtil.Warn(r, func, "Note: it may be a false positive if this database connection is used by multiple threads simultaneously without locking.");
		}

		#endregion
	}
}

namespace Au.Types
{
	/// <summary>
	/// A SQLite transaction or savepoint. The main purpose is to automatically rollback if not explicitly committed.
	/// Usage: <c>using(var trans = new SLTransaction(db)) { ... trans.Commit(); }</c>
	/// </summary>
	public struct SLTransaction : IDisposable
	{
		ASqlite _db;

		/// <summary>
		/// Gets or sets SQL to execute when disposing this variable if not called <see cref="Commit"/> or <see cref="Rollback"/>.
		/// Initially = parameter <c>sqlOfDispose</c> of constructor.
		/// </summary>
		public string SqlOfDispose { get; set; }

		//public string ErrorMessage { get; set; }

		/// <summary>
		/// Begins a SQLite transaction and prepares for automatic rollback if not explicitly committed.
		/// Usage: <c>using(var trans = new SLTransaction(db)) { ... trans.Commit(); }</c>
		/// </summary>
		/// <param name="db"></param>
		/// <param name="sql">SQL to execute now. Default "BEGIN". For nested transaction use "SAVEPOINT name".</param>
		/// <param name="sqlOfDispose">SQL to execute when disposing this variable if not called <see cref="Commit"/> or <see cref="Rollback"/>. Default "ROLLBACK". For nested transaction use "ROLLBACK TO name". See also: <see cref="SqlOfDispose"/>.</param>
		/// <exception cref="SLException">Failed to execute sql.</exception>
		public SLTransaction(ASqlite db, string sql = "BEGIN", string sqlOfDispose = "ROLLBACK") : this()
		{
			if(db == null) throw new ArgumentNullException();
			db.Execute(sql);
			_db = db;
			SqlOfDispose = sqlOfDispose;
		}

		/// <summary>
		/// Calls <see cref="Rollback"/> if not called <see cref="Commit"/> or <see cref="Rollback"/>.
		/// </summary>
		/// <exception cref="SLException">Failed to execute <see cref="SqlOfDispose"/>.</exception>
		public void Dispose()
		{
			if(_db != null) Rollback(SqlOfDispose);
		}

		/// <summary>
		/// Executes a rollback SQL (if in transaction) and disables <see cref="Dispose"/>.
		/// Usually don't need to call this function explicitly. It is implicitly called when disposing this variable if the transaction was not committed.
		/// </summary>
		/// <param name="sql">SQL to execute. Default: <see cref="SqlOfDispose"/>.</param>
		/// <exception cref="SLException">Failed to execute sql.</exception>
		public void Rollback(string sql = null)
		{
			if(_db == null) throw new InvalidOperationException();
			if(sql == null) sql = SqlOfDispose;
			//if(ErrorMessage!=null) AOutput.Write(ErrorMessage);
			if(_db.IsInTransaction) //in some cases sqlite rolls back on error
				_db.Execute(sql);
			_db = null;
		}

		/// <summary>
		/// Executes a commit SQL and disables <see cref="Dispose"/>.
		/// </summary>
		/// <param name="sql">SQL to execute. Default "COMMIT". For nested transaction use "RELEASE name".</param>
		/// <exception cref="SLException">Failed to execute sql.</exception>
		public void Commit(string sql = "COMMIT")
		{
			if(_db == null) throw new InvalidOperationException();
			_db.Execute(sql);
			_db = null;
		}
	}

	/// <summary>
	/// Used for parameter types of some <see cref="ASqliteStatement"/> functions.
	/// Has implicit conversions from int and string. If int, the value is interpreted as index. If string - as name.
	/// </summary>
	public struct SLIndexOrName
	{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
		public int index;
		public string name;
		SLIndexOrName(int index) { this.index = index; this.name = null; }
		SLIndexOrName(string name) { this.index = 0; this.name = name; }
		public static implicit operator SLIndexOrName(int index) => new SLIndexOrName(index);
		public static implicit operator SLIndexOrName(string name) => new SLIndexOrName(name);
#pragma warning restore CS1591
	}

	/// <summary>
	/// Exception thrown by <see cref="ASqlite"/>, <see cref="ASqliteStatement"/> and related types.
	/// </summary>
	public class SLException : Exception
	{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
		public SLException(string message) : base(message) { }

		internal SLException(SLError r, string prefix = null, string suffix = null)
			: base(SLUtil.Concat(prefix, SLApi.Errstr(r), suffix))
		{
			Debug.Assert(r != 0 && r != SLError.Done && r != SLError.Row);
			ErrorCode = r;
		}

		internal SLException(SLError r, IntPtr db, string prefix = null)
			: base(SLUtil.Concat(prefix, r, db))
		{
			Debug.Assert(r != 0 && r != SLError.Done && r != SLError.Row);
			ErrorCode = r;
		}
#pragma warning restore CS1591

		/// <summary>
		/// The called SQLite API function returned this error code.
		/// </summary>
		public SLError ErrorCode { get; private set; }
	}

	internal static unsafe class SLUtil
	{
		internal static string ToStringAndFree(byte* utf8)
		{
			var r = AConvert.FromUtf8(utf8);
			SLApi.sqlite3_free(utf8);
			return r;
		}

		//currently not used
		//internal static void Err(SLError r, ASqlite db, string func)
		//{
		//	if(r != 0) throw new SLException(r, db, func);
		//}

		internal static void Warn(SLError r, string func, string suffix = null)
		{
			if(r != 0) AWarning.Write(SLUtil.Concat(func, SLApi.Errstr(r), suffix));
		}

		internal static string Concat(string s1, string s2, string s3)
		{
			using(new StringBuilder_(out var b)) {
				_Append(s1); _Append(s2); if(s3 != s2) _Append(s3);
				return b.ToString();

				void _Append(string s) => b.AppendSentence(s, s?.Starts("sqlite3_") ?? false); //avoid uppercase for function names
			}
		}

		internal static string Concat(string s1, SLError r, IntPtr db)
			=> Concat(s1, SLApi.Errstr(r), SLApi.sqlite3_errcode(db) == 0 ? null : SLApi.Errmsg(db));
	}

}