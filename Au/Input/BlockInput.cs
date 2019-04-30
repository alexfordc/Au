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
using System.Runtime.ExceptionServices;
//using System.Linq;

using Au.Types;
using static Au.NoClass;

namespace Au
{
	/// <summary>
	/// Blocks keyboard and/or mouse input events from reaching applications.
	/// </summary>
	/// <remarks>
	/// Uses keyboard and/or mouse hooks. Does not use API <b>BlockInput</b>, it does not work on current Windows versions.
	/// Blocks hardware-generated events and software-generated events, except generated by functions of this library.
	/// Functions of this library that send keys or text use this class internally, to block user-pressed keys and resend them afterwards (see <see cref="ResendBlockedKeys"/>).
	/// Does not block:
	/// - In windows of the same thread that started blocking. For example, if your script shows a message box, the user can click its buttons.
	/// - In windows of higher [](xref:uac) integrity level (IL) processes, unless this process has uiAccess IL.
	/// - In special desktops/screens, such as when you press Ctrl+Alt+Delete or launch an admin program that requires UAC elevation. See also <see cref="ResumeAfterCtrlAltDelete"/>.
	/// - Some Windows hotkeys, such as Ctrl+Alt+Delete and Win+L.
	/// - Keyboard hooks don't work in windows of this process if this process uses direct input or raw input API.
	/// 
	/// To stop blocking, can be used the 'using' pattern, like in the example. Or the 'try/finally' pattern, where the finally block calls <see cref="Dispose"/> or <see cref="Stop"/>. Also automatically stops when this thread ends. Users can stop with Ctrl+Alt+Delete.
	/// </remarks>
	/// <example>
	/// <code><![CDATA[
	/// using(new BlockUserInput(BIEvents.All)) {
	/// 	Print("blocked");
	/// 	5.s();
	/// }
	/// Print("not blocked");
	/// ]]></code>
	/// </example>
	public unsafe class BlockUserInput : IDisposable
	{
		LibHandle _syncEvent, _stopEvent;
		LibHandle _threadHandle;
		Keyb _blockedKeys;
		long _startTime;
		BIEvents _block;
		int _threadId;
		bool _disposed;
		bool _discardBlockedKeys;

		//note: don't use API BlockInput because:
		//	UAC. Fails if our process has Medium IL.
		//	Too limited, eg cannot block only keys or only mouse.

		/// <summary>
		/// This constructor does nothing (does not call <see cref="Start"/>).
		/// </summary>
		public BlockUserInput() { }

		/// <summary>
		/// This constructor calls <see cref="Start"/>.
		/// </summary>
		/// <exception cref="ArgumentException">*what* is 0.</exception>
		public BlockUserInput(BIEvents what)
		{
			Start(what);
		}

		/// <summary>
		/// Starts blocking.
		/// </summary>
		/// <exception cref="ArgumentException">*what* is 0.</exception>
		/// <exception cref="InvalidOperationException">Already started.</exception>
		public void Start(BIEvents what)
		{
			if(_disposed) throw new ObjectDisposedException(nameof(BlockUserInput));
			if(_block != 0) throw new InvalidOperationException();
			if(!what.HasAny_(BIEvents.All)) throw new ArgumentException();

			_block = what;
			_startTime = Time.WinMilliseconds;

			_syncEvent = Api.CreateEvent(false);
			_stopEvent = Api.CreateEvent(false);
			_threadHandle = Api.OpenThread(Api.SYNCHRONIZE, false, _threadId = Api.GetCurrentThreadId());

			ThreadPool.QueueUserWorkItem(_this => (_this as BlockUserInput)._ThreadProc(), this);
			//SHOULDDO: what if thread pool is very busy? Eg if scripts use it incorrectly. Maybe better have own internal pool.

			Api.WaitForSingleObject(_syncEvent, Timeout.Infinite);
			GC.KeepAlive(this);
		}

		/// <summary>
		/// Calls <see cref="Stop"/>.
		/// </summary>
		public void Dispose()
		{
			if(!_disposed) {
				_disposed = true;
				Stop();
				GC.SuppressFinalize(this);
			}
		}

		///
		~BlockUserInput() => _CloseHandles();

		void _CloseHandles()
		{
			if(!_syncEvent.Is0) {
				_syncEvent.Dispose();
				_stopEvent.Dispose();
				_threadHandle.Dispose();
			}
		}

		/// <summary>
		/// Stops blocking.
		/// Plays back blocked keys if need. See <see cref="ResendBlockedKeys"/>.
		/// Does nothing if currently is not blocking.
		/// </summary>
		/// <param name="discardBlockedKeys">Do not play back blocked keys recorded because of <see cref="ResendBlockedKeys"/>.</param>
		public void Stop(bool discardBlockedKeys = false)
		{
			if(_block == 0) return;
			_block = 0;
			_discardBlockedKeys = discardBlockedKeys;
			Api.SetEvent(_stopEvent);
			Api.WaitForSingleObject(_syncEvent, Timeout.Infinite);
			_CloseHandles();
		}

		const int c_maxResendTime = 10000;

		void _ThreadProc()
		{
			Util.WinHook hk = null, hm = null; Util.AccHook hwe = null;
			try {
				try {
					if(_block.Has_(BIEvents.Keys))
						hk = Util.WinHook.Keyboard(_keyHookProc ?? (_keyHookProc = _KeyHookProc));
					if(_block.HasAny_(BIEvents.MouseClicks | BIEvents.MouseMoving))
						hm = Util.WinHook.Mouse(_mouseHookProc ?? (_mouseHookProc = _MouseHookProc));
				}
				catch(AuException e1) { Debug_.Print(e1); _block = 0; return; } //failed to hook

				//This prevents occassional inserting a foreign key after the first our-script-pressed key.
				//To reproduce, let our script send small series of chars in loop, and simultaneously a foreign script send other chars.
				Time.DoEvents();

				//Print("started");
				Api.SetEvent(_syncEvent);

				//the acc hook detects Ctrl+Alt+Del, Win+L, UAC consent, etc. SystemEvents.SessionSwitch only Win+L.
				try { hwe = new Util.AccHook(AccEVENT.SYSTEM_DESKTOPSWITCH, 0, _winEventProc ?? (_winEventProc = _WinEventProc)); }
				catch(AuException e1) { Debug_.Print(e1); } //failed to hook

				WaitFor.LibWait(-1, WHFlags.DoEvents, _stopEvent, _threadHandle);

				if(_blockedKeys != null && !_discardBlockedKeys) {
					if(Time.WinMilliseconds - _startTime < c_maxResendTime) {
						_blockedKeys.LibSendBlocked();
					}
					//TODO: send all 'up' always
				}
				//Print("ended");
			}
			finally {
				_blockedKeys = null;
				hk?.Dispose();
				hm?.Dispose();
				hwe?.Dispose();
				Api.SetEvent(_syncEvent);
			}
			GC.KeepAlive(this);
		}

		Action<HookData.Keyboard> _keyHookProc;
		Action<HookData.Mouse> _mouseHookProc;
		Action<HookData.AccHookData> _winEventProc;

		void _KeyHookProc(HookData.Keyboard x)
		{
			if(_DontBlock(x.IsInjected, x.dwExtraInfo, x.vkCode)) {
				//Print("ok", x.vkCode, !x.IsUp);
				return;
			}
			//Print(message, x.vkCode);

			//if(x.vkCode == KKey.Delete && !x.IsUp) {
			//	//Could detect Ctrl+Alt+Del here. But SetWinEventHook(SYSTEM_DESKTOPSWITCH) is better.
			//}

			if(ResendBlockedKeys && Time.WinMilliseconds - _startTime < c_maxResendTime) {
				if(_blockedKeys == null) _blockedKeys = new Keyb(Opt.Static.Key);
				//Print("blocked", x.vkCode, !x.IsUp);
				_blockedKeys.LibAddRaw(x.vkCode, (ushort)x.scanCode, x.LibSendInputFlags);
			}
			x.BlockEvent();
		}

		void _MouseHookProc(HookData.Mouse x)
		{
			bool isMMove = x.Event == HookData.MouseEvent.Move;
			switch(_block & (BIEvents.MouseClicks | BIEvents.MouseMoving)) {
			case BIEvents.MouseClicks | BIEvents.MouseMoving: break;
			case BIEvents.MouseClicks: if(isMMove) return; break;
			case BIEvents.MouseMoving: if(!isMMove) return; break;
			}
			if(!_DontBlock(x.IsInjected, x.dwExtraInfo, 0, isMMove)) x.BlockEvent();
		}

		bool _DontBlock(bool isInjected, LPARAM extraInfo, KKey vk = 0, bool isMMove = false)
		{
			if(_pause) return true;
			if(isInjected) {
				//if(DontBlockInjected || (extraInfo != default && extraInfo == DontBlockInjectedExtraInfo)) return true;
				if(DontBlockInjected) return true;
			}
			Wnd w;
			if(vk != 0) {
				//var a = DontBlockKeys;
				//if(a != null) foreach(var k in a) if(vk == k) return true;
				w = Wnd.Active;
			} else {
				w = isMMove ? Wnd.Active : Wnd.FromMouse();
				//note: don't use hook's pt, because of a bug in some OS versions.
				//note: for wheel it's better to use FromMouse.
			}
			if(w.ThreadId == _threadId) return true;
			return false;
		}

		void _WinEventProc(HookData.AccHookData x)
		{
			//the hook is called before and after Ctrl+Alt+Del screen. Only idEventThread different.
			//	GetForegroundWindow returns 0. WTSGetActiveConsoleSessionId returns main session.

			//Print("desktop switch"); //return;

			_startTime = 0; //don't resend Ctrl+Alt+Del and other blocked keys
			if(!ResumeAfterCtrlAltDelete)
				ThreadPool.QueueUserWorkItem(_this => (_this as BlockUserInput).Stop(), this);
		}

		/// <summary>
		/// Continue blocking when returned from a special screen where blocking is disabled: Ctrl+Alt+Delete, [](xref:uac) consent, etc.
		/// </summary>
		public bool ResumeAfterCtrlAltDelete { get; set; }

		/// <summary>
		/// Record blocked keys, and play back when stopped blocking.
		/// </summary>
		/// <remarks>
		/// Will not play back if: 1. The blocking time is &gt;= 10 seconds. 2. Detected Ctrl+Alt+Delete, [](xref:uac) consent or some other special screen. 3. Called <see cref="Pause"/>.
		/// </remarks>
		public bool ResendBlockedKeys { get; set; }

		/// <summary>
		/// Don't block software-generated key/mouse events.
		/// If false (default), only events generated by functions of this library are not blocked.
		/// </summary>
		public bool DontBlockInjected { get; set; }

		//rejected. Will be added later if need. Maybe a callback instead.
		///// <summary>
		///// Don't block software-generated key/mouse events if this extra info value was set when calling API <msdn>SendInput</msdn>.
		///// </summary>
		///// <remarks>
		///// Regardless of the property value, events generated by functions of this library are never blocked.
		///// </remarks>
		//public LPARAM DontBlockInjectedExtraInfo { get; set; }

		///// <summary>
		///// Don't block these keys.
		///// </summary>
		///// <remarks>
		///// For modifier keys use the left/right key code: LCtrl, RCtrl, LShift, RShift, LAlt, RAlt, Win, RWin.
		///// </remarks>
		//public static KKey[] DontBlockKeys { get; set; }

		/// <summary>
		/// Gets or sets whether the blocking is paused.
		/// </summary>
		/// <remarks>
		/// The 'set' function is much faster than <see cref="Stop"/>/<see cref="Start"/>. Does not remove hooks etc. Discards blocked keys.
		/// </remarks>
		public bool Pause {
			get => _pause;
			set {
				_pause = value;
				_startTime = 0; //don't resend blocked keys
			}
		}
		bool _pause;
	}
}

namespace Au.Types
{
	/// <summary>
	/// Used with <see cref="BlockUserInput"/> class to specify what user input types to block (keys, mouse).
	/// </summary>
	[Flags]
	public enum BIEvents
	{
		/// <summary>
		/// Do not block.
		/// </summary>
		None,

		/// <summary>
		/// Block keys. Except if generated by functions of this library.
		/// </summary>
		Keys = 1,

		/// <summary>
		/// Block mouse clicks and wheel. Except if generated by functions of this library.
		/// </summary>
		MouseClicks = 2,

		/// <summary>
		/// Block mouse moving. Except if generated by functions of this library.
		/// </summary>
		MouseMoving = 4,

		/// <summary>
		/// Block keys, mouse clicks, wheel and mouse moving. Except if generated by functions of this library.
		/// This flag incluses flags <b>Keys</b>, <b>MouseClicks</b> and <b>MouseMoving</b>.
		/// </summary>
		All = 7,
	}


}
