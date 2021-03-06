//#define TEST_COPYPASTE

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
using System.Windows.Forms;
using System.Drawing;
//using System.Linq;

using Au;
using Au.Types;
using Au.Controls;
using static Au.Controls.Sci;

class PanelEdit : UserControl
{
	List<SciCode> _docs = new List<SciCode>(); //documents that are actually open currently. Note: FilesModel.OpenFiles contains not only these.
	SciCode _activeDoc;

	public SciCode ZActiveDoc => _activeDoc;

	public event Action ZActiveDocChanged;

	public bool ZIsOpen => _activeDoc != null;

	public PanelEdit()
	{
		this.AccessibleName = this.Name = "Code";
		this.TabStop = false;
		this.BackColor = SystemColors.AppWorkspace;
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		_UpdateUI_IsOpen();
		_UpdateUI_EditView();
	}

	//protected override void OnGotFocus(EventArgs e) { _activeDoc?.Focus(); }

	//public SciControl SC => _activeDoc;

	public IReadOnlyList<SciCode> ZOpenDocs => _docs;

	/// <summary>
	/// If f is open (active or not), returns its SciCode, else null.
	/// </summary>
	public SciCode ZGetOpenDocOf(FileNode f) => _docs.Find(v => v.ZFile == f);

	/// <summary>
	///	If f is already open, unhides its control.
	///	Else loads f text and creates control. If fails, does not change anything.
	/// Hides current file's control.
	/// Returns false if failed to read file.
	/// Does not save text of previously active document.
	/// </summary>
	/// <param name="f"></param>
	/// <param name="newFile">Should be true if opening the file first time after creating.</param>
	public bool ZOpen(FileNode f, bool newFile)
	{
		Debug.Assert(!Program.Model.IsAlien(f));

		if(f == _activeDoc?.ZFile) return true;
		bool focus = _activeDoc?.Focused ?? false;

		var doc = ZGetOpenDocOf(f);
		if(doc != null) {
			if(_activeDoc != null) _activeDoc.Visible = false;
			_activeDoc = doc;
			_activeDoc.Visible = true;
			_UpdateUI_EditEnabled();
			ZActiveDocChanged?.Invoke();
		} else {
			var path = f.FilePath;
			byte[] text = null;
			SciText.FileLoaderSaver fls = default;
			try { text = fls.Load(path); }
			catch(Exception ex) { AOutput.Write("Failed to open file. " + ex.Message); }
			if(text == null) return false;

			if(_activeDoc != null) _activeDoc.Visible = false;
			doc = new SciCode(f, fls);
			doc.AccessibleName = f.Name;
			doc.AccessibleDescription = path;
			_docs.Add(doc);
			_activeDoc = doc;
			this.Controls.Add(doc);
			doc._Init(text, newFile);
			_UpdateUI_EditEnabled();
			ZActiveDocChanged?.Invoke();
			//CodeInfo.FileOpened(doc);
		}

		if(focus && !newFile) {
			_activeDoc.Focus();
		} else { //don't focus now, or then cannot select treeview items with keyboard etc. Focus on mouse move in editor control.
			_openFocus.onMM ??= (object sender, MouseEventArgs e) => {
				var c = sender as Control;
				if(!c.FindForm().Hwnd().IsActive) return;
				if(_openFocus.dist >= 0) { //if new file, don't focus until mouse moved away from tree
					int dist = (int)AMath.Distance(Program.Model.TreeControl.Hwnd().Rect, AMouse.XY);
					if(dist < _openFocus.dist + 10) {
						if(dist < _openFocus.dist) _openFocus.dist = dist;
						return;
					}
				}
				c.MouseMove -= _openFocus.onMM;
				c.Focus();
			};
			_activeDoc.MouseMove += _openFocus.onMM;
			_openFocus.dist = newFile ? int.MaxValue - 10 : -1;
		}

		_activeDoc.Call(SCI_SETWRAPMODE, Program.Settings.edit_wrap); //fast and does nothing if already is in that wrap state
		_activeDoc.ZImages.Visible = Program.Settings.edit_noImages ? AnnotationsVisible.ANNOTATION_HIDDEN : AnnotationsVisible.ANNOTATION_STANDARD;

		_UpdateUI_IsOpen();
		Panels.Find.ZUpdateQuickResults(true);
		return true;
	}
	(MouseEventHandler onMM, int dist) _openFocus;

	/// <summary>
	/// If f is open, closes its document and destroys its control.
	/// f can be any, not necessary the active document.
	/// Saves text before closing the active document.
	/// Does not show another document when closed the active document.
	/// </summary>
	/// <param name="f"></param>
	public void ZClose(FileNode f)
	{
		Debug.Assert(f != null);
		SciCode doc;
		if(f == _activeDoc?.ZFile) {
			Program.Model.Save.TextNowIfNeed();
			doc = _activeDoc;
			_activeDoc = null;
			ZActiveDocChanged?.Invoke();
		} else {
			doc = ZGetOpenDocOf(f);
			if(doc == null) return;
		}
		//CodeInfo.FileClosed(doc);
		doc.Dispose();
		_docs.Remove(doc);
		_UpdateUI_IsOpen();
	}

	/// <summary>
	/// Closes all documents and destroys controls.
	/// </summary>
	public void ZCloseAll(bool saveTextIfNeed)
	{
		if(saveTextIfNeed) Program.Model.Save.TextNowIfNeed();
		_activeDoc = null;
		ZActiveDocChanged?.Invoke();
		foreach(var doc in _docs) doc.Dispose();
		_docs.Clear();
		_UpdateUI_IsOpen();
	}

	public bool ZSaveText()
	{
		return _activeDoc?._SaveText() ?? true;
	}

	public void ZSaveEditorData()
	{
		_activeDoc?._SaveEditorData();
	}

	//public bool ZIsModified => _activeDoc?.IsModified ?? false;

	/// <summary>
	/// Enables/disables Edit and Run toolbars/menus and some other UI parts depending on whether a document is open in editor.
	/// </summary>
	void _UpdateUI_IsOpen(bool asynchronously = true)
	{
		bool enable = _activeDoc != null;
		if(enable != _uiDisabled_IsOpen) return;

		if(asynchronously) {
			BeginInvoke(new Action(() => _UpdateUI_IsOpen(false)));
			return;
		}
		_uiDisabled_IsOpen = !enable;

		//toolbars
		Strips.tbEdit.Enabled = enable;
		Strips.tbRun.Enabled = enable;
		//menus
		Strips.Menubar.Items["Menu_Edit"].Enabled = enable;
		Strips.Menubar.Items["Menu_Code"].Enabled = enable;
		Strips.Menubar.Items["Menu_Run"].Enabled = enable;
		//toolbar buttons
		Strips.tbFile.Items["File_Properties"].Enabled = enable;
		//drop-down menu items and submenus
		//don't disable these because can right-click...
		//Strips.ddFile.Items["File_Disable"].Enabled = enable;
		//Strips.ddFile.Items["File_Rename"].Enabled = enable;
		//Strips.ddFile.Items["File_Delete"].Enabled = enable;
		//Strips.ddFile.Items["File_Properties"].Enabled = enable;
		//Strips.ddFile.Items["File_More"].Enabled = enable;
	}
	bool _uiDisabled_IsOpen;

	/// <summary>
	/// Enables/disables commands (toolbar buttons, menu items) depending on document state such as "can undo".
	/// Called on SCN_UPDATEUI.
	/// </summary>
	internal void _UpdateUI_EditEnabled()
	{
		_EUpdateUI disable = 0;
		var d = _activeDoc;
		if(d == null) return; //we disable the toolbar and menu
		if(0 == d.Call(SCI_CANUNDO)) disable |= _EUpdateUI.Undo;
		if(0 == d.Call(SCI_CANREDO)) disable |= _EUpdateUI.Redo;
		if(0 != d.Call(SCI_GETSELECTIONEMPTY)) disable |= _EUpdateUI.Copy;
		if(disable.Has(_EUpdateUI.Copy) || d.Z.IsReadonly) disable |= _EUpdateUI.Cut;
		//if(0 == d.Call(SCI_CANPASTE)) disable |= EUpdateUI.Paste; //rejected. Often slow. Also need to see on focused etc.

		var dif = disable ^ _editDisabled; if(dif == 0) return;

		//AOutput.Write(dif);
		_editDisabled = disable;
		if(dif.Has(_EUpdateUI.Undo)) Strips.EnableCmd(nameof(CmdHandlers.Edit_Undo), !disable.Has(_EUpdateUI.Undo));
		if(dif.Has(_EUpdateUI.Redo)) Strips.EnableCmd(nameof(CmdHandlers.Edit_Redo), !disable.Has(_EUpdateUI.Redo));
		if(dif.Has(_EUpdateUI.Cut)) Strips.EnableCmd(nameof(CmdHandlers.Edit_Cut), !disable.Has(_EUpdateUI.Cut));
		if(dif.Has(_EUpdateUI.Copy)) Strips.EnableCmd(nameof(CmdHandlers.Edit_Copy), !disable.Has(_EUpdateUI.Copy));
		//if(dif.Has(EUpdateUI.Paste)) Strips.EnableCmd(nameof(CmdHandlers.Edit_Paste), !disable.Has(EUpdateUI.Paste));

	}

	_EUpdateUI _editDisabled;

	internal void _UpdateUI_EditView()
	{
		Strips.CheckCmd(nameof(CmdHandlers.Edit_WrapLines), Program.Settings.edit_wrap);
		Strips.CheckCmd(nameof(CmdHandlers.Edit_ImagesInCode), !Program.Settings.edit_noImages);
	}

	[Flags]
	enum _EUpdateUI
	{
		Undo = 1,
		Redo = 2,
		Cut = 4,
		Copy = 8,
		//Paste = 16,

	}
}
