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
using System.Linq;
using System.Xml.Linq;
using System.Collections;

using Au;
using Au.Types;
using static Au.NoClass;
using static Program;
using Aga.Controls.Tree;
using Aga.Controls.Tree.NodeControls;

partial class FilesModel :ITreeModel, Au.Compiler.IWorkspaceFiles
{
	TreeViewAdv _control;
	public TreeViewAdv TreeControl => _control;
	public readonly FileNode Root;
	public readonly int WorkspaceSN; //sequence number of workspace open in this process: 1, 2...
	static int s_workspaceSN;
	public readonly string WorkspaceFile;
	public readonly string WorkspaceDirectory;
	public readonly string WorkspaceName;
	public readonly string FilesDirectory;
	public readonly AutoSave Save;
	readonly Dictionary<uint, FileNode> _idMap;
	public readonly List<FileNode> OpenFiles;
	readonly string _dbFile;
	public readonly SqliteDB DB;
	readonly TriggersUI _triggers;
	readonly bool _importing;
	readonly bool _initedFully;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="c">Tree control. Can be null, for example when importing workspace.</param>
	/// <param name="file">Workspace file (XML).</param>
	/// <exception cref="ArgumentException">Invalid or not full path.</exception>
	/// <exception cref="Exception">XElement.Load exceptions. And possibly more.</exception>
	public FilesModel(TreeViewAdv c, string file)
	{
		_importing = c == null;
		_control = c;
		WorkspaceFile = Path_.Normalize(file);
		WorkspaceDirectory = Path_.GetDirectoryPath(WorkspaceFile);
		WorkspaceName = Path_.GetFileName(WorkspaceDirectory);
		FilesDirectory = WorkspaceDirectory + @"\files";
		if(!_importing) {
			WorkspaceSN = ++s_workspaceSN;
			File_.CreateDirectory(FilesDirectory);
			Save = new AutoSave(this);
		}
		_idMap = new Dictionary<uint, FileNode>();

		Root = FileNode.Load(WorkspaceFile, this); //recursively creates whole model tree; caller handles exceptions

		if(!_importing) {
			_dbFile = WorkspaceDirectory + @"\settings.db";
			try {
				DB = new SqliteDB(_dbFile, sql:
					//"PRAGMA journal_mode=WAL;" + //no, it does more bad than good
					"CREATE TABLE IF NOT EXISTS _misc (key TEXT PRIMARY KEY, data TEXT);" +
					"CREATE TABLE IF NOT EXISTS _editor (id INTEGER PRIMARY KEY, lines BLOB);"
					);
			}
			catch(Exception ex) {
				Print($"Failed to open file '{_dbFile}'. Will not load/save workspace settings, including lists of open files, expanded folders, markers, folding.\r\n\t{ex.ToStringWithoutStack_()}");
			}
			OpenFiles = new List<FileNode>();
			_InitClickSelect();
			_InitDragDrop();
			_InitWatcher();
			_triggers=new TriggersUI(this);
		}
		_initedFully = true;
	}

	public void Dispose()
	{
		if(_importing) return;
		if(_initedFully) {
			_triggers.Dispose();
			Tasks.OnWorkspaceClosed();
			//Save.AllNowIfNeed(); //owner FilesPanel calls this before calling this func. Because may need more code in between.
		}
		Save?.Dispose();
		if(_initedFully) {
			_UninitWatcher();
			_UninitClickSelect();
			_UninitDragDrop();
			_UninitNodeControls();
			DB?.Dispose();
		}
		_control = null;
	}

	#region node controls

	NodeIcon _ncIcon;
	NodeTextBox _ncName;

	//Called by FilesPanel
	public void InitNodeControls(NodeIcon icon, NodeTextBox name)
	{
		_ncIcon = icon;
		_ncName = name;
		_ncIcon.ValueNeeded = _ncIcon_ValueNeeded;
		_ncName.ValueNeeded = node => (node.Tag as FileNode).Name;
		_ncName.ValuePushed = (node, value) => { (node.Tag as FileNode).FileRename(value as string, false); };
		_ncName.DrawText += _ncName_DrawText;
		_control.RowDraw += _TV_RowDraw;
	}

	void _UninitNodeControls()
	{
		_ncName.DrawText -= _ncName_DrawText;
		_control.RowDraw -= _TV_RowDraw;
	}

	private object _ncIcon_ValueNeeded(TreeNodeAdv node)
	{
		var f = node.Tag as FileNode;
		//Print(f);
		Debug.Assert(node.IsLeaf != f.IsFolder);

		if(_myClipboard.Contains(f)) return EResources.GetImageUseCache(_myClipboard.cut ? "cut" : "copy");
		return f.GetIcon(node.IsExpanded);
	}

	private void _ncName_DrawText(object sender, DrawEventArgs e)
	{
		var f = e.Node.Tag as FileNode;
		if(f.IsFolder) return;
		if(f == _currentFile) {
			e.Font = Stock.FontBold;
			//e.TextColor = Color.DarkBlue;
			if(e.Node.IsSelected && e.Context.DrawSelection == DrawSelectionMode.None && _IsTextBlack)
				e.BackgroundBrush = Brushes.LightGoldenrodYellow; //yellow text rect in selected-inactive
		}
	}

	private void _TV_RowDraw(object sender, TreeViewRowDrawEventArgs e)
	{
		var f = e.Node.Tag as FileNode;
		if(f.IsFolder) return;
		if(!e.Node.IsSelected && OpenFiles.Contains(f)) {
			var g = e.Graphics;
			var r = e.RowRect; //why width 0?
			var cr = g.VisibleClipBounds;
			r.X = (int)cr.X; r.Width = (int)cr.Width;
			if(_IsTextBlack) g.FillRectangle(Brushes.LightGoldenrodYellow, r);
			//if(f == _currentFile) {
			//	r.Width--; r.Height--;
			//	g.DrawRectangle(SystemPens.ControlDark, r);
			//}
		}
	}

	static bool _IsTextBlack => (uint)SystemColors.WindowText.ToArgb() == 0xFF000000; //if not high-contrast theme

	#endregion

	#region ITreeModel

	IEnumerable ITreeModel.GetChildren(object nodeTag)
	{
		if(nodeTag == null) return Root.Children();
		var f = nodeTag as FileNode;
		return f.Children();
	}

	bool ITreeModel.IsLeaf(object nodeTag)
	{
		var f = nodeTag as FileNode;
		return !f.IsFolder;
	}

	public event EventHandler<TreeModelEventArgs> NodesChanged;
	public event EventHandler<TreeModelEventArgs> NodesInserted;
	public event EventHandler<TreeModelEventArgs> NodesRemoved;
	public event EventHandler<TreePathEventArgs> StructureChanged;

	/// <summary>
	/// Call this to update control row view when need to change row height.
	/// To just redraw without changing height use f.UpdateControlRow instead, it's faster.
	/// </summary>
	internal void OnNodeChanged(FileNode f)
	{
		NodesChanged?.Invoke(this, _TreeModelEventArgs(f));
	}

	internal void OnNodeRemoved(FileNode f)
	{
		NodesRemoved?.Invoke(this, _TreeModelEventArgs(f));
	}

	internal void OnNodeInserted(FileNode f)
	{
		NodesInserted?.Invoke(this, _TreeModelEventArgs(f));
	}

	internal void OnStructureChanged()
	{
		StructureChanged?.Invoke(this, new TreePathEventArgs(TreePath.Empty));
	}

	TreeModelEventArgs _TreeModelEventArgs(FileNode node)
	{
		return new TreeModelEventArgs(node.Parent.TreePath, new int[] { node.Index }, new object[] { node });
	}

	#endregion

	#region Au.Compiler.IWorkspaceFiles

	public object IcfCompilerContext { get; set; }

	public string IcfFilesDirectory => FilesDirectory;

	public string IcfWorkspaceDirectory => WorkspaceDirectory;

	public Au.Compiler.IWorkspaceFile IcfFindById(uint id) => FindById(id);

	#endregion

	#region find, id

	/// <summary>
	/// Finds file or folder by name or @"\relative path" or id.
	/// </summary>
	/// <param name="name">
	/// Can be:
	/// Name like "name.cs".
	/// Relative path like @"\name.cs" or @"\subfolder\name.cs".
	/// &lt;id&gt; - enclosed <see cref="FileNode.IdString"/>, or <see cref="FileNode.IdStringWithWorkspace"/>.
	/// 
	/// Case-insensitive. If enclosed in &lt;&gt;, can be followed by any text.
	/// </param>
	/// <param name="folder">true - folder, false - file, null - any.</param>
	public FileNode Find(string name, bool? folder)
	{
		if(name != null && name.Length > 0 && name[0] == '<') return FindById(name.ToLong_(1));
		return Root.FindDescendant(name, folder);
	}

	/// <summary>
	/// Adds id/f to the dictionary that is used by <see cref="FindById"/> etc.
	/// If id is 0 or duplicate, generates new.
	/// Returns id or the generated id.
	/// </summary>
	public uint AddGetId(FileNode f, uint id = 0)
	{
		g1:
		if(id == 0) {
			//Normally we don't reuse ids of deleted items.
			//	Would be problems with something that we cannot/fail/forget to delete when deleting items.
			//	We save MaxId in XML: <files max-i="MaxId">.
			id = ++MaxId;
			if(id == 0) { //if new item created every 8 s, we have 1000 years, but anyway
				for(uint u = 1; u < uint.MaxValue; u++) if(!_idMap.ContainsKey(u)) { MaxId = u - 1; break; } //fast
				goto g1;
			} else if(_idMap.ContainsKey(id)) { //damaged XML file, or maybe a bug?
				Debug_.Print("id already exists:" + id);
				MaxId = _idMap.Keys.Max();
				id = 0;
				goto g1;
			}
			Save?.WorkspaceLater(); //null when importing this workspace
		}
		try { _idMap.Add(id, f); }
		catch(ArgumentException) {
			PrintWarning($"Duplicate id of '{f.Name}'. Creating new.");
			id = 0;
			goto g1;
		}
		return id;
	}

	/// <summary>
	/// Current largest id, used to generate new id.
	/// The root FileNode's ctor reads it from XML attribute 'max-i' and sets this property.
	/// </summary>
	public uint MaxId { get; set; }

	/// <summary>
	/// Finds file or folder by its <see cref="FileNode.Id"/>.
	/// Returns null if id is 0 or not found.
	/// id can contain <see cref="WorkspaceSN"/> in high-order int.
	/// </summary>
	public FileNode FindById(long id)
	{
		int idc = (int)(id >> 32); if(idc != 0 && idc != WorkspaceSN) return null;
		uint idf = (uint)id;
		if(idf == 0) return null;
		if(_idMap.TryGetValue(idf, out var f)) {
			Debug_.PrintIf(f == null, "deleted: " + idf);
			return f;
		}
		Debug_.Print("id not found: " + idf);
		return null;
	}

	/// <summary>
	/// Finds file or folder by its <see cref="FileNode.IdString"/>.
	/// Note: it must not be as returned by <see cref="FileNode.IdStringWithWorkspace"/>.
	/// </summary>
	public FileNode FindById(string id) => FindById(id.ToLong_());

	/// <summary>
	/// Finds file or folder by its file path (<see cref="FileNode.FilePath"/>).
	/// </summary>
	/// <param name="path">Full path of a file in this workspace or of a linked external file.</param>
	public FileNode FindByFilePath(string path)
	{
		var d = FilesDirectory;
		if(path.Length > d.Length && path.StartsWithI_(d) && path[d.Length] == '\\') //is in workspace folder
			return Root.FindDescendant(path.Substring(d.Length), null);
		foreach(var f in Root.Descendants()) if(f.IsLink && path.EqualsI_(f.LinkTarget)) return f;
		return null;
	}

	/// <summary>
	/// Finds all files (and not folders) that have the specified name.
	/// Returns empty array if not found.
	/// </summary>
	/// <param name="name">File name. If starts with backslash, works like <see cref="Find"/>. Does not support <see cref="FileNode.IdStringWithWorkspace"/> string.</param>
	public FileNode[] FindAll(string name)
	{
		return Root.FindAllDescendantFiles(name);
	}

	#endregion

	#region click, open/close, select, current, selected

	void _InitClickSelect()
	{
		_control.NodeMouseClick += _TV_NodeMouseClick;
		_control.KeyDown += _TV_KeyDown;
		_control.Expanded += _TV_Expanded;
		_control.Collapsed += _TV_Expanded;
	}

	void _UninitClickSelect()
	{
		_control.NodeMouseClick -= _TV_NodeMouseClick;
		_control.KeyDown -= _TV_KeyDown;
		_control.Expanded -= _TV_Expanded;
		_control.Collapsed -= _TV_Expanded;
	}

	private void _TV_NodeMouseClick(object sender, TreeNodeAdvMouseEventArgs e)
	{
		//Print(e.Button, e.ModifierKeys);
		if(e.ModifierKeys != 0) return;
		var f = e.Node.Tag as FileNode;
		switch(e.Button) {
		case MouseButtons.Left:
			if(_currentFile != f) _SetCurrentFile(f);
			break;
		case MouseButtons.Right:
			_control.BeginInvoke(new Action(() => _ItemRightClicked(f)));
			break;
		case MouseButtons.Middle:
			CloseFile(f, true);
			break;
		}
	}

	/// <summary>
	/// Returns true if f is null or isn't in this workspace or is deleted.
	/// </summary>
	public bool IsAlien(FileNode f) => f?.Model != this || f.IsDeleted;

	/// <summary>
	/// Closes f if open.
	/// Saves text if need, removes from OpenItems, deselects in treeview.
	/// </summary>
	/// <param name="f">Can be any item or null. Does nothing if it is null, folder or not open.</param>
	/// <param name="activateOther">When closing current file, if there are more open files, activate another open file.</param>
	public bool CloseFile(FileNode f, bool activateOther)
	{
		if(IsAlien(f)) return false;
		var of = OpenFiles;
		if(!of.Remove(f)) return false;

		Panels.Editor.Close(f);
		SelectDeselectItem(f, false);

		if(f == _currentFile) {
			if(activateOther && of.Count > 0 && _SetCurrentFile(of[0])) return true; //and don't select
			_currentFile = null;
			MainForm.SetTitle();
		}
		f.UpdateControlRow();

		Panels.Open.UpdateList();
		Panels.Open.UpdateCurrent(_currentFile);
		Save.StateLater();

		return true;
	}

	/// <summary>
	/// Closes specified files that are open.
	/// </summary>
	/// <param name="files">Any IEnumerable except OpenFiles.</param>
	public void CloseFiles(IEnumerable<FileNode> files)
	{
		if(files == OpenFiles) files = OpenFiles.ToArray();
		bool closeCurrent = false;
		foreach(var f in files) if(f == _currentFile) closeCurrent = true; else CloseFile(f, false);
		if(closeCurrent) CloseFile(_currentFile, true);
	}

	/// <summary>
	/// Called by <see cref="PanelFiles.LoadWorkspace"/> before opening another workspace and disposing this.
	/// Saves all, closes documents, sets _currentFile = null.
	/// </summary>
	public void UnloadingWorkspace()
	{
		Save.AllNowIfNeed();
		_currentFile = null;
		Panels.Editor.CloseAll(saveTextIfNeed: false);
		OpenFiles.Clear();
		Panels.Open.UpdateList();
		MainForm.SetTitle();
	}

	/// <summary>
	/// Gets the current file. It is open/active in the code editor.
	/// </summary>
	public FileNode CurrentFile => _currentFile;
	FileNode _currentFile;

	/// <summary>
	/// Selects the node and opens its file in the code editor.
	/// Returns false if failed to select, for example if f is a folder.
	/// </summary>
	/// <param name="f"></param>
	/// <param name="doNotChangeSelection"></param>
	public bool SetCurrentFile(FileNode f, bool doNotChangeSelection = false)
	{
		if(IsAlien(f)) return false;
		if(!doNotChangeSelection) f.SelectSingle();
		if(_currentFile != f) _SetCurrentFile(f);
		return _currentFile == f;
	}

	/// <summary>
	/// If f!=_currentFile and not folder:
	///		Opens it in editor, adds to OpenFiles, sets _currentFile, saves state later, updates UI.
	///		Saves and hides current document.
	///	Returns false if fails to read file or if f is folder.
	/// </summary>
	/// <param name="f"></param>
	bool _SetCurrentFile(FileNode f)
	{
		Debug.Assert(!IsAlien(f));
		if(f == _currentFile) return true;
		//Print(f);
		if(f.IsFolder) return false;

		if(_currentFile != null) Save.TextNowIfNeed();

		var fPrev = _currentFile;
		_currentFile = f;

		if(!Panels.Editor.Open(f)) {
			_currentFile = fPrev;
			if(OpenFiles.Contains(f)) Panels.Open.UpdateCurrent(_currentFile);
			return false;
		}

		fPrev?.UpdateControlRow();
		_currentFile?.UpdateControlRow();

		var of = OpenFiles;
		of.Remove(f);
		of.Insert(0, f);
		Panels.Open.UpdateList();
		Panels.Open.UpdateCurrent(f);
		Save.StateLater();

		MainForm.SetTitle();

		return true;
	}

	public FileNode[] SelectedItems => _control.SelectedNodes.Select(tn => tn.Tag as FileNode).ToArray();

	/// <summary>
	/// Selects or deselects item in treeview. Does not set current file etc. Does not deselect others.
	/// </summary>
	/// <param name="f"></param>
	/// <param name="select"></param>
	public void SelectDeselectItem(FileNode f, bool select)
	{
		if(IsAlien(f)) return;
		f.IsSelected = select;
	}

	void _ItemRightClicked(FileNode f)
	{
		if(IsAlien(f)) return;
		var m = Strips.ddFile;

		ToolStripDropDownClosedEventHandler onClosed = null;
		onClosed = (sender, e) =>
		  {
			  (sender as ToolStripDropDownMenu).Closed -= onClosed;
			  _msgLoop.Stop();
		  };
		m.Closed += onClosed;

		_inContextMenu = true;
		try {
			m.ShowAsContextMenu_();
			_msgLoop.Loop();
			if(_control == null) return; //loaded another workspace
			if(f != _currentFile && _control.SelectedNodes.Count < 2) {
				if(_currentFile == null) _control.ClearSelection();
				//else if(_control.SelectedNode == f.TreeNodeAdv) _currentFile.SelectSingle(); //no. Breaks renaming, etc. We'll do it on editor focused.
				//else the action selected another file or folder
			}
		}
		finally { _inContextMenu = false; }
	}
	Au.Util.MessageLoop _msgLoop = new Au.Util.MessageLoop();
	bool _inContextMenu;

	public void OnEditorFocused()
	{
		if(_currentFile != null && _control.SelectedNode?.Tag != _currentFile && _control.SelectedNodes.Count < 2) _currentFile.SelectSingle();
	}

	private void _TV_Expanded(object sender, TreeViewAdvEventArgs e)
	{
		if(e.Node.Level == 0) return;
		Save.StateLater();
	}

	#endregion

	#region hotkeys, new, delete, open/close (menu commands), cut/copy/paste

	private void _TV_KeyDown(object sender, KeyEventArgs e)
	{
		switch(e.KeyData) {
		case Keys.Enter: OpenSelected(1); break;
		case Keys.Delete: DeleteSelected(); break;
		case Keys.Control | Keys.X: CutCopySelected(true); break;
		case Keys.Control | Keys.C: CutCopySelected(false); break;
		case Keys.Control | Keys.V: Paste(); break;
		}
	}

	/// <summary>
	/// Gets the place where item should be added in operations such as new, paste, import.
	/// </summary>
	FileNode _GetInsertPos(out NodePosition pos, bool askIntoFolder = false)
	{
		//CONSIDER: use _inContextMenu here. If it is false, and askIntoFolder, show dialog with 2 or 3 options: top, above selected, in selected folder.

		FileNode r;
		var c = _control.CurrentNode;
		if(c == null) { //empty treeview?
			r = Root;
			pos = NodePosition.Inside;
		} else {
			r = c.Tag as FileNode;
			if(askIntoFolder && r.IsFolder && c.IsSelected && _control.SelectedNodes.Count == 1 && AuDialog.ShowYesNo("Into the folder?", owner: _control)) pos = NodePosition.Inside;
			else if(r.Next == null) pos = NodePosition.After; //usually we want to add after the last, not before
			else pos = NodePosition.Before;
		}
		return r;
	}

	public FileNode NewItem(string template, string name = null, bool beginEdit = false)
	{
		var pos = NodePosition.Inside;
		var target = _inContextMenu ? _GetInsertPos(out pos) : null;
		return NewItem(target, pos, template, name, beginEdit);
	}

	/// <inheritdoc cref="FileNode.NewItem"/>
	public FileNode NewItem(FileNode target, NodePosition pos, string template, string name = null, bool beginEdit = false)
	{
		var f = FileNode.NewItem(this, target, pos, template, name);
		if(f == null) return null;
		if(f.IsFolder) {
			if(f.IsProjectFolder(out var main) && main != null) SetCurrentFile(f = main);
			else f.SelectSingle();
		} else SetCurrentFile(f);
		if(beginEdit && f.IsSelected) RenameSelected();
		return f;
	}

	public void RenameSelected()
	{
		//if(_control.SelectedNodes.Count != 1) return; //let edit current node, like F2 does
		(_control.NodeControls[1] as NodeTextBox).BeginEdit();
	}

	public void DeleteSelected()
	{
		var a = SelectedItems; if(a.Length < 1) return;

		//confirmation
		var text = string.Join("\n", a.Select(f => f.Name));
		var expandedText = "The file will be deleted, unless it is external.\r\nWill use Recycle Bin, if possible.";
		var r = AuDialog.ShowEx("Deleting", text, "1 OK|0 Cancel", owner: _control, checkBox: "Don't delete file", expandedText: expandedText);
		if(r.Button == 0) return;

		foreach(var f in a) {
			_Delete(f, doNotDeleteFile: r.IsChecked); //info: and saves everything, now and/or later
		}
	}

	bool _Delete(FileNode f, bool doNotDeleteFile = false, bool tryRecycleBin = true, bool canDeleteLinkTarget = false)
	{
		var e = f.Descendants(true);

		CloseFiles(e);

		if(!doNotDeleteFile && (canDeleteLinkTarget || !f.IsLink)) {
			try { File_.Delete(f.FilePath, tryRecycleBin); } //FUTURE: use other thread, because very slow. Or better move to folder 'deleted'.
			catch(Exception ex) { Print(ex.Message); return false; }
		} else {
			Print($"<>File not deleted: <explore>{f.FilePath}<>");
		}

		foreach(var k in e) {
			if(_myClipboard.Contains(k)) _myClipboard.Clear();
			try { DB?.Execute("DELETE FROM _editor WHERE id=?", k.Id); } catch(SLException ex) { Debug_.Print(ex); }
			Au.Compiler.Compiler.OnFileDeleted(this, k);
			_idMap[k.Id] = null;
			k.IsDeleted = true;
		}

		OnNodeRemoved(f);
		f.Remove();
		//FUTURE: call event to update other controls.

		Save.WorkspaceLater();
		return true;
	}

	public void CutCopySelected(bool cut)
	{
		if(!_myClipboard.Set(SelectedItems, cut)) return;
		//var d = new DataObject(string.Join("\r\n", _cutCopyNodes.Select(f => f.Name)));
		//Clipboard.SetDataObject(d);
		Clipboard.SetText(string.Join("\r\n", _myClipboard.nodes.Select(f => f.Name)));
	}

	struct _MyClipboard
	{
		public FileNode[] nodes;
		public bool cut;

		public bool Set(FileNode[] nodes, bool cut)
		{
			if(nodes == null || nodes.Length == 0) { Clear(); return false; }
			this.nodes = nodes;
			this.cut = cut;
			nodes[0].TreeControl.Invalidate(); //draw "cut" icon
			return true;
		}

		public void Clear()
		{
			if(nodes == null) return;
			if(nodes.Length > 0) nodes[0].TreeControl.Invalidate(); //was "cut" icon
			nodes = null;
		}

		public bool IsEmpty { get => nodes == null; }

		public bool Contains(FileNode f) { return !IsEmpty && nodes.Contains(f); }
	}
	_MyClipboard _myClipboard;

	public void Paste()
	{
		if(_myClipboard.IsEmpty) return;
		var target = _GetInsertPos(out var pos, true);
		_MultiCopyMove(!_myClipboard.cut, _myClipboard.nodes, target, pos);
		_myClipboard.Clear();
	}

	public void SelectedCopyPath(bool full)
	{
		var a = SelectedItems; if(a.Length == 0) return;
		Clipboard.SetText(string.Join("\r\n", a.Select(f => full ? f.FilePath : f.ItemPath)));
	}

	/// <summary>
	/// Opens the selected item(s) in our editor or in default app or selects in Explorer.
	/// </summary>
	/// <param name="how">1 open, 2 open in new window (not impl), 3 open in default app, 4 select in Explorer.</param>
	public void OpenSelected(int how)
	{
		var a = SelectedItems; if(a.Length == 0) return;
		foreach(var f in a) {
			switch(how) {
			case 1:
				if(f.IsFolder) f.TreeNodeAdv.Expand();
				else SetCurrentFile(f);
				break;
			//case 2:
			//	if(f.IsFolder) continue;
			//	//FUTURE
			//	break;
			case 3:
				if(f.IcfIsScript) goto case 1; //CONSIDER: maybe use .csx extension, then can open in Visual Studio. Now even if we find VS path and open, we have two problems: opens new VS process; no colors/intellisense.
				Shell.Run(f.FilePath);
				break;
			case 4:
				Shell.SelectFileInExplorer(f.FilePath);
				break;
			}
		}
	}

	/// <summary>
	/// Closes selected or all items, or collapses folders.
	/// Used to implement menu File -> Open/Close.
	/// </summary>
	/// <param name="how">1 close selected file(s) and current file, 2 close all, 3 collapse folders.</param>
	/// <remarks>
	/// When how is 1: Closes selected files. If there are no selected files, closes current file. Does not collapse selected folders.
	/// When how is 2: Closes all files and collapses folders.
	/// </remarks>
	public void CloseEtc(int how)
	{
		switch(how) {
		case 1:
			var a = SelectedItems;
			if(a.Length > 0) CloseFiles(a);
			else CloseFile(_currentFile, true);
			break;
		case 2:
			CloseFiles(OpenFiles);
			_control.CollapseAll();
			break;
		case 3:
			_control.CollapseAll();
			break;
		}
	}

	#endregion

	#region drag-drop

	void _InitDragDrop()
	{
		_control.ItemDrag += _TV_ItemDrag;
		_control.DragOver += _TV_DragOver;
		_control.DragDrop += _TV_DragDrop;
	}

	void _UninitDragDrop()
	{
		_control.ItemDrag -= _TV_ItemDrag;
		_control.DragOver -= _TV_DragOver;
		_control.DragDrop -= _TV_DragDrop;
	}

	private void _TV_ItemDrag(object sender, ItemDragEventArgs e)
	{
		if(e.Button != MouseButtons.Left) return;
		_control.DoDragDropSelectedNodes(DragDropEffects.Move | DragDropEffects.Copy);
	}

	private void _TV_DragOver(object sender, DragEventArgs e)
	{
		e.Effect = DragDropEffects.None;
		var effect = e.AllowedEffect;
		bool copy = (e.KeyState & 8) != 0;
		if(copy) effect &= ~(DragDropEffects.Link | DragDropEffects.Move);
		if(0 == (effect & (DragDropEffects.Link | DragDropEffects.Move | DragDropEffects.Copy))) return;

		var nTarget = _control.DropPosition.Node; if(nTarget == null) return;

		//can drop TreeNodeAdv and files
		TreeNodeAdv[] nodes = null;
		if(e.Data.GetDataPresent(typeof(TreeNodeAdv[]))) {
			nodes = e.Data.GetData(typeof(TreeNodeAdv[])) as TreeNodeAdv[];
			if(nodes?[0].Tree != _control) return;
			if(!copy) effect &= ~DragDropEffects.Copy;
		} else if(e.Data.GetDataPresent(DataFormats.FileDrop)) {
		} else return;

		var fTarget = nTarget.Tag as FileNode;
		bool isFolder = fTarget.IsFolder;
		bool isInside = _control.DropPosition.Position == NodePosition.Inside;

		//prevent selecting whole non-folder item. Make either above or below.
		if(isFolder) _control.DragDropBottomEdgeSensivity = _control.DragDropTopEdgeSensivity = 0.3f; //default
		else _control.DragDropBottomEdgeSensivity = _control.DragDropTopEdgeSensivity = 0.51f;

		//can drop here?
		if(!copy && nodes != null) {
			foreach(TreeNodeAdv n in nodes) {
				var f = n.Tag as FileNode;
				if(!f.CanMove(fTarget, _control.DropPosition.Position)) return;
			}
		}

		//expand-collapse folder on right-click. However this does not work when dragging files, because Explorer then ends the drag-drop.
		if(isFolder && isInside) {
			var ks = e.KeyState & 3;
			if(ks == 3 && _dragKeyStateForFolderExpand != 3) {
				if(nTarget.IsExpanded) nTarget.Collapse(); else nTarget.Expand();
			}
			_dragKeyStateForFolderExpand = ks;
		}

		e.Effect = effect;
	}

	int _dragKeyStateForFolderExpand;

	private void _TV_DragDrop(object sender, DragEventArgs e)
	{
		bool copy = (e.KeyState & 8) != 0;
		var pos = _control.DropPosition.Position;
		var target = _control.DropPosition.Node.Tag as FileNode;
		if(e.Data.GetDataPresent(typeof(TreeNodeAdv[]))) {
			var a = (e.Data.GetData(typeof(TreeNodeAdv[])) as TreeNodeAdv[]).Select(tn => tn.Tag as FileNode).ToArray();
			_MultiCopyMove(copy, a, target, pos);
		} else if(e.Data.GetDataPresent(DataFormats.FileDrop)) {
			var a = (string[])e.Data.GetData(DataFormats.FileDrop);
			if(a.Length == 1 && IsWorkspaceDirectory(a[0])) {
				switch(AuDialog.ShowEx("Workspace", a[0],
					"1 Open workspace|2 Import workspace|0 Cancel",
					flags: DFlags.Wider, footerText: GetSecurityInfo(true))) {
				case 1: Timer_.After(1, () => Panels.Files.LoadWorkspace(a[0])); break;
				case 2: ImportWorkspace(a[0], target, pos); break;
				}
				return;
			}
			_ImportFiles(copy, a, target, pos);
		}
	}

	#endregion

	#region import, move, copy

	/// <summary>
	/// Imports one or more files into the workspace.
	/// </summary>
	/// <param name="a">Files. If null, shows dialog to select files.</param>
	public void ImportFiles(string[] a = null)
	{
		if(a == null) {
			Print("Info: To import files, you can also drag and drop from a folder window.");
			var d = new OpenFileDialog();
			d.Multiselect = true;
			d.Title = "Import files to the workspace";
			if(d.ShowDialog(MainForm) != DialogResult.OK) return;
			a = d.FileNames;
		}

		var target = _GetInsertPos(out var pos);
		_ImportFiles(false, a, target, pos);
	}

	/// <summary>
	/// Imports another workspace into this workspace.
	/// </summary>
	/// <param name="wsDir">Workspace directory. If null, shows dialog to select.</param>
	/// <param name="target">If null, calls _GetInsertPos.</param>
	/// <param name="pos">Used when target is not null.</param>
	public void ImportWorkspace(string wsDir = null, FileNode target = null, NodePosition pos = 0)
	{
		string xmlFile;
		if(wsDir != null) xmlFile = wsDir + @"\files.xml";
		else {
			var d = new OpenFileDialog() { Title = "Import workspace", Filter = "files.xml|files.xml" };
			if(d.ShowDialog(MainForm) != DialogResult.OK) return;
			wsDir = Path_.GetDirectoryPath(xmlFile = d.FileName);
		}

		try {
			//create new folder for workspace's items
			if(target == null) target = _GetInsertPos(out pos);
			target = FileNode.NewItem(this, target, pos, "Folder", Path_.GetFileName(wsDir));
			if(target == null) return;

			var m = new FilesModel(null, xmlFile);
			var a = m.Root.Children().ToArray();
			_MultiCopyMove(true, a, target, NodePosition.Inside, true);
			m.Dispose(); //currently does nothing

			target.SelectSingle();

			Print($"Info: Imported workspace '{wsDir}' to folder '{target.Name}'.\r\n\t{GetSecurityInfo()}");
		}
		catch(Exception ex) { Print(ex.Message); }
	}

	void _MultiCopyMove(bool copy, FileNode[] a, FileNode target, NodePosition pos, bool importingWorkspace = false)
	{
		_control.ClearSelection();
		_control.BeginUpdate();
		try {
			bool movedCurrentFile = false;
			var a2 = new List<FileNode>(a.Length);
			foreach(var f in (pos == NodePosition.After) ? a.Reverse() : a) {
				if(!importingWorkspace && !this.IsMyFileNode(f)) continue; //deleted?
				if(copy) {
					var fCopied = f.FileCopy(target, pos, this);
					if(fCopied != null) a2.Add(fCopied);
				} else {
					if(!f.FileMove(target, pos)) continue;
					a2.Add(f);
					if(!movedCurrentFile && _currentFile != null) {
						if(f == _currentFile || (f.IsFolder && _currentFile.IsDescendantOf(f))) movedCurrentFile = true;
					}
				}
			}
			if(movedCurrentFile) _control.EnsureVisible(_currentFile.TreeNodeAdv);
			if(pos != NodePosition.Inside || target.TreeNodeAdv.IsExpanded) {
				foreach(var f in a2) f.IsSelected = true;
			}
		}
		catch(Exception ex) { Print(ex.Message); }
		finally { _control.EndUpdate(); }

		//info: don't need to schedule saving here. FileCopy and FileMove did it.
	}

	void _ImportFiles(bool copy, string[] a, FileNode target, NodePosition pos)
	{
		bool fromWorkspaceDir = false, dirsDropped = false;
		for(int i = 0; i < a.Length; i++) {
			var s = a[i] = Path_.Normalize(a[i]);
			if(s.IndexOf_(@"\$RECYCLE.BIN\", true) > 0) {
				AuDialog.ShowEx("Files from Recycle Bin", $"At first restore the file to the <a href=\"{FilesDirectory}\">workspace folder</a> or other normal folder.",
					icon: DIcon.Info, owner: _control, onLinkClick: e => Shell.TryRun(e.LinkHref));
				return;
			}
			var fd = FilesDirectory;
			if(!fromWorkspaceDir) {
				if(s.StartsWithI_(fd) && (s.Length == fd.Length || s[fd.Length] == '\\')) fromWorkspaceDir = true;
				else if(!dirsDropped) dirsDropped = File_.ExistsAsDirectory(s);
			}
		}
		int r;
		if(copy) {
			if(fromWorkspaceDir) {
				AuDialog.ShowInfo("Files from workspace folder", "Ctrl not supported."); //not implemented
				return;
			}
			r = 2; //copy
		} else if(fromWorkspaceDir) {
			r = 3; //move
		} else {
			string ins1 = dirsDropped ? "\nFolders not supported." : null;
			r = AuDialog.ShowEx("Import files", string.Join("\n", a),
			$"1 Add as a link to the external file{ins1}|2 Copy to the workspace folder|3 Move to the workspace folder|0 Cancel",
			flags: DFlags.CommandLinks | DFlags.Wider, owner: _control, footerText: GetSecurityInfo(true));
			if(r == 0) return;
		}

		var newParent = (pos == NodePosition.Inside) ? target : target.Parent;
		bool select = pos != NodePosition.Inside || target.TreeNodeAdv.IsExpanded;
		if(select) _control.ClearSelection();
		_control.BeginUpdate();
		try {
			var newParentPath = newParent.FilePath;
			var (nf1, nd1) = _CountFilesFolders();

			foreach(var s in a) {
				bool isDir;
				var itIs = File_.ExistsAs2(s, true);
				if(itIs == FileDir2.File) isDir = false;
				else if(itIs == FileDir2.Directory && r != 1) isDir = true;
				else continue; //skip symlinks or if does not exist

				FileNode k;
				var name = Path_.GetFileName(s);
				if(r == 1) {
					k = new FileNode(this, name, false, s); //CONSIDER: unexpand
				} else {
					//var newPath = newParentPath + "\\" + name;
					if(fromWorkspaceDir) { //already exists?
						var relPath = s.Substring(FilesDirectory.Length);
						var fExists = this.Find(relPath, null);
						if(fExists != null) {
							fExists.FileMove(target, pos);
							continue;
						}
					}
					k = new FileNode(this, name, isDir);
					if(isDir) _AddDir(s, k);
					try {
						if(r == 2) File_.CopyTo(s, newParentPath, IfExists.Fail);
						else File_.MoveTo(s, newParentPath, IfExists.Fail);
					}
					catch(Exception ex) { Print(ex.Message); continue; }
				}
				target.AddChildOrSibling(k, pos, false);
				if(select) k.IsSelected = true;
			}

			var (nf2, nd2) = _CountFilesFolders();
			int nf = nf2 - nf1, nd = nd2 - nd1;
			if(nf + nd > 0) Print($"Info: Imported {nf} files and {nd} folders.\r\n\t{GetSecurityInfo()}");
		}
		catch(Exception ex) { Print(ex.Message); }
		finally { _control.EndUpdate(); }
		Save.WorkspaceLater();

		void _AddDir(string path, FileNode parent)
		{
			foreach(var u in File_.EnumDirectory(path, FEFlags.UseRawPath | FEFlags.SkipHiddenSystem)) {
				bool isDir = u.IsDirectory;
				var k = new FileNode(this, u.Name, isDir);
				parent.AddChild(k);
				if(isDir) _AddDir(u.FullPath, k);
			}
		}

		(int nf, int nd) _CountFilesFolders()
		{
			int nf = 0, nd = 0; foreach(var v in Root.Descendants()) if(v.IsFolder) nd++; else nf++;
			return (nf, nd);
		}
	}

	#endregion

	#region export

	/// <summary>
	/// Shows dialog to get path for new or exporting workspace.
	/// Returns workspace's directory path.
	/// Does not create any files/directories.
	/// </summary>
	/// <param name="name">Default name of the workspace.</param>
	/// <param name="location">Default parent directory of the main directory of the workspace.</param>
	public static string GetDirectoryPathForNewWorkspace(string name = null, string location = null)
	{
		var f = new _FormNewWorkspace();
		f.textName.Text = name;
		f.textLocation.Text = location ?? Folders.ThisAppDocuments;
		if(f.ShowDialog() != DialogResult.OK) return null;
		return f.textPath.Text;
	}

	public bool ExportSelected(string location = null)
	{
		var a = SelectedItems; if(a.Length < 1) return false;

		string name = a[0].Name; if(!a[0].IsFolder) name = Path_.GetFileNameWithoutExtension(name);

		if(a.Length == 1 && a[0].IsFolder && a[0].HasChildren) a = a[0].Children().ToArray();

		var wsDir = GetDirectoryPathForNewWorkspace(name, location); if(wsDir == null) return false;
		string filesDir = wsDir + @"\files";
		try {
			File_.CreateDirectory(filesDir);
			foreach(var f in a) {
				if(!f.IsLink) File_.CopyTo(f.FilePath, filesDir);
			}
			FileNode.Export(a, wsDir + @"\files.xml");
		}
		catch(Exception ex) {
			Print(ex);
			return false;
		}

		Shell.SelectFileInExplorer(wsDir);
		return true;
	}

	#endregion

	#region watch folder

	FileSystemWatcher _watcher;

	void _InitWatcher()
	{
		_watcher = new FileSystemWatcher(FilesDirectory);
		_watcher.IncludeSubdirectories = true;
		_watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;
		_watcher.Changed += _watcher_Changed;
		_watcher.Created += _watcher_Created;
		_watcher.Deleted += _watcher_Deleted;
		_watcher.Renamed += _watcher_Renamed;
		//_watcher.EnableRaisingEvents = true; //FUTURE
	}

	void _UninitWatcher()
	{
		//_watcher.EnableRaisingEvents = false;
		//_watcher.Changed -= _watcher_Changed;
		//_watcher.Created -= _watcher_Created;
		//_watcher.Deleted -= _watcher_Deleted;
		//_watcher.Renamed -= _watcher_Renamed;
		_watcher.Dispose(); //disables raising events and sets all events = null
	}

	private void _watcher_Renamed(object sender, RenamedEventArgs e)
	{
		Print(e.ChangeType, e.OldName, e.Name, e.OldFullPath, e.FullPath);
	}

	private void _watcher_Deleted(object sender, FileSystemEventArgs e)
	{
		Print(e.ChangeType, e.Name, e.FullPath);
	}

	private void _watcher_Created(object sender, FileSystemEventArgs e)
	{
		Print(e.ChangeType, e.Name, e.FullPath);
	}

	private void _watcher_Changed(object sender, FileSystemEventArgs e)
	{
		Print(e.ChangeType, e.Name, e.FullPath);
	}

	#endregion

	#region util

	/// <summary>
	/// Returns true if FileNode f is not null and belongs to this FilesModel and is not deleted.
	/// </summary>
	public bool IsMyFileNode(FileNode f) { return Root.IsAncestorOf(f); }

	/// <summary>
	/// Returns true if s is path of a workspace directory.
	/// </summary>
	public static bool IsWorkspaceDirectory(string s)
	{
		string xmlFile = s + @"\files.xml";
		if(File_.ExistsAsFile(xmlFile) && File_.ExistsAsDirectory(s + @"\files")) {
			try { return XElement_.Load(xmlFile).Name == "files"; } catch { }
		}
		return false;
	}

	/// <summary>
	/// Security info string.
	/// </summary>
	public static string GetSecurityInfo(bool withShieldIcon = false)
	{
		var s = "Security info: Unknown files can contain malicious code - virus, spyware, etc. It is safe to import, open and edit files if you don't run and don't compile them. Triggers are inactive until run/compile.";
		return withShieldIcon ? ("v|" + s) : s;
	}

	#endregion
}
