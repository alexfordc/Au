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

using System.Text.RegularExpressions; //for XML doc links

using Au.Types;

namespace Au
{
	/// <summary>
	/// PCRE regular expression.
	/// </summary>
	/// <remarks>
	/// PCRE is a regular expression library: <see href="https://www.pcre.org/"/>.
	/// PCRE regular expression syntax: <see href="https://www.pcre.org/current/doc/html/pcre2pattern.html">full</see>, <see href="https://www.pcre.org/current/doc/html/pcre2syntax.html">short</see>.
	/// Some websites with tutorials and info: <see href="http://www.rexegg.com/">rexegg</see>, <see href="https://www.regular-expressions.info/">regular-expressions.info</see>.
	/// 
	/// This class is an alternative to the .NET <see cref="Regex"/> class. The regular expression syntax is similar. PCRE has some features unavailable in .NET, and vice versa. In most cases PCRE is about 2 times faster. You can use any of these classes. Functions of <see cref="AAcc"/> class support only PCRE.
	/// 
	/// Terms used in this documentation and in names of functions and types:
	/// - <i>regular expression</i> - regular expression string. Also known as <i>pattern</i>.
	/// - <i>subject string</i> - the string in which to search for the regular expression. Also known as <i>input string</i>.
	/// - <i>match</i> - the part (substring) of the subject string that matches the regular expression.
	/// - <i>groups</i> - regular expression parts enclosed in (). Except non-capturing parts, like (?:...) and (?options). Also known as <i>capturing group</i>, <i>capturing subpattern</i>. Often term <i>group</i> also is used for group matches.
	/// - <i>group match</i> - the part (substring) of the subject string that matches the group. Also known as <i>captured substring</i>.
	/// 
	/// This library uses an unmanaged code dll AuCpp.dll that contains PCRE code. This class is a managed wrapper for it. The main PCRE API functions used by this class are <see href="https://www.pcre.org/current/doc/html/pcre2api.html">pcre2_compile and pcre2_match</see>. The <b>ARegex</b> constructor calls <b>pcre2_compile</b> and stores the compiled code in the variable. Other <b>ARegex</b> functions call <b>pcre2_match</b>. Compiling to native code (JIT) is not supported.
	/// 
	/// An <b>ARegex</b> variable can be used by multiple threads simultaneously.
	/// 
	/// Also there are several <b>String</b> extension methods that use this class. The string variable is the subject string. These methods create and use cached <b>ARegex</b> instances for speed. The <b>ARegex</b> constructor does not use caching.
	/// </remarks>
	/// <example>
	/// <code><![CDATA[
	/// var s = "one two22, three333,four"; //subject string
	/// var x = new ARegex(@"\b(\w+?)(\d+)\b"); //regular expression
	///  
	///  AOutput.Write("//IsMatch:");
	/// AOutput.Write(x.IsMatch(s));
	///  
	///  AOutput.Write("//Match:");
	/// if(x.Match(s, out var m)) AOutput.Write(m.Value, m[1].Value, m[2].Value);
	///  
	///  AOutput.Write("//FindAll with foreach:");
	/// foreach(var v in x.FindAll(s)) AOutput.Write(v.Value, v[1].Value, v[2].Value);
	///  AOutput.Write("//FindAllS, get only strings of group 2:");
	/// AOutput.Write(x.FindAllS(s, 2));
	///  
	///  AOutput.Write("//Replace:");
	/// AOutput.Write(x.Replace(s, "'$2$1'"));
	///  AOutput.Write("//Replace with callback:");
	/// AOutput.Write(x.Replace(s, o => o.Value.Upper()));
	///  AOutput.Write("//Replace with callback and ExpandReplacement:");
	/// AOutput.Write(x.Replace(s, o => { if(o.Length > 5) return o.ExpandReplacement("'$2$1'"); else return o[1].Value; }));
	///  
	///  AOutput.Write("//Split:");
	/// AOutput.Write(new ARegex(@" *, *").Split(s));
	/// ]]></code>
	///  Examples with <b>String</b> extension methods. 
	/// <code><![CDATA[
	/// var s = "one two22, three333,four"; //subject string
	/// var rx = @"\b(\w+?)(\d+)\b"; //regular expression
	///  
	///  AOutput.Write("//RegexIsMatch:");
	/// AOutput.Write(s.RegexIsMatch(rx));
	///  
	///  AOutput.Write("//RegexMatch:");
	/// if(s.RegexMatch(rx, out var m)) AOutput.Write(m.Value, m[1].Value, m[2].Value);
	///  
	///  AOutput.Write("//RegexMatch, get only string:");
	/// if(s.RegexMatch(rx, 0, out var s0)) AOutput.Write(s0);
	///  AOutput.Write("//RegexMatch, get only string of group 1:");
	/// if(s.RegexMatch(rx, 1, out var s1)) AOutput.Write(s1);
	///  
	///  AOutput.Write("//RegexFindAll with foreach:");
	/// foreach(var v in s.RegexFindAll(rx)) AOutput.Write(v.Value, v[1].Value, v[2].Value);
	///  
	///  AOutput.Write("//RegexFindAll with foreach, get only strings:");
	/// foreach(var v in s.RegexFindAll(rx, 0)) AOutput.Write(v);
	///  AOutput.Write("//RegexFindAll with foreach, get only strings of group 2:");
	/// foreach(var v in s.RegexFindAll(rx, 2)) AOutput.Write(v);
	///  
	///  AOutput.Write("//RegexFindAll, get array:");
	/// if(s.RegexFindAll(rx, out var am)) foreach(var k in am) AOutput.Write(k.Value, k[1].Value, k[2].Value);
	///  
	///  AOutput.Write("//RegexFindAll, get array of strings:");
	/// if(s.RegexFindAll(rx, 0, out var av)) AOutput.Write(av);
	///  AOutput.Write("//RegexFindAll, get array of group 2 strings:");
	/// if(s.RegexFindAll(rx, 2, out var ag)) AOutput.Write(ag);
	///  
	///  AOutput.Write("//RegexReplace:");
	/// AOutput.Write(s.RegexReplace(rx, "'$2$1'"));
	///  
	///  AOutput.Write("//RegexReplace with callback:");
	/// AOutput.Write(s.RegexReplace(rx, o => o.Value.Upper()));
	///  AOutput.Write("//RegexReplace with callback and ExpandReplacement:");
	/// AOutput.Write(s.RegexReplace(rx, o => { if(o.Length > 5) return o.ExpandReplacement("'$2$1'"); else return o[1].Value; }));
	///  
	///  AOutput.Write("//RegexReplace, get replacement count:");
	/// if(0 != s.RegexReplace(rx, "'$2$1'", out var s2)) AOutput.Write(s2);
	///  
	///  AOutput.Write("//RegexReplace with callback, get replacement count:");
	/// if(0 != s.RegexReplace(rx, o => o.Value.Upper(), out var s3)) AOutput.Write(s3);
	///  
	///  AOutput.Write("//RegexSplit:");
	/// AOutput.Write(s.RegexSplit(@" *, *"));
	/// ]]></code></example>
	public unsafe class ARegex
	{
		IntPtr _codeUnsafe; //pcre2_code_16*. Don't pass to PCRE API directly, because then GC can collect this object
		Cpp.PcreCalloutT _pcreCallout; //our callout that calls the user's callout. This field protects the delegates from GC.
		byte _matchFlags; //RXMatchFlags specified in hi byte of ctor flags

		internal HandleRef _CodeHR => new HandleRef(this, _codeUnsafe); //pass this to PCRE API

		/// <summary>
		/// Compiles regular expression string.
		/// </summary>
		/// <param name="rx">Regular expression. Cannot be null.</param>
		/// <param name="flags">
		/// Options.
		/// Default 0. Flag UTF is implicitly added if <i>rx</i> contains non-ASCII characters and there is no flag NEVER_UTF.
		/// </param>
		/// <exception cref="ArgumentNullException"></exception>
		/// <exception cref="ArgumentException">Invalid regular expression. Or failed to compile it for some other reason (unlikely).</exception>
		/// <remarks>
		/// Calls PCRE API function <see href="https://www.pcre.org/current/doc/html/pcre2api.html">pcre2_compile</see>.
		/// 
		/// PCRE regular expression syntax: <see href="https://www.pcre.org/current/doc/html/pcre2pattern.html">full</see>, <see href="https://www.pcre.org/current/doc/html/pcre2syntax.html">short</see>.
		/// 
		/// Examples in class help: <see cref="ARegex"/>.
		/// </remarks>
		public ARegex([ParamString(PSFormat.ARegex)] string rx, RXFlags flags = 0)
		{
			if(rx == null) throw new ArgumentNullException();
			_matchFlags = (byte)((ulong)flags >> 56); flags = (RXFlags)((ulong)flags & 0xffffff_ffffffff);
			_codeUnsafe = Cpp.Cpp_RegexCompile(rx, rx.Length, flags, out int codeSize, out BSTR errStr);
			if(_codeUnsafe == default) throw new ArgumentException(errStr.ToStringAndDispose());
			GC.AddMemoryPressure(codeSize);
		}

		///
		~ARegex()
		{
			//AOutput.Write("dtor");
			if(_codeUnsafe == default) return;
			int codeSize = Cpp.Cpp_RegexDtor(_codeUnsafe);
			GC.RemoveMemoryPressure(codeSize);
		}

		/// <summary>
		/// Sets callout callback function.
		/// </summary>
		/// <value>Callback delegate (eg lambda) or null.</value>
		/// <remarks>
		/// Callouts can be used to: 1. Track the matching progress. 2. Get all instances of a group that can match multiple times. 3. Evaluate and reject some matches or match parts. 4. Etc.
		/// The callback function is called by <see cref="IsMatch"/>, <see cref="Match"/>, <see cref="FindAll"/>, <see cref="Replace"/>, <see cref="Split"/> and similar functions, when they reach callout points in regular expression. To insert callout points use (?C), (?C1), (?C2), (?C'name') etc or pass flag AUTO_CALLOUT to the constructor.
		/// More info in PCRE help topic <see href="https://www.pcre.org/current/doc/html/pcre2callout.html">pcre2callout</see>.
		/// See also: <see href="http://www.rexegg.com/pcre-callouts.html"/>
		/// </remarks>
		/// <example>
		/// Track the matching progress.
		/// <code><![CDATA[
		/// var s = "text <a href='url'>link</a> text";
		/// var rx = @"(?C1)<a (?C2)href='.+?'>(?C3)[^<]*(?C4)</a>";
		/// var x = new ARegex(rx);
		/// x.Callout = o => { AOutput.Write(o.callout_number, o.current_position, s.Substring(o.start_match, o.current_position), rx.Substring(o.pattern_position, o.next_item_length)); };
		/// AOutput.Write(x.IsMatch(s));
		/// ]]></code>
		/// Track the matching progress with flag AUTO_CALLOUT.
		/// <code><![CDATA[
		/// var s = "one 'two' three";
		/// var rx = @"'(.+?)'";
		/// var x = new ARegex(rx, RXFlags.AUTO_CALLOUT);
		/// x.Callout = o => AOutput.Write(o.current_position, o.pattern_position, rx.Substring(o.pattern_position, o.next_item_length));
		/// AOutput.Write(x.IsMatch(s));
		/// ]]></code>
		/// Get all instances of a group that can match multiple times.
		/// <code><![CDATA[
		/// var s = "BEGIN 111 2222 333 END";
		/// var x = new ARegex(@"^(\w+) (?:(\d+) (?C1))+(\w+)$");
		/// var a = new List<string>();
		/// x.Callout = o => a.Add(o.LastGroupValue);
		/// if(!x.Match(s, out var m)) { AOutput.Write("no match"); return; }
		/// AOutput.Write(m[1]);
		/// AOutput.Write(a); //all numbers. m[2] contains only the last number.
		/// AOutput.Write(m[3]);
		/// ]]></code>
		/// Evaluate and reject some matches or match parts. This code rejects matches longer than 5.
		/// <code><![CDATA[
		/// var s = "one 123-5 two 12-456 three 1-34 four";
		/// var x = new ARegex(@"\b\d+-\d+\b(?C1)");
		/// x.Callout = o => { int len = o.current_position - o.start_match; /*AOutput.Write(len);*/ if(len > 5) o.Result = 1; };
		/// AOutput.Write(x.FindAllS(s));
		/// ]]></code>
		/// </example>
		public RXCalloutFunc Callout {
			set {
				lock(this) {
					if(value == null) {
						_pcreCallout = null;
					} else {
						_pcreCallout = (void* calloutBlock, void* param) => {
							var b = new RXCalloutData(calloutBlock);
							value(b);
							return b.Result;
						};
					}
				}
			}
		}

		/// <summary>
		/// Finds a named group and returns its 1-based index.
		/// Returns -1 if not found.
		/// </summary>
		/// <param name="groupName">
		/// Group name.
		/// In regular expression, to set name of group <c>(text)</c>, use <c>(?&lt;NAME&gt;text)</c>.
		/// </param>
		/// <exception cref="ArgumentNullException"></exception>
		/// <exception cref="ArgumentException">More than 1 group have this name.</exception>
		/// <seealso cref="RXMatch.GroupNumberFromName(string)"/>
		/// <seealso cref="RXMatch.GroupNumberFromName(string, out bool)"/>
		public int GroupNumberFromName(string groupName)
		{
			if(groupName == null) throw new ArgumentNullException();
			fixed(char* p = groupName) {
				int R = Cpp.pcre2_substring_nametable_scan(_CodeHR, p, null, null);
				if(R <= 0) {
					if(R == -50) throw new ArgumentException("Multiple groups have name " + groupName); //-50 PCRE2_ERROR_NOUNIQUESUBSTRING
					R = -1;
				}
				return R;
			}
		}

		/// <summary>
		/// Returns true if string <i>s</i> matches this regular expression.
		/// </summary>
		/// <param name="s">
		/// Subject string.
		/// If null, always returns false, even if the regular expression matches empty string.
		/// </param>
		/// <param name="range">
		/// Start and end offsets in the subject string. If null (default), uses whole string.
		/// Examples: <c>i..j</c> (from i to j), <c>i..</c> (from i to the end), <c>..j</c> (from 0 to j).
		/// The subject part before the start index is not ignored if regular expression starts with a lookbehind assertion or anchor, eg <c>^</c> or <c>\b</c> or <c>(?&lt;=...)</c>. Instead of <c>^</c> you can use <c>\G</c>. More info in PCRE documentation topic <see href="https://www.pcre.org/current/doc/html/pcre2api.html">pcre2api</see>, chapter "The string to be matched by pcre2_match()".
		/// The subject part after the end index is always ignored.
		/// </param>
		/// <param name="matchFlags">Options.
		/// The same options also can be set in <b>ARegex</b> constructor's <i>flags</i>. Constructor's flags and <i>matchFlags</i> are added, which means that <i>matchFlags</i> cannot unset flags set by constructor.
		/// </param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <remarks>
		/// If partial match, returns true too. Partial match is possible if used a PARTIAL_ flag.
		/// 
		/// This function is similar to <see cref="Regex.IsMatch(string)"/>.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two22 three333 four";
		/// var x = new ARegex(@"\b(\w+?)(\d+)\b");
		/// AOutput.Write(x.IsMatch(s));
		/// ]]></code>
		/// </example>
		public bool IsMatch(string s, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			if(!_GetStartEnd(s, range, out int start, out int end)) return false;
			int rc = Cpp.Cpp_RegexMatch(_CodeHR, s, end, start, _GetMatchFlags(matchFlags), _pcreCallout, null, out BSTR errStr);
			//AOutput.Write(rc);
			//info: 0 is partial match, -1 is no match, <-1 is error
			if(rc < -1) throw new AuException(errStr.ToStringAndDispose());
			return rc >= 0;
		}

		/// <summary>
		/// Returns true if string <i>s</i> matches this regular expression.
		/// Gets match info as <see cref="RXMatch"/>.
		/// </summary>
		/// <param name="s">
		/// Subject string.
		/// If null, always returns false, even if the regular expression matches empty string.
		/// </param>
		/// <param name="result">Receives match info. Read more in Remarks.</param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <remarks>
		/// If full match, returns true, and <i>result</i> contains the match and all groups that exist in the regular expressions.
		/// If partial match, returns true, and <i>result</i> contains the match without groups. Partial match is possible if used a PARTIAL_ flag.
		/// If no match, returns false, and <i>result</i> normally is null. But if a mark is available, <i>result</i> is an object with two valid properties - <see cref="RXMatch.Exists"/> (false) and <see cref="RXMatch.Mark"/>; other properties have undefined values or throw exception.
		/// 
		/// This function is similar to <see cref="Regex.Match(string)"/>.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two22 three333 four";
		/// var x = new ARegex(@"\b(\w+?)(\d+)\b");
		/// if(x.Match(s, out var m)) AOutput.Write(m.Value, m[1].Value, m[2].Value);
		/// ]]></code>
		/// </example>
		public bool Match(string s, out RXMatch result, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			result = null;
			int rc = _Match(s, 0, range, matchFlags, out var m);
			if(rc >= 0 || m.mark != null) {
				result = new RXMatch(this, s, rc, in m);
			}
			return rc >= 0;
		}

		/// <summary>
		/// Returns true if string <i>s</i> matches this regular expression.
		/// Gets whole match or some group, as <see cref="RXGroup"/> (index, length, value).
		/// </summary>
		/// <param name="s">
		/// Subject string.
		/// If null, always returns false, even if the regular expression matches empty string.
		/// </param>
		/// <param name="result">Receives match info.</param>
		/// <param name="group">
		/// Group number (1-based index) of result. If 0 (default) - whole match.
		/// See also <see cref="GroupNumberFromName"/>.
		/// </param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>group</i> or <i>range</i>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <remarks>
		/// This function is a simplified version of <see cref="Match"/>.
		/// If full match, returns true, and <i>result</i> contains the match or the specifed group.
		/// If partial match, returns true too. Partial match is possible if used a PARTIAL_ flag. Then cannot get groups, therefore <i>group</i> should be 0.
		/// If no match, returns false, and <i>result</i> is empty.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two22 three333 four";
		/// var x = new ARegex(@"\b(\w+?)(\d+)\b");
		/// if(x.MatchG(s, out var g)) AOutput.Write(g.Value, g.Start);
		/// ]]></code>
		/// </example>
		public bool MatchG(string s, out RXGroup result, int group = 0, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			int rc = _Match(s, group, range, matchFlags, out var m);
			if(rc < 0) {
				result = default;
				return false;
			}
			result = new RXGroup(s, m.vec[group]);
			return true;
		}

		/// <summary>
		/// Returns true if string <i>s</i> matches this regular expression.
		/// Gets whole match or some group, as string.
		/// </summary>
		/// <param name="s">
		/// Subject string.
		/// If null, always returns false, even if the regular expression matches empty string.
		/// </param>
		/// <param name="result">Receives the match value.</param>
		/// <param name="group">
		/// Group number (1-based index) of result. If 0 (default) - whole match.
		/// See also <see cref="GroupNumberFromName"/>.
		/// </param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>group</i> or <i>range</i>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <remarks>
		/// This function is a simplified version of <see cref="Match"/> and <see cref="MatchG"/>.
		/// If full match, returns true, and <i>result</i> contains the value of the match or of the specifed group.
		/// If partial match, returns true too. Partial match is possible if used a PARTIAL_ flag. Then cannot get groups, therefore <i>group</i> should be 0.
		/// If no match, returns false, and <i>result</i> is null.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two22 three333 four";
		/// var x = new ARegex(@"\b(\w+?)(\d+)\b");
		/// if(x.MatchS(s, out var v)) AOutput.Write(v);
		/// ]]></code>
		/// </example>
		public bool MatchS(string s, out string result, int group = 0, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			result = null;
			if(!MatchG(s, out var g, group, range, matchFlags)) return false;
			result = g.Value;
			return true;
		}

		//If s (subject) is null, returns false.
		//Else gets real range in s. Throws ArgumentOutOfRangeException if invalid.
		static bool _GetStartEnd(string s, Range? range, out int start, out int end)
		{
			if(s == null) { start = end = 0; return false; }
			(start, end) = range?.GetStartEnd(s.Length) ?? (0, s.Length);
			return true;
		}

		RXMatchFlags _GetMatchFlags(RXMatchFlags matchFlags, bool throwIfPartial = false)
		{
			var f = (RXMatchFlags)_matchFlags | matchFlags;
			if(throwIfPartial) {
				if(0 != (f & (RXMatchFlags.PARTIAL_SOFT | RXMatchFlags.PARTIAL_HARD)))
					throw new ArgumentException("This function does not support PARTIAL_ flags.", nameof(matchFlags));
			}
			return f;
		}

		//Calls Cpp_RegexMatch and returns its results.
		//Throws if it returns less than -1. Throws if invalid start/end/group.
		//m.vec array is thread_local. Next call reallocates/overwrites it, except when called by a callout of the same call.
		//m.mark is set even if no match, if available.
		//s - subject. If null, returns rc -1.
		//group - 0 or group number. Used only to throw if invalid.
		int _Match(string s, int group, Range? range, RXMatchFlags matchFlags, out Cpp.RegexMatch m)
		{
			if(!_GetStartEnd(s, range, out int start, out int end)) { m = default; return -1; }
			int rc = Cpp.Cpp_RegexMatch(_CodeHR, s, end, start, _GetMatchFlags(matchFlags), _pcreCallout, out m, out BSTR errStr);
			//AOutput.Write(rc);
			//info: 0 is partial match, -1 is no match, <-1 is error
			if(rc < -1) throw new AuException(errStr.ToStringAndDispose());
			if(group != 0 && rc >= 0 && (uint)group >= m.vecCount) throw new ArgumentOutOfRangeException(nameof(group));
			return rc;
		}

		//Used by FindAllX and ReplaceAllX to easily find matches in loop.
		struct _MatchEnum
		{
			ARegex _regex;
			string _subject;
			Cpp.RegexMatch _m;
			RXMatchFlags _matchFlags;
			int _group, _from, _to, _maxCount, _rc;
			public int foundCount;

			//Calls _GetFromTo and inits fields. Throws if s is null or if invalid start/end or used 'partial' flags.
			public _MatchEnum(ARegex regex, string s, int group, Range? range, RXMatchFlags matchFlags, int maxCount = -1)
			{
				_regex = regex; _subject = s; _group = group;
				if(!_GetStartEnd(s, range, out _from, out _to)) throw new ArgumentNullException(nameof(s));
				_matchFlags = regex._GetMatchFlags(matchFlags, throwIfPartial: true);
				_maxCount = maxCount;
				foundCount = _rc = 0;
				_m = default;
			}

			//Calls Cpp_RegexMatch, remembers its results, increments foundCount if found.
			//Returns false if it returns -1. Throws if it returns < -1. Throws if invalid group.
			//To get results, use properties Match or GroupX. Don't call Next or any other match function before it.
			public bool Next()
			{
				if(foundCount >= (uint)_maxCount) return false;
				_rc = Cpp.Cpp_RegexMatch(_regex._CodeHR, _subject, _to, _from, _matchFlags, _regex._pcreCallout, out _m, out BSTR errStr);
				//AOutput.Write(_rc);
				//info: 0 cannot be (partial match), -1 is no match, <-1 is error
				if(_rc < 0) {
					if(_rc < -1) throw new AuException(errStr.ToStringAndDispose());
					return false;
				}
				if(_group != 0 && (uint)_group >= _m.vecCount) throw new ArgumentOutOfRangeException("group");
				_SetNextFrom();
				_matchFlags |= RXMatchFlags.NO_UTF_CHECK;
				foundCount++;
				return true;
			}

			void _SetNextFrom()
			{
				var p = _m.vec[0]; //x=start, y=end
				_from = p.y;
				//empty match?
				if(_from <= p.x) {
					if(_from < p.x) throw new ArgumentException(@"This function does not support (?=...\K).");
					if(++_from < _to) {
						var c = _subject[_from];
						if(c == '\n') { //skip \n if inside \r\n
							if(_subject[_from - 1] == '\r') _from++;
						} else if((c & 0xfc00) == 0xdc00) { //skip the second part of surrogate pair
							if(0 != (_regex._InfoAllOptions & RXFlags.UTF)) _from++;
						}
					}
					if(_from > _to) _maxCount = 0;
				}
			}

			public RXMatch Match => new RXMatch(_regex, _subject, _rc, in _m);

			public POINT GroupP => _m.vec[_group];

			public RXGroup GroupG => new RXGroup(_subject, GroupP);

			public string GroupS { get { var p = GroupP; return p.x < 0 ? null : _subject.Substring(p.x, p.y - p.x); } }
		}

		/// <summary>
		/// Finds all match instances of the regular expression.
		/// Returns a lazy IEnumerable&lt;<see cref="RXMatch"/>&gt; object that can be used with foreach.
		/// </summary>
		/// <param name="s">Subject string. Cannot be null.</param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentNullException"><i>s</i> is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">1. Used a PARTIAL_ flag. 2. The regular expression contains <c>(?=...\K)</c>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <remarks>
		/// This function is similar to <see cref="Regex.Matches(string)"/>.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two22 three333 four";
		/// var x = new ARegex(@"\b(\w+?)(\d+)\b");
		/// foreach(var m in x.FindAll(s)) AOutput.Write(m.Value, m[1].Value, m[2].Value);
		/// ]]></code>
		/// </example>
		public IEnumerable<RXMatch> FindAll(string s, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			var e = new _MatchEnum(this, s, 0, range, matchFlags);
			while(e.Next()) yield return e.Match;
		}

		/// <summary>
		/// Finds all match instances of the regular expression. Gets array of <see cref="RXMatch"/>.
		/// Returns true if found 1 or more matches.
		/// </summary>
		/// <param name="s">Subject string. Cannot be null.</param>
		/// <param name="result">Receives all found matches.</param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentNullException"><i>s</i> is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">1. Used a PARTIAL_ flag. 2. The regular expression contains <c>(?=...\K)</c>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <remarks>
		/// This function is similar to <see cref="Regex.Matches(string)"/>.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two22 three333 four";
		/// var x = new ARegex(@"\b(\w+?)(\d+)\b");
		/// if(!x.FindAll(s, out var a)) { AOutput.Write("not found"); return; }
		/// foreach(var m in a) AOutput.Write(m.Value, m[1].Value, m[2].Value);
		/// ]]></code>
		/// </example>
		public bool FindAll(string s, out RXMatch[] result, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			result = FindAll(s, range, matchFlags).ToArray();
			return result.Length != 0;
		}

		/// <summary>
		/// Finds all match instances of the regular expression.
		/// Returns a lazy IEnumerable&lt;<see cref="RXGroup"/>&gt; object that can be used with foreach.
		/// </summary>
		/// <param name="s">Subject string. Cannot be null.</param>
		/// <param name="group">
		/// Group number (1-based index) of results. If 0 (default) - whole match.
		/// See also <see cref="GroupNumberFromName"/>.
		/// </param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentNullException"><i>s</i> is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>group</i> or <i>range</i>.</exception>
		/// <exception cref="ArgumentException">1. Used a PARTIAL_ flag. 2. The regular expression contains <c>(?=...\K)</c>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two three";
		/// var x = new ARegex(@"\b\w+\b");
		/// foreach(var g in x.FindAllG(s)) AOutput.Write(g.Start, g.Value);
		/// ]]></code>
		/// </example>
		public IEnumerable<RXGroup> FindAllG(string s, int group = 0, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			var e = new _MatchEnum(this, s, group, range, matchFlags);
			while(e.Next()) yield return e.GroupG;
		}

		/// <summary>
		/// Finds all match instances of the regular expression. Gets array of <see cref="RXGroup"/> (index, length, value).
		/// Returns true if found 1 or more matches.
		/// </summary>
		/// <param name="s">Subject string. Cannot be null.</param>
		/// <param name="result">Receives all found matches.</param>
		/// <param name="group">
		/// Group number (1-based index) of results. If 0 (default) - whole match.
		/// See also <see cref="GroupNumberFromName"/>.
		/// </param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentNullException"><i>s</i> is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>group</i> or <i>range</i>.</exception>
		/// <exception cref="ArgumentException">1. Used a PARTIAL_ flag. 2. The regular expression contains <c>(?=...\K)</c>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two three";
		/// var x = new ARegex(@"\b\w+\b");
		/// if(!x.FindAllG(s, out var a)) { AOutput.Write("not found"); return; }
		/// foreach(var g in a) AOutput.Write(g.Start, g.Value);
		/// ]]></code>
		/// </example>
		public bool FindAllG(string s, out RXGroup[] result, int group = 0, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			result = FindAllG(s, group, range, matchFlags).ToArray();
			return result.Length != 0;
		}

		/// <summary>
		/// Finds all match instances of the regular expression.
		/// Returns a lazy IEnumerable&lt;string&gt; object that can be used with foreach.
		/// </summary>
		/// <param name="s">Subject string. Cannot be null.</param>
		/// <param name="group">
		/// Group number (1-based index) of results. If 0 (default) - whole match.
		/// See also <see cref="GroupNumberFromName"/>.
		/// </param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentNullException"><i>s</i> is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>group</i> or <i>range</i>.</exception>
		/// <exception cref="ArgumentException">1. Used a PARTIAL_ flag. 2. The regular expression contains <c>(?=...\K)</c>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two three";
		/// var x = new ARegex(@"\b\w+\b");
		/// foreach(var v in x.FindAllS(s)) AOutput.Write(v);
		/// ]]></code>
		/// </example>
		public IEnumerable<string> FindAllS(string s, int group = 0, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			var e = new _MatchEnum(this, s, group, range, matchFlags);
			while(e.Next()) yield return e.GroupS;
		}

		/// <summary>
		/// Finds all match instances of the regular expression. Gets array of strings.
		/// Returns true if found 1 or more matches.
		/// </summary>
		/// <param name="s">Subject string. Cannot be null.</param>
		/// <param name="result">Receives all found matches.</param>
		/// <param name="group">
		/// Group number (1-based index) of results. If 0 (default) - whole match.
		/// See also <see cref="GroupNumberFromName"/>.
		/// </param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentNullException"><i>s</i> is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>group</i> or <i>range</i>.</exception>
		/// <exception cref="ArgumentException">1. Used a PARTIAL_ flag. 2. The regular expression contains <c>(?=...\K)</c>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two three";
		/// var x = new ARegex(@"\b\w+\b");
		/// if(!x.FindAllS(s, out var a)) { AOutput.Write("not found"); return; }
		/// foreach(var v in a) AOutput.Write(v);
		/// ]]></code>
		/// </example>
		public bool FindAllS(string s, out string[] result, int group = 0, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			result = FindAllS(s, group, range, matchFlags).ToArray();
			return result.Length != 0;
		}

		int _Replace(string s, out string result, string repl, Func<RXMatch, string> replFunc, int maxCount, Range? range, RXMatchFlags matchFlags)
		{
			StringBuilder b = null;
			Util.StringBuilder_ bCache = default;
			int prevEnd = 0;
			int replType = 0; //0 empty, 1 simple, 2 with $, 3 callback

			var e = new _MatchEnum(this, s, 0, range, matchFlags, maxCount);
			while(e.Next()) {
				//init variables
				if(b == null) {
					bCache = new Util.StringBuilder_(out b, s.Length + 100);
					if(replFunc != null) replType = 3; else if(!repl.NE()) replType = repl.IndexOf('$') < 0 ? 1 : 2;
				}
				//append s part before this match
				var p = e.GroupP; //x=start, y=end
				int nBefore = p.x - prevEnd;
				if(nBefore != 0) b.Append(s, prevEnd, nBefore);
				prevEnd = p.y;
				//append replacement
				string re = null;
				if(replType >= 2) {
					var m = e.Match; //FUTURE: optimization: if no callback, use single instance and set fields.
					if(replFunc != null) re = replFunc(m);
					else ExpandReplacement_(m, repl, b);
				} else re = repl;
				if(!re.NE()) b.Append(re);
			}

			//append s part after last match
			if(e.foundCount != 0) {
				int nAfter = s.Length - prevEnd;
				if(nAfter > 0) b.Append(s, prevEnd, nAfter);
				result = b.ToString();
				bCache.Dispose();
			} else result = s;

			return e.foundCount;
		}

		/// <summary>
		/// Finds and replaces all match instances of the regular expression.
		/// Returns the result string.
		/// </summary>
		/// <param name="s">Subject string. Cannot be null.</param>
		/// <param name="repl">
		/// Replacement pattern.
		/// Can consist of any combination of literal text and substitutions like $1.
		/// Supports .NET regular expression substitution syntax. See <see cref="Regex.Replace(string, string, int)"/>. Also: replaces $* with the name of the last encountered mark; replaces ${+func} with the return value of a function registered with <see cref="AddReplaceFunc"/>.
		/// </param>
		/// <param name="maxCount">The maximal count of replacements to make. If -1 (default), replaces all.</param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentNullException"><i>s</i> is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">1. Used a PARTIAL_ flag. 2. The regular expression contains <c>(?=...\K)</c>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <remarks>
		/// This function is similar to <see cref="Regex.Replace(string, string, int)"/>.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two22 three333 four";
		/// var x = new ARegex(@"\b(\w+?)(\d+)\b");
		/// s = x.Replace(s, "'$2$1'");
		/// AOutput.Write(s);
		/// ]]></code>
		/// </example>
		public string Replace(string s,
			[ParamString(PSFormat.ARegexReplacement)] string repl = null,
			int maxCount = -1, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			_Replace(s, out var R, repl, null, maxCount, range, matchFlags);
			return R;
		}

		/// <summary>
		/// Finds and replaces all match instances of the regular expression.
		/// Returns the number of replacements made. Returns the result string through an out parameter.
		/// </summary>
		/// <param name="s">Subject string. Cannot be null.</param>
		/// <param name="repl">
		/// Replacement pattern.
		/// Can consist of any combination of literal text and substitutions like $1.
		/// Supports .NET regular expression substitution syntax. See <see cref="Regex.Replace(string, string, int)"/>. Also: replaces $* with the name of the last encountered mark; replaces ${+func} with the return value of a function registered with <see cref="AddReplaceFunc"/>.
		/// </param>
		/// <param name="result">The result string. Can be <i>s</i>.</param>
		/// <param name="maxCount">The maximal count of replacements to make. If -1 (default), replaces all.</param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentNullException"><i>s</i> is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">1. Used a PARTIAL_ flag. 2. The regular expression contains <c>(?=...\K)</c>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <remarks>
		/// This function is similar to <see cref="Regex.Replace(string, string, int)"/>.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two22 three333 four";
		/// var x = new ARegex(@"\b(\w+?)(\d+)\b");
		/// if(0 == x.Replace(s, "'$2$1'", out s)) AOutput.Write("not found");
		/// else AOutput.Write(s);
		/// ]]></code>
		/// </example>
		public int Replace(string s,
			[ParamString(PSFormat.ARegexReplacement)] string repl,
			out string result, int maxCount = -1, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			return _Replace(s, out result, repl, null, maxCount, range, matchFlags);
		}

		/// <summary>
		/// Finds and replaces all match instances of the regular expression. Uses a callback function.
		/// Returns the result string.
		/// </summary>
		/// <param name="s">Subject string. Cannot be null.</param>
		/// <param name="replFunc">
		/// Callback function's delegate, eg lambda. Called for each found match. Returns the replacement.
		/// In the callback function you can use <see cref="RXMatch.ExpandReplacement"/>.
		/// </param>
		/// <param name="maxCount">The maximal count of replacements to make. If -1 (default), replaces all.</param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentNullException"><i>s</i> is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">1. Used a PARTIAL_ flag. 2. The regular expression contains <c>(?=...\K)</c>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <remarks>
		/// This function is similar to <see cref="Regex.Replace(string, MatchEvaluator, int)"/>.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two22 three333 four";
		/// var x = new ARegex(@"\b(\w+?)(\d+)\b");
		/// s = x.Replace(s, o => o.Value.Upper());
		/// AOutput.Write(s);
		/// ]]></code>
		/// </example>
		public string Replace(string s, Func<RXMatch, string> replFunc, int maxCount = -1, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			_Replace(s, out var R, null, replFunc, maxCount, range, matchFlags);
			return R;
		}

		/// <summary>
		/// Finds and replaces all match instances of the regular expression. Uses a callback function.
		/// Returns the number of replacements made. Returns the result string through an out parameter.
		/// </summary>
		/// <param name="s">Subject string. Cannot be null.</param>
		/// <param name="replFunc">
		/// Callback function's delegate, eg lambda. Called for each found match. Returns the replacement.
		/// In the callback function you can use <see cref="RXMatch.ExpandReplacement"/>.
		/// </param>
		/// <param name="result">The result string. Can be <i>s</i>.</param>
		/// <param name="maxCount">The maximal count of replacements to make. If -1 (default), replaces all.</param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentNullException"><i>s</i> is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">1. Used a PARTIAL_ flag. 2. The regular expression contains <c>(?=...\K)</c>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <remarks>
		/// This function is similar to <see cref="Regex.Replace(string, MatchEvaluator, int)"/>.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one two22 three333 four";
		/// var x = new ARegex(@"\b(\w+?)(\d+)\b");
		/// if(0 == x.Replace(s, o => o.Value.Upper(), out s)) AOutput.Write("not found");
		/// else AOutput.Write(s);
		/// ]]></code>
		/// </example>
		public int Replace(string s, Func<RXMatch, string> replFunc, out string result, int maxCount = -1, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			return _Replace(s, out result, null, replFunc, maxCount, range, matchFlags);
		}

		//Used by _ReplaceAll and RXMatch.ExpandReplacement.
		//Fully supports .NET regular expression substitution syntax. Also: replaces $* with the name of the last encountered mark; replaces ${+func} with the return value of a function registered with <see cref="AddReplaceFunc"/>.
		[MethodImpl(MethodImplOptions.AggressiveOptimization)]
		internal static void ExpandReplacement_(RXMatch m, string repl, StringBuilder b)
		{
			fixed(char* s0 = repl) {
				char* s = s0, eos = s + repl.Length, e = s; //e is the end of s part added to b
				while(s < eos) {
					if(*s == '$') {
						if(s > e) { b.Append(e, (int)(s - e)); e = s; }

						char ch = *++s;

						if(ch == '$') { //escaped $
							e = s++;
							continue;
						}

						int group = -1;
						if(ch == '{') { //${name} or ${number}
							char* t = ++s; while(t < eos && *t != '}') t++;
							if(t == eos) break;
							ch = *s;
							if(ch == '+') { //${+userFunc}
								s++;
								if(!s_userReplFuncs.TryGetValue(new string(s, 0, (int)(t - s)), out var replFunc)) continue;
								b.Append(replFunc(m));
							} else if(ch >= '0' && ch <= '9') { //${number}. info: group name cannot start with a digit, then PCRE returns error.
								group = repl.ToInt((int)(s - s0), out int numEnd, STIFlags.NoHex);
								if(s0 + numEnd != t || group < 0) continue;
							} else { //${name}
								group = m.GroupNumberFromName_(s, (int)(t - s), out _); //speed: 40-100 ns
								if(group < 0) continue;
							}
							s = t + 1;
						} else if(ch >= '0' && ch <= '9') { //$number
							group = repl.ToInt((int)(s - s0), out int numEnd, STIFlags.NoHex);
							if(numEnd == 0 || group < 0) continue;
							s = s0 + numEnd;
						} else {
							s++;
							if(ch == '`') { //part before match
								int i = m.Start;
								if(i > 0) b.Append(m.Subject, 0, i);
							} else if(ch == '\'') { //part after match
								var subject = m.Subject;
								int i = m.End, len = subject.Length - i;
								if(len > 0) b.Append(subject, i, len);
							} else if(ch == '&') { //whole match
								group = 0;
							} else if(ch == '+') { //last group
								group = m.GroupCountPlusOne - 1;
							} else if(ch == '_') { //subject
								b.Append(m.Subject);
							} else if(ch == '*') { //last mark
								b.Append(m.Mark);
							} else continue;
						}

						if(group >= 0) {
							if(group >= m.GroupCountPlusOne) continue;
							var g = m[group];
							if(g.Length > 0) b.Append(g.Subject_, g.Start, g.Length);
						}

						e = s;
					} else s++;
				}

				int tail = (int)(eos - e);
				if(tail > 0) b.Append(e, tail);
			}
		}

		/// <summary>
		/// Adds or replaces a function that is called when a regular expression replacement string contains ${+name}.
		/// </summary>
		/// <param name="name">A string used to identify the function. Can contain any characters except '}'.</param>
		/// <param name="replFunc">
		/// Callback function's delegate, eg lambda. Called for each found match. Returns the replacement.
		/// In the callback function you can use <see cref="RXMatch.ExpandReplacement"/>.
		/// </param>
		/// <remarks>
		/// Can be used when there is no way to use <b>Replace</b> overloads with a <i>replFunc</i> parameter. For example in Find/Replace UI.
		/// </remarks>
		/// <example>
		/// Create new script in the Au editor and add this code. In Properties set role editorExtension. Run.
		/// Then in the Find pane in the replacement field you can use <c>${+Upper}</c> and <c>${+Lower}</c>.
		/// <code><![CDATA[
		/// ARegex.AddReplaceFunc("Upper", m => m.Value.Upper()); //make uppercase
		/// ARegex.AddReplaceFunc("Lower", m => m.Value.Lower()); //make lowercase
		/// ]]></code>
		/// </example>
		public static void AddReplaceFunc(string name, Func<RXMatch, string> replFunc)
		{
			s_userReplFuncs[name] = replFunc;
		}
		static System.Collections.Concurrent.ConcurrentDictionary<string, Func<RXMatch, string>> s_userReplFuncs = new System.Collections.Concurrent.ConcurrentDictionary<string, Func<RXMatch, string>>();

		//rejected: use pcre2_substitute. Not useful because: we cannot implement RXMatch.ExpandReplacement with it; we have AddReplaceFunc.

		/// <summary>
		/// Returns array of substrings delimited by regular expression matches.
		/// </summary>
		/// <param name="s">Subject string. Cannot be null.</param>
		/// <param name="maxCount">The maximal count of substrings to get. The last substring contains the unsplit remainder of the subject string. If 0 (default) or negative, gets all.</param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentNullException"><i>s</i> is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">1. Used a PARTIAL_ flag. 2. The regular expression contains <c>(?=...\K)</c>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <remarks>
		/// Element 0 of the returned array is <i>s</i> substring until the first match of the regular expression, element 1 is substring between the first and second match, and so on. If no matches, the array contains single element and it is <i>s</i>.
		/// 
		/// This function is similar to <see cref="Regex.Split(string, int)"/>.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one, two,three , four";
		/// var x = new ARegex(@" *, *");
		/// var a = x.Split(s);
		/// for(int i = 0; i < a.Length; i++) AOutput.Write(i, a[i]);
		/// ]]></code>
		/// </example>
		public string[] Split(string s, int maxCount = 0, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			if(maxCount < 0) maxCount = 0;
			if(maxCount != 1) {
				var a = new List<string>();
				int prevEnd = 0;
				var e = new _MatchEnum(this, s, 0, range, matchFlags, maxCount - 1);
				while(e.Next()) {
					var p = e.GroupP;
					a.Add(s.Substring(prevEnd, p.x - prevEnd));
					prevEnd = p.y;
				}
				if(e.foundCount > 0) {
					a.Add(s.Substring(prevEnd));
					return a.ToArray();
				}
			}
			return new string[] { s };
		}

		/// <summary>
		/// Returns <see cref="RXGroup"/> array of substrings delimited by regular expression matches.
		/// </summary>
		/// <param name="s">Subject string. Cannot be null.</param>
		/// <param name="maxCount">The maximal count of substrings to get. The last substring contains the unsplit remainder of the subject string. If 0 (default) or negative, gets all.</param>
		/// <param name="range">See <see cref="IsMatch"/>.</param>
		/// <param name="matchFlags">See <see cref="IsMatch"/>.</param>
		/// <exception cref="ArgumentNullException"><i>s</i> is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">1. Used a PARTIAL_ flag. 2. The regular expression contains <c>(?=...\K)</c>.</exception>
		/// <exception cref="AuException">The PCRE API function <b>pcre2_match</b> failed. Unlikely.</exception>
		/// <remarks>
		/// Element 0 of the returned array is <i>s</i> substring until the first match of the regular expression, element 1 is substring between the first and second match, and so on. If no matches, the array contains single element and it is <i>s</i>.
		/// 
		/// This function is similar to <see cref="Regex.Split(string, int)"/>.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// var s = "one, two,three , four";
		/// var x = new ARegex(@" *, *");
		/// var a = x.SplitG(s);
		/// foreach(var v in a) AOutput.Write(v.Start, v.Value);
		/// ]]></code>
		/// </example>
		public RXGroup[] SplitG(string s, int maxCount = 0, Range? range = null, RXMatchFlags matchFlags = 0)
		{
			if(maxCount < 0) maxCount = 0;
			if(maxCount != 1) {
				var a = new List<RXGroup>();
				int prevEnd = 0;
				var e = new _MatchEnum(this, s, 0, range, matchFlags, maxCount - 1);
				while(e.Next()) {
					var p = e.GroupP;
					a.Add(new RXGroup(s, prevEnd, p.x));
					prevEnd = p.y;
				}
				if(e.foundCount > 0) {
					a.Add(new RXGroup(s, prevEnd, s.Length));
					return a.ToArray();
				}
			}
			return new RXGroup[] { new RXGroup(s, 0, s.Length) };
		}

		//rejected: probably rarely used. Or need IEnumerable<string> too.
		//public IEnumerable<RXGroup> SplitE(string s, int maxCount = 0, Range? range = null, RXMatchFlags matchFlags = 0)
		//{
		//	if(maxCount< 0) maxCount = 0;
		//	if(maxCount != 1) {
		//		int prevEnd = 0;
		//		var e = new _MatchEnum(this, s, 0, range, matchFlags, maxCount - 1);
		//		while(e.Next()) {
		//			var p = e.GroupP;
		//			yield return new RXGroup(s, prevEnd, p.x);
		//			prevEnd = p.y;
		//		}
		//		if(e.foundCount > 0) {
		//			yield return new RXGroup(s, prevEnd, s.Length);
		//			yield break;
		//		}
		//	}
		//	yield return new RXGroup(s, 0, s.Length);
		//}

		//Calls pcre2_pattern_info(ALLOPTIONS), which returns flags passed to the ctor and possibly modified by (*OPTION) and possibly added UTF if contains non-ASCII characters.
		//Actually RXFlags is long, where the high 32 bits is extended options. This func gets only the main options (the low 32 bits).
		RXFlags _InfoAllOptions {
			get {
				RXFlags R;
				Cpp.pcre2_pattern_info(_CodeHR, Cpp.PCRE2_INFO_.ALLOPTIONS, &R);
				return R;
			}
		}

		/// <summary>
		/// Encloses string in <c>\Q</c> <c>\E</c> if it contains metacharacters <c>\^$.[|()?*+{</c> or if <i>always</i> == true.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="always">Enclose always, even if the string does not contain metacharacters. Should be true if the regular expression in which this string will be used has option "extended", because then whitespace is ignored and # is a special character too.</param>
		/// <remarks>
		/// Such enclosed substring in a regular expression is interpreted as a literal string.
		/// This function also escapes \E, so that it does not end the literal string.
		/// </remarks>
		public static string EscapeQE(string s, bool always = false)
		{
			if(s == null) return s;
			if(always) goto g1;
			for(int i = 0; i < s.Length; i++) {
				char c = s[i];
				if((c >= '(' && c <= '+') || c == '\\' || c == '.' || c == '?' || c == '{' || c == '[' || c == '|' || c == '$' || c == '^') goto g1;
				//this is slower
				//if(c < 128) {
				//	if(c < 64) {
				//		if(0 != (0b1000000000000000010011110001000000000000000000000000000000000000UL & (1UL << c))) goto g1;
				//	} else {
				//		if(0 != (0b0001100000000000000000000000000001011000000000000000000000000000UL & (1UL << (c - 64)))) goto g1;
				//	}
				//}
			}
			return s;
			g1:
			return @"\Q" + s.Replace(@"\E", @"\E\\E\Q") + @"\E";
		}
	}

	#region static

	public static partial class AExtString
	{
		/// <summary>
		/// Returns true if this string matches PCRE regular expression <i>rx</i>.
		/// Parameters etc are of <see cref="ARegex(string, RXFlags)"/> and <see cref="ARegex.IsMatch"/>.
		/// Examples in <see cref="ARegex"/> class help.
		/// </summary>
		/// <param name="t">This string. If null, returns false.</param>
		/// <param name="rx">Regular expression.</param>
		/// <param name="flags"></param>
		/// <param name="range">See <see cref="ARegex.IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">Invalid regular expression.</exception>
		/// <exception cref="AuException">Failed (unlikely).</exception>
		public static bool RegexIsMatch(this string t,
			[ParamString(PSFormat.ARegex)] string rx,
			RXFlags flags = 0, Range? range = null)
		{
			var x = _cache.AddOrGet(rx, flags);
			return x.IsMatch(t, range);
		}

		/// <summary>
		/// Returns true if this string matches PCRE regular expression <i>rx</i>.
		/// Gets match info as <see cref="RXMatch"/>.
		/// Parameters etc are of <see cref="ARegex(string, RXFlags)"/> and <see cref="ARegex.Match"/>.
		/// Examples in <see cref="ARegex"/> class help.
		/// </summary>
		/// <param name="t">This string. If null, returns false.</param>
		/// <param name="rx">Regular expression.</param>
		/// <param name="result">Receives match info.</param>
		/// <param name="flags"></param>
		/// <param name="range">See <see cref="ARegex.IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">Invalid regular expression.</exception>
		/// <exception cref="AuException">Failed (unlikely).</exception>
		public static bool RegexMatch(this string t,
			[ParamString(PSFormat.ARegex)] string rx,
			out RXMatch result, RXFlags flags = 0, Range? range = null)
		{
			var x = _cache.AddOrGet(rx, flags);
			return x.Match(t, out result, range);
		}

		/// <summary>
		/// Returns true if this string matches PCRE regular expression <i>rx</i>.
		/// Gets whole match or some group, as string.
		/// Parameters etc are of <see cref="ARegex(string, RXFlags)"/> and <see cref="ARegex.MatchS"/>.
		/// Examples in <see cref="ARegex"/> class help.
		/// </summary>
		/// <param name="t">This string. If null, returns false.</param>
		/// <param name="rx">Regular expression.</param>
		/// <param name="group">Group number (1-based index) of result. If 0 (default) - whole match.</param>
		/// <param name="result">Receives the match value.</param>
		/// <param name="flags"></param>
		/// <param name="range">See <see cref="ARegex.IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>group</i> or <i>range</i>.</exception>
		/// <exception cref="ArgumentException">Invalid regular expression.</exception>
		/// <exception cref="AuException">Failed (unlikely).</exception>
		public static bool RegexMatch(this string t,
			[ParamString(PSFormat.ARegex)] string rx,
			int group, out string result, RXFlags flags = 0, Range? range = null)
		{
			var x = _cache.AddOrGet(rx, flags);
			return x.MatchS(t, out result, group, range);
		}

		/// <summary>
		/// Returns true if this string matches PCRE regular expression <i>rx</i>.
		/// Gets whole match or some group, as index and length.
		/// Parameters etc are of <see cref="ARegex(string, RXFlags)"/> and <see cref="ARegex.MatchG"/>.
		/// Examples in <see cref="ARegex"/> class help.
		/// </summary>
		/// <param name="t">This string. If null, returns false.</param>
		/// <param name="rx">Regular expression.</param>
		/// <param name="group">Group number (1-based index) of result. If 0 (default) - whole match.</param>
		/// <param name="result">Receives match info.</param>
		/// <param name="flags"></param>
		/// <param name="range">See <see cref="ARegex.IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>group</i> or <i>range</i>.</exception>
		/// <exception cref="ArgumentException">Invalid regular expression.</exception>
		/// <exception cref="AuException">Failed (unlikely).</exception>
		public static bool RegexMatch(this string t,
			[ParamString(PSFormat.ARegex)] string rx,
			int group, out RXGroup result, RXFlags flags = 0, Range? range = null)
		{
			var x = _cache.AddOrGet(rx, flags);
			return x.MatchG(t, out result, group, range);
		}

		/// <summary>
		/// Finds all match instances of PCRE regular expression <i>rx</i>.
		/// Returns a lazy IEnumerable&lt;<see cref="RXMatch"/>&gt; object that can be used with foreach.
		/// Parameters etc are of <see cref="ARegex(string, RXFlags)"/> and <see cref="ARegex.FindAll(string, Range?, RXMatchFlags)"/>.
		/// Examples in <see cref="ARegex"/> class help.
		/// </summary>
		/// <param name="t">This string.</param>
		/// <param name="rx">Regular expression.</param>
		/// <param name="flags"></param>
		/// <param name="range">See <see cref="ARegex.IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">Invalid regular expression. Or used a PARTIAL_ flag.</exception>
		/// <exception cref="AuException">Failed (unlikely).</exception>
		public static IEnumerable<RXMatch> RegexFindAll(this string t,
			[ParamString(PSFormat.ARegex)] string rx,
			RXFlags flags = 0, Range? range = null)
		{
			if(t == null) throw new NullReferenceException();
			var x = _cache.AddOrGet(rx, flags);
			return x.FindAll(t, range);
		}

		/// <summary>
		/// Finds all match instances of PCRE regular expression <i>rx</i>. Gets array of <see cref="RXMatch"/>.
		/// Returns true if found 1 or more matches.
		/// Parameters etc are of <see cref="ARegex(string, RXFlags)"/> and <see cref="ARegex.FindAll(string, out RXMatch[], Range?, RXMatchFlags)"/>.
		/// Examples in <see cref="ARegex"/> class help.
		/// </summary>
		/// <param name="t">This string.</param>
		/// <param name="rx">Regular expression.</param>
		/// <param name="result">Receives all found matches.</param>
		/// <param name="flags"></param>
		/// <param name="range">See <see cref="ARegex.IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">Invalid regular expression. Or used a PARTIAL_ flag.</exception>
		/// <exception cref="AuException">Failed (unlikely).</exception>
		public static bool RegexFindAll(this string t,
			[ParamString(PSFormat.ARegex)] string rx,
			out RXMatch[] result, RXFlags flags = 0, Range? range = null)
		{
			if(t == null) throw new NullReferenceException();
			var x = _cache.AddOrGet(rx, flags);
			return x.FindAll(t, out result, range);
		}

		/// <summary>
		/// Finds all match instances of PCRE regular expression <i>rx</i>.
		/// Returns a lazy IEnumerable&lt;<see cref="RXGroup"/>&gt; object that can be used with foreach.
		/// Parameters etc are of <see cref="ARegex(string, RXFlags)"/> and <see cref="ARegex.FindAllS(string, int, Range?, RXMatchFlags)"/>.
		/// Examples in <see cref="ARegex"/> class help.
		/// </summary>
		/// <param name="t">This string.</param>
		/// <param name="rx">Regular expression.</param>
		/// <param name="group">Group number (1-based index) of results. If 0 (default) - whole match.</param>
		/// <param name="flags"></param>
		/// <param name="range">See <see cref="ARegex.IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>group</i> or <i>range</i>.</exception>
		/// <exception cref="ArgumentException">Invalid regular expression. Or used a PARTIAL_ flag.</exception>
		/// <exception cref="AuException">Failed (unlikely).</exception>
		public static IEnumerable<string> RegexFindAll(this string t,
			[ParamString(PSFormat.ARegex)] string rx,
			int group, RXFlags flags = 0, Range? range = null)
		{
			if(t == null) throw new NullReferenceException();
			var x = _cache.AddOrGet(rx, flags);
			return x.FindAllS(t, group, range);
		}

		/// <summary>
		/// Finds all match instances of PCRE regular expression <i>rx</i>. Gets array of strings.
		/// Returns true if found 1 or more matches.
		/// Parameters etc are of <see cref="ARegex(string, RXFlags)"/> and <see cref="ARegex.FindAllS(string, out string[], int, Range?, RXMatchFlags)"/>.
		/// Examples in <see cref="ARegex"/> class help.
		/// </summary>
		/// <param name="t">This string.</param>
		/// <param name="rx">Regular expression.</param>
		/// <param name="group">Group number (1-based index) of results. If 0 (default) - whole match.</param>
		/// <param name="result">Receives all found matches.</param>
		/// <param name="flags"></param>
		/// <param name="range">See <see cref="ARegex.IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>group</i> or <i>range</i>.</exception>
		/// <exception cref="ArgumentException">Invalid regular expression. Or used a PARTIAL_ flag.</exception>
		/// <exception cref="AuException">Failed (unlikely).</exception>
		public static bool RegexFindAll(this string t,
			[ParamString(PSFormat.ARegex)] string rx,
			int group, out string[] result, RXFlags flags = 0, Range? range = null)
		{
			if(t == null) throw new NullReferenceException();
			var x = _cache.AddOrGet(rx, flags);
			return x.FindAllS(t, out result, group, range);
		}

		/// <summary>
		/// Finds and replaces all match instances of PCRE regular expression <i>rx</i>.
		/// Returns the result string.
		/// Parameters etc are of <see cref="ARegex(string, RXFlags)"/> and <see cref="ARegex.Replace(string, string, int, Range?, RXMatchFlags)"/>.
		/// Examples in <see cref="ARegex"/> class help.
		/// </summary>
		/// <param name="t">This string.</param>
		/// <param name="rx">Regular expression.</param>
		/// <param name="repl">Replacement pattern.</param>
		/// <param name="maxCount">The maximal count of replacements to make. If -1 (default), replaces all.</param>
		/// <param name="flags"></param>
		/// <param name="range">See <see cref="ARegex.IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">Invalid regular expression. Or used a PARTIAL_ flag.</exception>
		/// <exception cref="AuException">Failed (unlikely).</exception>
		public static string RegexReplace(this string t,
			[ParamString(PSFormat.ARegex)] string rx,
			[ParamString(PSFormat.ARegexReplacement)] string repl,
			int maxCount = -1, RXFlags flags = 0, Range? range = null)
		{
			if(t == null) throw new NullReferenceException();
			var x = _cache.AddOrGet(rx, flags);
			return x.Replace(t, repl, maxCount, range);
		}

		/// <summary>
		/// Finds and replaces all match instances of PCRE regular expression <i>rx</i>.
		/// Returns the number of replacements made.
		/// Parameters etc are of <see cref="ARegex(string, RXFlags)"/> and <see cref="ARegex.Replace(string, string, out string, int, Range?, RXMatchFlags)"/>.
		/// Examples in <see cref="ARegex"/> class help.
		/// </summary>
		/// <param name="t">This string.</param>
		/// <param name="rx">Regular expression.</param>
		/// <param name="repl">Replacement pattern.</param>
		/// <param name="result">The result string.</param>
		/// <param name="maxCount">The maximal count of replacements to make. If -1 (default), replaces all.</param>
		/// <param name="flags"></param>
		/// <param name="range">See <see cref="ARegex.IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">Invalid regular expression. Or used a PARTIAL_ flag.</exception>
		/// <exception cref="AuException">Failed (unlikely).</exception>
		public static int RegexReplace(this string t,
			[ParamString(PSFormat.ARegex)] string rx,
			[ParamString(PSFormat.ARegexReplacement)] string repl,
			out string result, int maxCount = -1, RXFlags flags = 0, Range? range = null)
		{
			if(t == null) throw new NullReferenceException();
			var x = _cache.AddOrGet(rx, flags);
			return x.Replace(t, repl, out result, maxCount, range);
		}

		/// <summary>
		/// Finds and replaces all match instances of PCRE regular expression <i>rx</i>. Uses a callback function.
		/// Returns the result string.
		/// Parameters etc are of <see cref="ARegex(string, RXFlags)"/> and <see cref="ARegex.Replace(string, Func{RXMatch, string}, int, Range?, RXMatchFlags)"/>.
		/// Examples in <see cref="ARegex"/> class help.
		/// </summary>
		/// <param name="t">This string.</param>
		/// <param name="rx">Regular expression.</param>
		/// <param name="replFunc">Callback function that receives found matches and returns replacements.</param>
		/// <param name="maxCount">The maximal count of replacements to make. If -1 (default), replaces all.</param>
		/// <param name="flags"></param>
		/// <param name="range">See <see cref="ARegex.IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">Invalid regular expression. Or used a PARTIAL_ flag.</exception>
		/// <exception cref="AuException">Failed (unlikely).</exception>
		public static string RegexReplace(this string t,
			[ParamString(PSFormat.ARegex)] string rx,
			Func<RXMatch, string> replFunc, int maxCount = -1, RXFlags flags = 0, Range? range = null)
		{
			if(t == null) throw new NullReferenceException();
			var x = _cache.AddOrGet(rx, flags);
			return x.Replace(t, replFunc, maxCount, range);
		}

		/// <summary>
		/// Finds and replaces all match instances of PCRE regular expression <i>rx</i>. Uses a callback function.
		/// Returns the number of replacements made.
		/// Parameters etc are of <see cref="ARegex(string, RXFlags)"/> and <see cref="ARegex.Replace(string, Func{RXMatch, string}, out string, int, Range?, RXMatchFlags)"/>.
		/// Examples in <see cref="ARegex"/> class help.
		/// </summary>
		/// <param name="t">This string.</param>
		/// <param name="rx">Regular expression.</param>
		/// <param name="replFunc">Callback function that receives found matches and returns replacements.</param>
		/// <param name="result">The result string.</param>
		/// <param name="maxCount">The maximal count of replacements to make. If -1 (default), replaces all.</param>
		/// <param name="flags"></param>
		/// <param name="range">See <see cref="ARegex.IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">Invalid regular expression. Or used a PARTIAL_ flag.</exception>
		/// <exception cref="AuException">Failed (unlikely).</exception>
		public static int RegexReplace(this string t,
			[ParamString(PSFormat.ARegex)] string rx,
			Func<RXMatch, string> replFunc, out string result, int maxCount = -1, RXFlags flags = 0, Range? range = null)
		{
			if(t == null) throw new NullReferenceException();
			var x = _cache.AddOrGet(rx, flags);
			return x.Replace(t, replFunc, out result, maxCount, range);
		}

		/// <summary>
		/// Returns array of substrings delimited by PCRE regular expression <i>rx</i> matches.
		/// Parameters etc are of <see cref="ARegex(string, RXFlags)"/> and <see cref="ARegex.Split(string, int, Range?, RXMatchFlags)"/>.
		/// Examples in <see cref="ARegex"/> class help.
		/// </summary>
		/// <param name="t">This string.</param>
		/// <param name="rx">Regular expression.</param>
		/// <param name="maxCount">The maximal count of substrings to get. The last substring contains the unsplit remainder of the subject string. If 0 (default) or negative, gets all.</param>
		/// <param name="flags"></param>
		/// <param name="range">See <see cref="ARegex.IsMatch"/>.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid <i>range</i>.</exception>
		/// <exception cref="ArgumentException">Invalid regular expression. Or used a PARTIAL_ flag.</exception>
		/// <exception cref="AuException">Failed (unlikely).</exception>
		public static string[] RegexSplit(this string t,
			[ParamString(PSFormat.ARegex)] string rx,
			int maxCount = 0, RXFlags flags = 0, Range? range = null)
		{
			if(t == null) throw new NullReferenceException();
			var x = _cache.AddOrGet(rx, flags);
			return x.Split(t, maxCount, range);
		}

		static _RegexCache _cache = new _RegexCache();

		//Cache of compiled regular expressions.
		//Can make ~10 times faster when the subject string is short.
		//The algorithm is from .NET Regex source code.
		class _RegexCache
		{
			struct _RXCode
			{
				public string regex;
				public ARegex code; //note: could instead cache only PCRE code (LPARAM), but it makes quite difficult
				public RXFlags flags;
			}

			LinkedList<_RXCode> _list = new LinkedList<_RXCode>();
			const int c_maxCount = 15;

			/// <summary>
			/// If rx/flags is in the cache, returns the cached code.
			/// Else compiles rx/flags, adds to the cache and returns the code.
			/// </summary>
			/// <param name="rx"></param>
			/// <param name="flags"></param>
			/// <exception cref="ArgumentException">Invalid regular expression. Or failed to compile it for some other reason.</exception>
			public ARegex AddOrGet(string rx, RXFlags flags)
			{
				lock(this) {
					int len = rx.Length;
					for(var x = _list.First; x != null; x = x.Next) {
						var v = x.Value.regex;
						if(v.Length == len && v == rx && x.Value.flags == flags) {
							if(x != _list.First) {
								_list.Remove(x);
								_list.AddFirst(x);
							}
							return x.Value.code;
						}
					}
					{
						var code = new ARegex(rx, flags);

						var x = new _RXCode() { code = code, regex = rx, flags = flags };
						_list.AddFirst(x);
						if(_list.Count > c_maxCount) _list.RemoveLast();
						//note: now cannot free the PCRE code, because another thread may be using it. GC will do it safely.

						return code;
					}
				}
			}
		}
	}

	#endregion
}
