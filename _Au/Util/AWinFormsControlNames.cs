﻿using System;
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

using Au.Types;

namespace Au.Util
{
	/// <summary>
	/// Gets programming names of .NET Windows Forms controls.
	/// </summary>
	/// <remarks>
	/// Usually each control has a unique name. It's the Control.Name property. Useful to identify controls without a classic name/text.
	/// The control id of these controls is not useful, it is not constant.
	/// </remarks>
	public sealed class AWinFormsControlNames : IDisposable
	{
		AProcessMemory _pm;
		AWnd _w;

		///
		public void Dispose()
		{
			if(_pm != null) { _pm.Dispose(); _pm = null; }
			GC.SuppressFinalize(this);
		}

		static readonly int WM_GETCONTROLNAME = Api.RegisterWindowMessage("WM_GETCONTROLNAME");

		/// <summary>
		/// Prepares to get control names.
		/// </summary>
		/// <param name="w">Any top-level or child window of that process.</param>
		/// <exception cref="AuWndException">w invalid.</exception>
		/// <exception cref="AuException">Failed to allocate process memory (see <see cref="AProcessMemory"/>) needed to get control names, usually because of [](xref:uac).</exception>
		public AWinFormsControlNames(AWnd w)
		{
			_pm = new AProcessMemory(w, 4096); //throws
			_w = w;
		}

		/// <summary>
		/// Gets control name.
		/// Returns null if fails or the name is empty.
		/// </summary>
		/// <param name="c">The control. Can be a top-level window too. Must be of the same process as the window specified in the constructor.</param>
		public string GetControlName(AWnd c)
		{
			if(_pm == null) return null;
			if(!IsWinFormsControl(c)) return null;
			if(!c.SendTimeout(5000, out var R, WM_GETCONTROLNAME, 4096, _pm.Mem) || (int)R < 1) return null;
			int len = (int)R - 1;
			if(len == 0) return "";
			return _pm.ReadUnicodeString(len);
		}

		/// <summary>
		/// Returns true if window class name starts with "WindowsForms".
		/// Usually it means that we can get Windows Forms control name of w and its child controls.
		/// </summary>
		/// <param name="w">The window. Can be top-level or control.</param>
		public static bool IsWinFormsControl(AWnd w)
		{
			return w.ClassNameIs("WindowsForms*");
		}

		/// <summary>
		/// Gets the programming name of a Windows Forms control.
		/// Returns null if it is not a Windows Forms control or if fails.
		/// </summary>
		/// <param name="c">The control. Can be top-level window too.</param>
		/// <remarks>This function is easy to use and does not throw excaptions. However, when you need names of multiple controls of a single window, better create an AWinFormsControlNames instance (once) and for each control call its GetControlNameOrText method, it will be faster.</remarks>
		public static string GetSingleControlName(AWnd c)
		{
			if(!IsWinFormsControl(c)) return null;
			try {
				using(var x = new AWinFormsControlNames(c)) return x.GetControlName(c);
			}
			catch { }
			return null;
		}

		//Don't use this cached version, it does not make significantly faster. Also, keeping process handle in such a way is not good, would need to use other thread to close it after some time.
		///// <summary>
		///// Gets programming name of a Windows Forms control.
		///// Returns null if it is not a Windows Forms control or if fails.
		///// </summary>
		///// <param name="c">The control. Can be top-level window too.</param>
		///// <remarks>When need to get control names repeatedly or quite often, this function can be faster than creating AWinFormsControlNames instance each time and calling its GetControlNameOrText method, because this function remembers the last used process etc. Also it is easier to use and does not throw exceptions.</remarks>
		//public static string GetSingleControlName(AWnd c)
		//{
		//	if(!IsWinFormsControl(c)) return null;
		//	uint pid = c.ProcessId; if(pid == 0) return null;
		//	lock (_prevLock) {
		//		if(pid != _prevPID || ATime.PerfMilliseconds - _prevTime > 1000) {
		//			if(_prev != null) { _prev.Dispose(); _prev = null; }
		//			try { _prev = new AWinFormsControlNames(c); } catch { }
		//			//AOutput.Write("new");
		//		} //else AOutput.Write("cached");
		//		_prevPID = pid; _prevTime = ATime.PerfMilliseconds;
		//		if(_prev == null) return null;
		//		return _prev.GetControlNameOrText(c);
		//	}
		//}
		//static AWinFormsControlNames _prev; static uint _prevPID; static long _prevTime; static object _prevLock = new object(); //cache
	}
}
