using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Win32;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using System.Drawing;
//using System.Linq;
using System.Xml.Linq;
//using System.Xml.XPath;

using Catkeys.Types;
using static Catkeys.NoClass;

#pragma warning disable CS0282 //VS bug: shows warning "There is no defined ordering between fields in multiple declarations of partial struct 'Acc'. To specify an ordering, all instance fields must be in the same declaration."

namespace Catkeys
{
	/// <summary>
	/// Accessible object.
	/// </summary>
	/// <remarks>
	/// Accessible objects (AO) are various user interface (UI) objects in windows and controls. For example buttons, links, list items. This class can find them, get properties, click, etc. Especially useful with web pages, because there are no controls. And many other windows don't use controls but support AO. But not all UI objects are AO.
	/// 
	/// An Acc instance holds an AO COM pointer (<msdn>IAccessible</msdn>) and a simple element id (int). Most Acc functions wrap IAccessible interface functions or/and related API.
	/// 
	/// Acc functions that get properties don't throw exception when the wrapped IAccessible/etc function failed (returned an error code of HRESULT type). Then they return "" (string properties), 0, false, null or empty collection, dependin on return type. Applications implement AOs differently, often with bugs, and their IAccessible interface functions return a variety of error codes. It's impossible to reliably detect whether the error code means a serious error or the property is merely unavailable. These Acc functions also set the last error code of this thread = the return value (HRESULT) of the IAccessible function, and callers can use <see cref="Native.GetError"/> to get it. If Native.GetError returns 1 (S_FALSE), in most cases it's not an error, just the property is unavailable. On error it will probably be a negative error code.
	/// 
	/// You can dispose Acc variables to release the COM object, but it is not necessary (GC will do it later).
	/// 
	/// An Acc variable cannot be used in multiple threads. Only Dispose can be called in any thread.
	/// 
	/// AOs are implemented and live in their applications. This class just communicates with them.
	/// 
	/// Many applications have various problems with their AOs: bugs, incorrect/nonstandard/partial implementation, or initially disabled. This class implements workarounds for known problems, where possible.
	/// 
	/// Known problematic applications:
	/// <list type="bullet">
	/// <item>
	/// Chrome web browser. Also Opera and other apps that use Chrome code; window class name is like "Chrome_WidgetWin_1".
	/// 
	/// Web page AOs initially are disabled (missing). Workarounds:
	/// Functions Find, Wait and FindAll enable it if used role prefix "web:" or "chrome:". Functions FromXY, FromMouse and Focused enable it if window class name starts with "Chrome". However Chrome does it lazily, therefore first time the functions often get wrong AO. Note: this auto-enabing may fail with future Chrome versions.
	/// Other ways to enable Chrome AOs: 1. Start Chrome with command line --force-renderer-accessibility. 2. In the future the script editor will have an option to enable Chrome AOs when it starts.
	/// 
	/// Some new web browser versions add new features or bugs that break something. AOs are especially vulnerable, because they are considered second-class citizens.
	/// </item>
	/// <item>
	/// Firefox web browser.
	/// 
	/// By default, Find is slow. Also for this reason Wait consumes much CPU.
	/// To make faster (~100 times), need to disable the Firefox multiprocess feature: open URL about:config and set browser.tabs.remote.force-enable = false. Note: Firefox may reset it when upgrading or reinstalling. Note: future Firefox versions may remove this setting; then, if you want speed, use another web browser.
	/// 
	/// Ocassionally Firefox briefly turns off its accessible objects. Workaround: use Wait, not Find. With other web browsers also it's better to use Wait.
	/// 
	/// Some new web browser versions add new features or bugs that break something. AOs are especially vulnerable, because they are considered second-class citizens.
	/// </item>
	/// <item>
	/// Edge web browser, JavaFX and other applications that don't have true accessible objects but have UI Automation elements.
	/// 
	/// To find AOs in these applications, need flag <see cref="AFFlags.UIA"/>.
	/// </item>
	/// <item>
	/// Java applications that use AWT/Swing (window class name starts with "SunAwt").
	/// 
	/// Not supported on 32-bit OS.
	/// 
	/// Must be enabled Java Access Bridge (JAB).
	/// You can enable/disable it in Control Panel -> Ease of Access Center -> Use the computer without a display. Or use jabswitch.exe. Then restart Java applications.
	/// 
	/// Your process must have the same 32/64 bitness as the installed Java. To remove this limitation, install Java 32-bit and 64-bit (they coexist).
	/// </item>
	/// <item>
	/// OpenOffice.
	/// 
	/// Often crashes after using accessible objects, usually when closing. Noticed in OpenOffice 4.1.4; may be fixed in newer versions.
	/// </item>
	/// <item>
	/// LibreOffice.
	/// 
	/// AOs are unavailable unless this process is 32-bit (when LibreOffice is 64-bit). Also need flag <see cref="NotInProc"/>.
	/// </item>
	/// <item>
	/// In some windows, AO of some controls are not connected to AO of parent control. Then Find cannot find them if searches in whole window.
	/// 
	/// Workaround: search only in that control. For example, use role prefix "class=...:" or id=...:". If it's a web browser control - prefix "web:". Or find the control with <see cref="Wnd.Child"/> and search in it. Or use <see cref="Acc.Finder.Find(Wnd, Wnd.ChildFinder)"/>.
	/// </item>
	/// <item>
	/// AOs of many standard Windows controls have bugs when they are retrieved without loading dll into the target process (see <see cref="AFFlags.NotInProc"/>).
	/// Known bugs: 1. Toolbar buttons don't have Name in some cases. 2. <see cref="Focus"/> and <see cref="Select"/> often don't work properly.
	/// 
	/// Workaround: don't use <see cref="AFFlags.NotInProc"/>, or use <see cref="AFFlags.UIA"/>.
	/// </item>
	/// <item>
	/// Function Find is much slower when cannot load dll into the target process. More info: <see cref="AFFlags.NotInProc"/>. Function Wait then consumes much more CPU.
	/// </item>
	/// <item>
	/// If the process has different 32/64 bitness than this process, to load the dll is launched rundll32.exe, which makes slower by about 50 ms first time.
	/// </item>
	/// <item>
	/// Currently this library does not support auto-scaled windows when using high DPI (text size 125%, 150% or more).
	/// If the target process is auto-scaled and this process isn't (or vice versa, or they have a different scaling factor), most coordinate-related functions don't work properly. For example, they get wrong AO rectangles.
	/// </item>
	/// </list>
	/// </remarks>
	/// <example>
	/// Click link "Example" in Chrome.
	/// <code><![CDATA[
	/// var w = Wnd.Find("* Chrome").OrThrow();
	/// var a = Acc.Find(w, "web:LINK", "Example").OrThrow();
	/// a.DoAction();
	/// ]]></code>
	/// Click a link, wait for new web page, click a link in it.
	/// <code><![CDATA[
	/// var w = Wnd.Find("* Chrome").OrThrow();
	/// var a = Acc.Wait(1, w, "web:LINK", "Link 1").OrThrow();
	/// a.DoActionAndWaitForNewWebPage();
	/// a = Acc.Wait(10, w, "web:LINK", "Link 2").OrThrow();
	/// a.DoActionAndWaitForNewWebPage();
	/// ]]></code>
	/// </example>
	[StructLayout(LayoutKind.Sequential)]
	public unsafe partial class Acc :IDisposable, ISupportOrThrow
	{
		//tested: Acc object memory size with overhead: 32 bytes. Note: we don't use RCW<IAccessible>, which would add another 32 bytes.

		//FUTURE: Acc.Misc.EnableAccInChromeWebPagesWhenItStarts
		//FUTURE: Acc.Misc.EnableAccInJavaWindows (see JavaEnableJAB in QM2)
		//FUTURE: add functions to marshal AO to another thread.

		[Flags]
		internal enum _MiscFlags :byte
		{
			InProc = 1, //retrieved inproc
			UIA = 2,
			Java = 4,
		}

		internal struct _Misc
		{
			public _MiscFlags flags;
			public byte role; //for optimization. 0 if not set or failed to get or VT_BSTR or does not fit in BYTE.
			public ushort level; //for ToString. 0 if not set.

			public void SetRole(AccROLE role) { this.role = (byte)((uint)role <= 0xff ? role : 0); }
			public void SetLevel(int level) { this.level = (ushort)Math_.MinMax(level, 0, 0xffff); }
		}

		internal IntPtr _iacc;
		internal int _elem;
		internal _Misc _misc; //info: does not make real object memory bigger (still 32 bytes).

		/// <summary>
		/// Creates Acc from IAccessible and child id.
		/// By default does not AddRef.
		/// iacc must not be Is0.
		/// </summary>
		internal Acc(IntPtr iacc, int elem = 0, bool addRef = false)
		{
			_Set(iacc, elem, default, addRef);
		}

		/// <summary>
		/// Creates Acc from Cpp_Acc.
		/// By default does not AddRef.
		/// x.acc must not be Is0.
		/// </summary>
		internal Acc(Cpp.Cpp_Acc x, bool addRef = false)
		{
			_Set(x.acc, x.elem, x.misc, addRef);
		}

		/// <summary>
		/// Sets fields.
		/// _iacc must be Is0.
		/// </summary>
		void _Set(IntPtr iacc, int elem = 0, _Misc misc = default, bool addRef = false)
		{
			Debug.Assert(_iacc == default);
			Debug.Assert(iacc != default);
			if(addRef) Marshal.AddRef(iacc);
			_iacc = iacc;
			_elem = elem;
			_misc = misc;

			//CONSIDER: GC.AddMemoryPressure/GC.RemoveMemoryPressure
		}

		void _Dispose(bool doNotRelease = false)
		{
			if(_iacc != default) {
				var t = _iacc; _iacc = default;
				//Perf.First();
				if(!doNotRelease) Marshal.Release(t);
				//if(!doNotRelease) Print($"rel: {Marshal.Release(t)}");
				//Perf.NW();
			}
			_elem = 0;
			_misc = default;
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Releases IAccessible COM object and clears this variable.
		/// </summary>
		public void Dispose()
		{
			_Dispose();
		}

		///
		~Acc()
		{
			//Debug_.Print("Acc not disposed: " + (_role != 0 ? Role : ("_Disposed=" + _Disposed.ToString()))); //note: with debugger somehow can be called several times, then Is0 true
			_Dispose();
		}

		/// <summary>
		/// Gets or changes simple element id, also known as child id.
		/// </summary>
		/// <remarks>
		/// Most accessible objects (AO) are not simple elements. Then this property is 0.
		/// Often (but not always) this property is the 1-based item index in parent AO. For example LISTITEM in LIST.
		/// The 'set' function sometimes can be used as a fast alternative to <see cref="Navigate"/>. It modifies only this variable. It does not check whether the value is valid.
		/// Simple elements cannot have child AOs.
		/// </remarks>
		public int SimpleElementId { get => _elem; set { _misc.role = 0; _elem = value; } }

		/// <summary>
		/// Returns true if this accessible object was found without loading dll into the target process.
		/// More info: <see cref="AFFlags.NotInProc"/>.
		/// </summary>
		public bool NotInProc => !_misc.flags.Has_(_MiscFlags.InProc);

		/// <summary>
		/// Gets or sets indentation level for <see cref="ToString"/>.
		/// </summary>
		/// <remarks>
		/// When Find or similar function finds an accessible object, it sets this property of the Acc variable.
		/// When searching in a window or control, at level 0 are direct children of the WINDOW object of the window or control. When searching in Acc, at level 0 are direct children of the Acc. When searching in web page (role prefix "web:" etc), at level 0 is the web page object (role DOCUMENT or PANE).
		/// </remarks>
		public int Level { get => _misc.level; set => _misc.SetLevel(value); }

		/// <summary>
		/// Returns true if this variable is disposed.
		/// </summary>
		bool _Disposed => _iacc == default;
		//note: named not 'IsDisposed' because can be easily confused with IsDisabled.

		internal void LibThrowIfDisposed()
		{
			if(_Disposed) throw new ObjectDisposedException(nameof(Acc));
		}

		/// <summary>
		/// Gets accessible object of window or control or its standard part - client area, titlebar etc.
		/// Uses API <msdn>AccessibleObjectFromWindow</msdn>.
		/// </summary>
		/// <param name="w">Window or control.</param>
		/// <param name="objid">Window part id. Default AccOBJID.WINDOW. Also can be a custom id supported by that window, cast int to AccOBJID.</param>
		/// <param name="flags"></param>
		/// <exception cref="WndException">Invalid window.</exception>
		/// <exception cref="CatException">Failed. For example, window of a higher UAC integrity level process.</exception>
		public static Acc FromWindow(Wnd w, AccOBJID objid = AccOBJID.WINDOW, AWFlags flags = 0)
		{
			var hr = Cpp.Cpp_AccFromWindow(flags.Has_(AWFlags.InProc) ? 1 : 0, w, objid, out var a, out _);
			if(hr != 0) {
				if(flags.Has_(AWFlags.NoThrow)) return null;
				w.ThrowIfInvalid();
				_WndThrow(hr, w, "*get accessible object from window.");
			}
			return new Acc(a);
		}

		static void _WndThrow(int hr, Wnd w, string es)
		{
			w.LibUacCheckAndThrow(es);
			throw new CatException(hr, es);
		}

		/// <summary>
		/// Gets accessible object from point.
		/// Uses API <msdn>AccessibleObjectFromPoint</msdn>.
		/// </summary>
		/// <param name="p">Coordinates in screen.</param>
		/// <param name="flags"></param>
		/// <exception cref="CatException">Failed. For example, window of a higher UAC integrity level process.</exception>
		public static Acc FromXY(Point p, AXYFlags flags = 0)
		{
			for(int i = 0; ; i++) {
				var hr = Cpp.Cpp_AccFromPoint(p, flags, out var a);
				if(hr == 0) return new Acc(a);
				if(i < 2) continue;
				if(flags.Has_(AXYFlags.NoThrow)) return null;
				_WndThrow(hr, Wnd.FromXY(p.X, p.Y, WXYFlags.Raw), "*get accessible object from point.");
			}
		}

		/// <summary>
		/// Gets accessible object from point.
		/// Uses API <msdn>AccessibleObjectFromPoint</msdn>.
		/// </summary>
		/// <param name="x">X coordinate in screen.</param>
		/// <param name="y">Y coordinate in screen.</param>
		/// <param name="co">Can be used to specify screen (see <see cref="Screen_.FromObject"/>) and/or whether x y are relative to the work area.</param>
		/// <param name="flags"></param>
		/// <exception cref="CatException">Failed. For example, window of a higher UAC integrity level process.</exception>
		public static Acc FromXY(Coord x, Coord y, CoordOptions co = null, AXYFlags flags = 0)
		{
			var p = Coord.Normalize(x, y, co);
			return FromXY(p, flags);
		}

		/// <summary>
		/// Gets accessible object from mouse cursor (pointer) position.
		/// Uses API <msdn>AccessibleObjectFromPoint</msdn>.
		/// </summary>
		/// <param name="flags"></param>
		/// <exception cref="CatException">Failed. For example, window of a higher UAC integrity level process.</exception>
		public static Acc FromMouse(AXYFlags flags = 0)
		{
			return FromXY(Mouse.XY, flags);
		}

		/// <summary>
		/// Gets the accessible object that has the keyboard focus.
		/// Returns null if fails.
		/// </summary>
		/// <param name="useUIAutomation">
		/// Use UI Automation API.
		/// Need this with windows that don't support accessible objects but support UI Automation elements. Can be used with most other windows too.
		/// More info: <see cref="AFFlags.UIA"/>.
		/// </param>
		public static Acc Focused(bool useUIAutomation = false)
		{
			var w = Wnd.WndFocused;
			g1:
			if(w.Is0) return null;
			int hr = Cpp.Cpp_AccGetFocused(w, useUIAutomation ? 1 : 0, out var a);
			if(hr != 0) {
				var w2 = Wnd.WndFocused;
				if(w2 != w) { w = w2; goto g1; }
				return null;
			}
			return new Acc(a);
			//FUTURE: wait, like FromXY.
		}

		/// <summary>
		/// Gets the accessible object that generated the event that is currently being processed by the callback function used with API <msdn>SetWinEventHook</msdn>.
		/// Returns null if failed. Suports <see cref="Native.GetError"/>.
		/// </summary>
		/// <param name="w"></param>
		/// <param name="idObject"></param>
		/// <param name="idChild"></param>
		/// <remarks>
		/// The parameters are of the callback function.
		/// Uses API <msdn>AccessibleObjectFromEvent</msdn>.
		/// Often fails because the object already does not exist, because the callback function is called asynchronously, especially when the event is OBJECT_DESTROY, OBJECT_HIDE, SYSTEM_*END.
		/// Returns null if failed. Always check the return value, to avoid NullReferenceException. An exception in a callback function used with API <msdn>SetWinEventHook</msdn> kills this process.
		/// </remarks>
		public static Acc FromEvent(Wnd w, int idObject, int idChild)
		{
			int hr = Api.AccessibleObjectFromEvent(w, idObject, idChild, out var iacc, out var v);
			if(hr == 0 && iacc == default) hr = Api.E_FAIL;
			if(hr != 0) { Native.SetError(hr); return null; }
			int elem = v.vt == Api.VARENUM.VT_I4 ? v.ValueInt : 0;
			return new Acc(iacc, elem);
		}

#if false //rejected: not useful. Maybe in the future.
		/// <summary>
		/// Gets accessible object from a COM object of any type that supports it.
		/// Returns null if fails.
		/// </summary>
		/// <param name="x">Unmanaged COM object.</param>
		/// <remarks>
		/// The COM object type can be IAccessible, IAccessible2, IHTMLElement, ISimpleDOMNode or any other COM interface type that can give <msdn>IAccessible</msdn> interface pointer through API <msdn>IUnknown.QueryInterface</msdn> or <msdn>IServiceProvider.QueryService</msdn>.
		/// For IHTMLElement and ISimpleDOMNode returns null if the HTML element is not an accessible object. Then you can try to get accessible object of its parent HTML element, parent's parent and so on, until succeeds.
		/// </remarks>
		public static Acc FromComObject(IntPtr x)
		{
			if(x == default) return null;
			if(Util.Marshal_.QueryInterface(x, out IntPtr iacc, ref Api.IID_IAccessible)
				|| Util.Marshal_.QueryService(x, out iacc, ref Api.IID_IAccessible)
				) return new Acc(iacc);
			return null;
		}

		/// <summary>
		/// Gets accessible object from a COM object of any type that supports it.
		/// Returns null if fails.
		/// </summary>
		/// <param name="x">Managed COM object.</param>
		/// <remarks>
		/// The COM object type can be IAccessible, IAccessible2, IHTMLElement, ISimpleDOMNode or any other COM interface type that can give <msdn>IAccessible</msdn> interface pointer through API <msdn>IUnknown.QueryInterface</msdn> or <msdn>IServiceProvider.QueryService</msdn>.
		/// For IHTMLElement and ISimpleDOMNode returns null if the HTML element is not an accessible object. Then you can try to get accessible object of its parent HTML element, parent's parent and so on, until succeeds.
		/// </remarks>
		public static Acc FromComObject(object x)
		{
			if(x == null) return null;

			//FUTURE: support UIA. Don't use LegacyIAccessible, it work not with all windows. Instead wrap in UIAccessible.
			//if(x is UIA.IElement e) { //info: IElement2-7 are IElement too
			//	var pat = e.GetCurrentPattern(UIA.PatternId.LegacyIAccessible) as UIA.ILegacyIAccessiblePattern;
			//	x = pat?.GetIAccessible();
			//	if(x == null) return null;
			//}

			var ip = Marshal.GetIUnknownForObject(x);
			if(ip == default) return null;
			try { return FromComObject(ip); }
			finally { Marshal.Release(ip); }
		}
#endif

		//FUTURE
		//public static Acc FromImage(SIResult foundImage, Coord x=default, Coord y=default, bool noThrow = false)
		//{
		//	return null;
		//}

		/// <summary>
		/// Used only for debug.
		/// </summary>
		enum _FuncId { name = 1, value, description, default_action, role, state, rectangle, parent_object, child_object, container_window, child_count, child_objects, help_text, keyboard_shortcut, html, selection, uiaAutomationId }

		/// <summary>
		/// Calls Native.SetError and returns hr.
		/// In Debug config also outputs error in red.
		/// If hr looks like not an error but just the property or action is unavailable, changes it to S_FALSE and does not show error. These are: S_FALSE, DISP_E_MEMBERNOTFOUND, E_NOTIMPL.
		/// _FuncId also can be char, like (_FuncId)'n' for name.
		/// </summary>
		int _Hresult(_FuncId funcId, int hr)
		{
			if(hr != 0) {
				switch(hr) {
				case Api.DISP_E_MEMBERNOTFOUND: case Api.E_NOTIMPL: hr = Api.S_FALSE; break;
				case (int)Cpp.EError.InvalidParameter: throw new ArgumentException("Invalid argument value.");
				default: Debug.Assert(!Cpp.IsCppError(hr)); break;
				}
#if DEBUG
				if(hr != Api.S_FALSE) {
					_DebugPropGet(funcId, hr);
				}
#endif
			}
			Native.SetError(hr);
			return hr;
		}

#if DEBUG
		void _DebugPropGet(_FuncId funcId, int hr)
		{
			if(t_debugNoRecurse || _Disposed) return;

			if(funcId >= (_FuncId)'A') {
				switch((char)funcId) {
				case 'R': funcId = _FuncId.role; break;
				case 'n': funcId = _FuncId.name; break;
				case 'v': funcId = _FuncId.value; break;
				case 'd': funcId = _FuncId.description; break;
				case 'h': funcId = _FuncId.help_text; break;
				case 'a': funcId = _FuncId.default_action; break;
				case 'k': funcId = _FuncId.keyboard_shortcut; break;
				case 's': funcId = _FuncId.state; break;
				case 'r': funcId = _FuncId.rectangle; break;
				case 'u': funcId = _FuncId.uiaAutomationId; break;
				}
			}

			if(hr == Api.E_FAIL && funcId == _FuncId.default_action) return; //many in old VS etc
			t_debugNoRecurse = true;
			try {
				var s = ToString();
				Print($"<><c 0xff>-{funcId}, 0x{hr:X} - {Native.GetErrorMessage(hr)}    {s}</c>");
			}
			finally { t_debugNoRecurse = false; }
		}
		[ThreadStatic] static bool t_debugNoRecurse;
#endif

		/// <summary>
		/// Formats string from main properties of this accessible object.
		/// </summary>
		/// <remarks>
		/// The string starts with role. Other properties have format like x="value", where x is a property character like with <see cref="GetMultipleProperties"/>; character e is <see cref="SimpleElementId"/>. HTML attributes have format @name="value". In string values are used C# escape sequences, for example \r\n for new line.
		/// Indentation depends on <see cref="Level"/>.
		/// </remarks>
		/// <example>
		/// Displays all visible accessible objects in Chrome web page.
		/// <code><![CDATA[
		/// Output.Clear();
		/// var w = Wnd.Find("*- Google Chrome").OrThrow();
		/// Acc.Find(w, "web:", also: o => { Print(o); return false; }); //Print calls ToString
		/// ]]></code>
		/// </example>
		/// <seealso cref="PrintAll"/>
		public override string ToString()
		{
			if(_Disposed) return "<disposed>";

			string props = "Rnsvdarw@"; if(_elem != 0 || !_misc.flags.Has_(_MiscFlags.InProc)) props = "Rnsvdarw";
			var k = GetMultipleProperties(props);

			var s = Util.LibStringBuilderCache.Acquire();

			if(Level > 0) s.Append(' ', Level);
			string t = k[0] as string;
			s.Append(t);
			for(int i = 1; i < k.Length; i++) {
				t = null;
				char q1 = '\"', q2 = q1;
				switch(k[i]) {
				case string str:
					if(str.Length != 0) t = str.Limit_(250).Escape_();
					break;
				case AccSTATE state:
					if(state != 0) { t = state.ToString(); q1 = '('; q2 = ')'; }
					break;
				case RECT rect:
					if(!rect.Is0) { t = rect.ToString(); q1 = '\0'; }
					break;
				case Wnd w:
					t = w.ClassName;
					break;
				case Dictionary<string, string> dict:
					if(dict.Count != 0) {
						foreach(var kv in dict) {
							s.Append(",  @"); s.Append(kv.Key); s.Append('='); s.Append('\"');
							s.Append(kv.Value.Limit_(250).Escape_()); s.Append('\"');
						}
						continue;
					}
					break;
				}
				if(t == null) continue;
				s.Append(",  "); s.Append(props[i]); s.Append('=');
				if(q1 != '\0') s.Append(q1);
				s.Append(t);
				if(q1 != '\0') s.Append(q2);
			}
			if(SimpleElementId != 0) { s.Append(",  e="); s.Append(SimpleElementId); }

			return s.ToStringCached_();
		}

		/// <summary>
		/// Displays properties of all found accessible objects of window w.
		/// </summary>
		/// <remarks>
		/// Uses <see cref="ToString"/>.
		/// Catches exceptions. On exception prints $"!exception! exceptionType exceptionMessage".
		/// Parameters are the same as of <see cref="Find(Wnd, string, string, string, AFFlags, Func{Acc, bool}, int)"/>.
		/// By default skips invisible objects and objects in menus. Use flags to include them.
		/// Chrome web page accessible objects normally are disabled (missing) when it starts. Use role prefix "web:" or "chrome:" to enable. See example.
		/// </remarks>
		/// <example>
		/// Displays visible accessible objects in Chrome web page.
		/// <code><![CDATA[
		/// Output.Clear();
		/// var w = Wnd.Find("*- Google Chrome").OrThrow();
		/// Print("---- all ----");
		/// Acc.PrintAll(w, "web:");
		/// Print("---- links ----");
		/// Acc.PrintAll(w, "web:LINK");
		/// ]]></code>
		/// </example>
		public static void PrintAll(Wnd w, string role = null, AFFlags flags = 0, string prop = null)
		{
			try {
				Find(w, role, null, prop, flags, also: o => { Print(o); return false; });
			}catch(Exception ex) { Print($"!exception! {ex.GetType().Name} {ex.Message}"); }
		}
	}
}
