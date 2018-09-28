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
//using System.Xml.Linq;

using Au;
using Au.Types;
using static Au.NoClass;
using static Program;
using Au.Controls;
using static Au.Controls.Sci;

class PanelOutput :Control
{
	SciOutput _c;
	Queue<Au.Util.OutputServer.Message> _history;

	//public SciControl Output { get => _c; }

	public PanelOutput()
	{
		_c = new SciOutput();
		_c.Dock = DockStyle.Fill;
		_c.AccessibleName = this.Name = "Output";
		this.Controls.Add(_c);

		_history = new Queue<Au.Util.OutputServer.Message>();
		OutputServer.SetNotifications(_GetServerMessages, this);

		_c.HandleCreated += _c_HandleCreated;
	}

	void _GetServerMessages()
	{
		_c.Tags.OutputServerProcessMessages(OutputServer, m =>
		{
			if(m.Type != Au.Util.OutputServer.MessageType.Write) return;
			_history.Enqueue(m);
			if(_history.Count > 50) _history.Dequeue();

			//Output.LibWriteQM2(s);

			//create links in compilation errors/warnings or run time stack trace
			var s = m.Text;
			if(s.Length >= 22) {
				if(s.StartsWith_("<><Z #") && s.EqualsAt_(12, ">Compilation: ")) { //compilation
					if(s_rx1 == null) s_rx1 = new Regex_(@"(?m)^\[(.+?)(\((\d+),(\d+)\))?\]: ");
					m.Text = s_rx1.Replace(s, x =>
					{
						var f = Model.FindByFilePath(x[1].Value);
						if(f == null) return x[0].Value;
						return $"<+open {f.Guid}|{x[3].Value}|{x[4].Value}>{f.Name}{x[2].Value}<>: ";
					});
				} else if(s.Contains(":line ")) { //stack trace
					if(s_rx2 == null) s_rx2 = new Regex_(@"(?m)^(\s+at .+) in (.+?):line (\d+)$");
					var s2 = s_rx2.Replace(s, x =>
					{
						var f = Model.FindByFilePath(x[2].Value);
						if(f == null) return x[0].Value;
						var line = x[3].Value;
						return $"{x[1].Value.Limit_(70)} in <+open {f.Guid}|{line}>{f.Name}<>:line {line}";
					});
					if(!ReferenceEquals(s, s2)) {
						if(!s2.StartsWith_("<>")) s2 = "<>" + s2;
						m.Text = s2;
					}
				}
				//SHOULDDO: escape non-link text with <_>...</_>.
			}
		});
	}
	static Regex_ s_rx1, s_rx2;

	protected override void OnGotFocus(EventArgs e) { _c.Focus(); }

	public void Clear() { _c.ST.ClearText(); }

	public void Copy() { _c.Call(SCI_COPY); }

	//not override void OnHandleCreated, because then _c handle still not created, and we need to Call
	private void _c_HandleCreated(object sender, EventArgs e)
	{
		var h = _c.Handle;
		_inInitSettings = true;
		if(WrapLines) WrapLines = true;
		if(WhiteSpace) WhiteSpace = true;
		if(Topmost) Strips.CheckCmd("Tools_Output_Topmost", true); //see also OnParentChanged, below
		_inInitSettings = false;
	}
	bool _inInitSettings;

	public bool WrapLines
	{
		get => Settings.Get("Tools_Output_WrapLines", false);
		set
		{
			Debug.Assert(!_inInitSettings || value);
			if(!_inInitSettings) Settings.Set("Tools_Output_WrapLines", value);
			//_c.Call(SCI_SETWRAPVISUALFLAGS, SC_WRAPVISUALFLAG_START | SC_WRAPVISUALFLAG_END); //in SciControl.OnHandleCreated
			//_c.Call(SCI_SETWRAPINDENTMODE, SC_WRAPINDENT_INDENT); //in SciControl.OnHandleCreated
			_c.Call(SCI_SETWRAPMODE, value ? SC_WRAP_WORD : 0);
			Strips.CheckCmd("Tools_Output_WrapLines", value);
		}
	}

	public bool WhiteSpace
	{
		get => Settings.Get("Tools_Output_WhiteSpace", false);
		set
		{
			Debug.Assert(!_inInitSettings || value);
			if(!_inInitSettings) Settings.Set("Tools_Output_WhiteSpace", value);
			_c.Call(SCI_SETWHITESPACEFORE, 1, 0xFF0080);
			_c.Call(SCI_SETVIEWWS, value);
			Strips.CheckCmd("Tools_Output_WhiteSpace", value);
		}
	}

	public bool Topmost
	{
		get => Settings.Get("Tools_Output_Topmost", false);
		set
		{
			var p = Panels.PanelManager.GetPanel(this);
			if(value) p.Floating = true;
			if(p.Floating) _SetTopmost(value);
			Settings.Set("Tools_Output_Topmost", value);
			Strips.CheckCmd("Tools_Output_Topmost", value);
		}
	}

	void _SetTopmost(bool on)
	{
		var w = ((Wnd)this).Window;
		if(on) {
			w.Owner = default;
			w.ZorderTopmost();
			//w.SetExStyle(Native.WS_EX.APPWINDOW, SetAddRemove.Add);
			//Wnd.GetWnd.Root.ActivateLL(); w.ActivateLL(); //let taskbar add button
		} else {
			w.ZorderNoTopmost();
			w.Owner = (Wnd)MainForm;
		}
	}

	protected override void OnParentChanged(EventArgs e)
	{
		if(Parent is Form && Topmost) Timer_.After(1, () => _SetTopmost(true));

		base.OnParentChanged(e);
	}

	class SciOutput :AuScintilla
	{
		public SciOutput()
		{
			InitReadOnlyAlways = true;
			InitTagsStyle = TagsStyle.AutoWithPrefix;
			InitImagesStyle = ImagesStyle.ImageTag;

			SciTags.AddCommonLinkTag("open", s => _OpenLink(false, s));
			SciTags.AddCommonLinkTag("+open", s => _OpenLink(true, s));
			SciTags.AddCommonLinkTag("script", s => _RunScript(s));
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);

			ST.MarginWidth(1, 3);
			ST.StyleBackColor(STYLE_DEFAULT, 0xF7F7F7);
			ST.StyleFont(STYLE_DEFAULT, "Courier New", 8);
			ST.StyleClearAll();
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			switch(e.Button) {
			case MouseButtons.Middle:
				ST.ClearText();
				break;
			}
			base.OnMouseDown(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			switch(e.Button) {
			case MouseButtons.Right:
				Strips.ddOutput.ShowAsContextMenu_();
				break;
			}
			base.OnMouseUp(e);
		}

		void _OpenLink(bool isGuid, string s)
		{
			//Print(s);
			var a = s.Split('|');
			var fn = isGuid ? Model.FindByGUID(a[0]) : Model.Find(a[0], false);
			if(fn == null || !Model.SetCurrentFile(fn)) return;
			var doc = Panels.Editor.ActiveDoc;
			doc.Focus();
			if(a.Length == 1) return;
			int line = a[1].ToInt_(0) - 1; if(line < 0) return;
			int column = a.Length == 2 ? -1 : a[2].ToInt_() - 1;

			var t = doc.ST;
			int i = t.LineStart(line);
			if(column > 0) i = t.Call(SCI_POSITIONRELATIVE, i, column); //not SCI_FINDCOLUMN, it calculates tabs
			t.GoToPos(i);
		}

		void _RunScript(string s)
		{
			var a = s.Split('|');
			var fn = Model.Find(a[0], false);
			if(fn == null) return;
			Run.CompileAndRun(true, fn, a.Length == 1 ? null : a.RemoveAt_(0));
		}
	}
}
