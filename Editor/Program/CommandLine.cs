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
using System.Windows.Forms;
using System.Drawing;
//using System.Linq;
using System.Xml.Linq;

using Au;
using Au.Types;
using static Au.NoClass;
using static Program;

static class CommandLine
{
	/// <summary>
	/// Processes command line of this program.
	/// Returns true if this instance must exit: 1. If finds previous program instance; then sends the command line to it if need. 2. If incorrect command line.
	/// </summary>
	public static bool OnProgramStarted(string[] a)
	{
		string s = null;
		int cmd = 0;
		if(a.Length > 0) {
			//Print(a);

			for(int i = 0; i < a.Length; i++) if(a[i].StartsWith_('-')) a[i] = a[i].ReplaceAt_(0, 1, "/");
			for(int i = 0; i < a.Length; i++) if(a[i].StartsWith_('/')) a[i] = a[i].ToLower_();

			s = a[0];
			if(s.StartsWith_('/')) {
				for(int i = 0; i < a.Length; i++) {
					s = a[i];
					switch(s) {
					//case "/x":
					//	if(cmd != 0 || ++i == a.Length) { Console.WriteLine("/x used incorrectly"); return true; }
					//	cmd = ;
					//	break;
					default:
						Console.WriteLine("unknown: " + s);
						return true;
					}
				}
			} else { //one or more files
				if(a.Length == 1 && FilesModel.IsWorkspaceDirectory(s)) {
					switch(cmd = AuDialog.ShowEx("Workspace", s, "1 Import|2 Open|0 Cancel", footerText: FilesModel.GetSecurityInfo(true))) {
					case 1: _importWorkspace = s; break;
					case 2: WorkspaceDirectory = s; break;
					}
				} else {
					cmd = 3;
					_importFiles = a;
				}
			}
		}

		var w = Wnd.FindFast(null, c_msgClass);
		if(w.Is0) return false;

		if(a.Length == 0) { //activate main window
			Wnd wMain = (Wnd)w.Send(Api.WM_USER);
			if(!wMain.Is0) wMain.ActivateLL();
		}

		switch(cmd) {
		case 3: //import files
			s = string.Join("\0", a);
			break;
		}
		if(cmd != 0) {
			Wnd.Misc.CopyDataStruct.SendString(w, cmd, s);
		}
		return true;
	}

	public static void OnAfterCreatedFormAndOpenedWorkspace()
	{
		try {
			if(_importWorkspace != null) Model.ImportWorkspace(_importWorkspace);
			else if(_importFiles != null) Model.ImportFiles(_importFiles);
		}
		catch(Exception ex) { Print(ex.Message); }

		Wnd.Misc.CopyDataStruct.EnableReceivingWM_COPYDATA();
		Wnd.Misc.MyWindow.RegisterClass(c_msgClass);
		_msgWnd = new MsgWindow();
		_msgWnd.CreateMessageWindow(c_msgClass);
	}

	/// <summary>
	/// null or workspace file specified in command line.
	/// </summary>
	public static string WorkspaceDirectory;

	static string _importWorkspace;
	static string[] _importFiles;

	const string c_msgClass = "Au.Editor.Msg";
	static MsgWindow _msgWnd;

	class MsgWindow :Wnd.Misc.MyWindow
	{
		protected override LPARAM WndProc(Wnd w, int message, LPARAM wParam, LPARAM lParam)
		{
			switch(message) {
			case Api.WM_COPYDATA:
				if(MainForm.IsClosed) return default;
				try { return _WmCopyData(wParam, lParam); }
				catch(Exception ex) { Print(ex.Message); }
				return default;
			case Api.WM_USER: //return main form window handle
				if(MainForm.IsClosed) return default;
				return MainForm.Handle;
			}

			return base.WndProc(w, message, wParam, lParam);
		}
	}

	static unsafe LPARAM _WmCopyData(LPARAM wParam, LPARAM lParam)
	{
		var c = new Wnd.Misc.CopyDataStruct(lParam);
		int action = c.DataId;
		bool isString = action < 100;
		string s = isString ? c.GetString() : null;
		byte[] b = isString ? null : c.GetBytes();
		switch(action) {
		case 1:
			Model.ImportWorkspace(s);
			break;
		case 2:
			Panels.Files.LoadWorkspace(s);
			break;
		case 3:
			Api.ReplyMessage(1); //avoid 'wait' cursor while we'll show task dialog
			Model.ImportFiles(s.Split_("\0"));
			break;
		case 99: //run script from Au.CL.exe command line
		case 100: //run script from script (AuTask.Run/RunWait)
			int mode = (int)wParam; //1 - wait, 3 - wait and get AuTask.WriteResult output
			string script; string[] args; string pipeName = null;
			if(action == 99) {
				var a = Au.Util.StringMisc.CommandLineToArray(s); if(a.Length == 0) return 0;
				int nRemove = 0;
				if(0 != (mode & 2)) pipeName = a[nRemove++];
				script = a[nRemove++];
				args = a.Length == nRemove ? null : a.RemoveAt_(0, nRemove);
			} else {
				var d = Au.Util.LibSerializer.Deserialize(b);
				script = d[0]; args = d[1]; pipeName = d[2];
			}
			var f = Model?.Find(script, false);
			if(f == null) {
				if(action == 99) Print($"Command line: script '{script}' not found."); //else the caller script will throw exception
				return (int)AuTask.ERunResult.notFound;
			}
			return Run.CompileAndRun(true, f, args, noDefer: 0 != (mode & 1), pipeName: pipeName);
		default:
			Debug.Assert(false);
			return 0;
		}
		return 1;
	}
}
