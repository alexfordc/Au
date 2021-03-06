//Code colors and folding.

//#if TRACE
//#define PRINT
//#endif

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
//using System.Windows.Forms;
//using System.Drawing;
//using System.Linq;

using Au;
using Au.Types;
using static Au.Controls.Sci;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Shared.Extensions;

partial class CiStyling
{
	/// <summary>
	/// Called when opening a document, when handle created but text still not loaded.
	/// </summary>
	public static void DocHandleCreated(SciCode doc)
	{
		Program.Settings.edit_styles.ToScintilla(doc);

		doc.Call(SCI_MARKERDEFINE, SciCode.c_markerUnderline, SC_MARK_UNDERLINE);
		doc.Call(SCI_MARKERSETBACK, SciCode.c_markerUnderline, 0xe0e0e0);

		_InitFolding(doc);
	}

	/// <summary>
	/// Called after setting editor control text when a document opened (not just switched active document).
	/// </summary>
	public static void DocTextAdded(SciCode doc, bool newFile) => CodeInfo._styling._DocTextAdded(doc, newFile);
	void _DocTextAdded(SciCode doc, bool newFile)
	{
		doc.ZFoldScriptHeader(setCaret: newFile);

		if(CodeInfo.IsReadyForStyling) {
			doc.BeginInvoke(new Action(() => _DocChanged(doc, true)));
		} else { //at program startup
			CodeInfo.ReadyForStyling += () => { if(doc.IsHandleCreated) _DocChanged(doc, true); };
		}
	}

	/// <summary>
	/// Sets timer to updates styling and folding from 0 to the end of the visible area.
	/// </summary>
	public void Update() => _update = true;

	SciCode _doc; //to detect when the active document changed
	bool _update;
	Range _visibleLines;
	ATimer _modTimer;
	int _modFromEnd; //like _endStyling (SCI_GETENDSTYLED), but from end
	int _diagCounter;
	CancellationTokenSource _cancelTS;

	void _DocChanged(SciCode doc, bool opened)
	{
		_doc = doc;
		_update = false;
		_visibleLines = default;
		_modTimer?.Stop();
		_modFromEnd = int.MaxValue;
		_diagCounter = 0;
		_cancelTS?.Cancel(); _cancelTS = null;
		if(opened) {
			_StylingAndFoldingVisibleFrom0(doc, firstTime: true);
		}
	}

	/// <summary>
	/// Called every 250 ms while editor is visible.
	/// </summary>
	public void Timer250msWhenVisibleAndWarm(SciCode doc)
	{
		//We use SCLEX_NULL. If SCLEX_CONTANER, Scintilla sends too many notifications, particularly if folding used too.
		//To detect when need styling and folding we use 'opened' and 'modified' events and 250 ms timer.
		//When modified, we do styling for the modified line(s). It is faster but unreliable, eg does not update new/deleted identifiers.
		//The timer does styling and folding for all visible lines. It is slower but updates everything after modified, scrolled, resized, folded, etc.
		//When opened, we do styling for all visible lines; folding for all lines, because may need to restore saved contracted fold points.

		if(_cancelTS != null || (_modTimer?.IsRunning ?? false)) return;
		if(doc != _doc || _update) {
			if(doc != _doc) _DocChanged(doc, false); else _update = false;
			_StylingAndFoldingVisibleFrom0(doc);
		} else {
			Sci_GetStylingInfo(doc.SciPtr, 8 | 4, out var si); //fast
			if(si.visibleFromLine < _visibleLines.Start.Value || si.visibleToLine > _visibleLines.End.Value) {
				_StylingAndFolding(doc); //all visible
			} else if(_diagCounter > 0 && --_diagCounter == 0) {
				CodeInfo._diag.Indicators(doc.Pos16(si.visibleFrom), doc.Pos16(si.visibleTo));
			}
		}
	}

	/// <summary>
	/// Called when editor text modified.
	/// </summary>
	public void SciModified(SciCode doc, in SCNotification n)
	{
		//Delay to avoid multiple styling/folding/canceling on multistep actions (replace text range, find/replace all, autocorrection) and fast automated text input.
		_cancelTS?.Cancel(); _cancelTS = null;
		_modFromEnd = Math.Min(_modFromEnd, doc.Len8 - n.FinalPosition);
		_modTimer ??= new ATimer(_Modified);
		if(!_modTimer.IsRunning) _modTimer.After(25, doc);
	}

	void _Modified(ATimer t)
	{
		//var p1=APerf.Create();
		var doc = t.Tag as SciCode;
		if(doc != Panels.Editor.ZActiveDoc) return;
		if(_cancelTS != null) return;
		_StylingAndFolding(doc, false, doc.Z.LineEndFromPos(false, doc.Len8 - _modFromEnd, withRN: true));
		//p1.NW('a'); //we return without waiting for the async task to complete
	}

	void _StylingAndFoldingVisibleFrom0(SciCode doc, bool firstTime = false)
	{
		_cancelTS?.Cancel(); _cancelTS = null;
		if(!firstTime) doc.Call(SCI_STARTSTYLING); //set the Scintilla's SCI_GETENDSTYLED field = 0
		_StylingAndFolding(doc, true, -1, firstTime);
	}

	async void _StylingAndFolding(SciCode doc, bool fromStart = false, int end8 = -1, bool firstTime = false)
	{
#if PRINT
		var p1 = APerf.Create();
#endif
		Sci_GetStylingInfo(doc.SciPtr, 2 | 4 | 8, out var si);

		int start8;
		bool minimal = end8 >= 0 && !fromStart;
		if(minimal) {
			start8 = si.endStyledLineStart;
			end8 = Math.Min(end8, si.visibleTo);
		} else {
			start8 = fromStart ? 0 : Math.Min(si.visibleFrom, si.endStyledLineStart);
			end8 = si.visibleTo;
		}
		if(end8 == si.visibleTo) _modFromEnd = doc.Len8 - end8;
		if(end8 <= start8) return;

		var cd = new CodeInfo.Context(0);
		if(!cd.GetDocument()) return;
#if PRINT
		p1.Next('d');
		AOutput.Write($"<><c green>style needed: {start8}-{end8}, lines {doc.Z.LineFromPos(false, start8) + 1}-{doc.Z.LineFromPos(false, end8)}<>");
#endif
		int start16 = doc.Pos16(start8), end16 = doc.Pos16(end8);

		Debug.Assert(_cancelTS == null);
		_cancelTS = new CancellationTokenSource();
		var cancelTS = _cancelTS;
		var cancelToken = cancelTS.Token;

		var document = cd.document;
		SemanticModel semo = null;
		IEnumerable<ClassifiedSpan> a = null;
		try {
			await Task.Run(async () => {
				semo = await document.GetSemanticModelAsync(cancelToken).ConfigureAwait(false);
#if PRINT
				p1.Next('m');
#endif
				a = Classifier.GetClassifiedSpans(semo, TextSpan.FromBounds(start16, end16), document.Project.Solution.Workspace, cancelToken);
				//info: GetClassifiedSpansAsync calls GetSemanticModelAsync and GetClassifiedSpans, like here.
				//GetSemanticModelAsync+GetClassifiedSpans are slow, about 90% of total time.
				//Tried to implement own "GetClassifiedSpans", but slow too, often slower, because GetSymbolInfo is slow.
			});
		}
		catch(OperationCanceledException) { }
		finally {
			cancelTS.Dispose();
			if(cancelTS == _cancelTS) _cancelTS = null;
		}
		if(cancelToken.IsCancellationRequested) {
#if PRINT
			p1.Next();
			AOutput.Write($"<><c orange>canceled.  {p1.ToString()}<>");
#endif
			return;
		}
		if(doc != Panels.Editor.ZActiveDoc) {
#if PRINT
			AOutput.Write("<><c red>switched doc<>");
#endif
			return;
		}
#if PRINT
		p1.Next('c');
#endif

		//rejected. The 250 ms timer will fix it.
		//some spans are outside start16..end16, eg when in /**/ or in unclosed @" or #if
		//int startMin = start16, endMax = end16;
		//foreach(var v in a) {
		//	var ss = v.TextSpan.Start;
		//	if((object)v.ClassificationType == ClassificationTypeNames.ExcludedCode) ss -= 2; //move to the #if etc line
		//	startMin = Math.Min(startMin, ss);
		//	endMax = Math.Max(endMax, v.TextSpan.End);
		//}
		//if(startMin < start16) start16 = doc.Pos16(start8 = doc.Z.LineStartFromPos(false, doc.Pos8(startMin)));
		//if(endMax > end16) end16 = doc.Pos16(end8 = doc.Z.LineEndFromPos(false, doc.Pos8(endMax), withRN: true));
		////AOutput.Write(start8, end8, start16, end16);

		var b = new byte[end8 - start8];

		foreach(var v in a) {
			//AOutput.Write(v.ClassificationType, v.TextSpan);
			EToken style = v.ClassificationType switch
			{
				#region
				ClassificationTypeNames.ClassName => EToken.Type,
				ClassificationTypeNames.Comment => EToken.Comment,
				ClassificationTypeNames.ConstantName => EToken.Constant,
				ClassificationTypeNames.ControlKeyword => EToken.Keyword,
				ClassificationTypeNames.DelegateName => EToken.Type,
				ClassificationTypeNames.EnumMemberName => EToken.EnumMember,
				ClassificationTypeNames.EnumName => EToken.Type,
				ClassificationTypeNames.EventName => EToken.Function,
				ClassificationTypeNames.ExcludedCode => EToken.Excluded,
				ClassificationTypeNames.ExtensionMethodName => EToken.Function,
				ClassificationTypeNames.FieldName => EToken.Field,
				ClassificationTypeNames.Identifier => _TryResolveMethod(),
				ClassificationTypeNames.InterfaceName => EToken.Type,
				ClassificationTypeNames.Keyword => EToken.Keyword,
				ClassificationTypeNames.LabelName => EToken.Label,
				ClassificationTypeNames.LocalName => EToken.LocalVar,
				ClassificationTypeNames.MethodName => EToken.Function,
				ClassificationTypeNames.NamespaceName => EToken.Namespace,
				ClassificationTypeNames.NumericLiteral => EToken.Number,
				ClassificationTypeNames.Operator => EToken.Operator,
				ClassificationTypeNames.OperatorOverloaded => EToken.Function,
				ClassificationTypeNames.ParameterName => EToken.Parameter,
				ClassificationTypeNames.PreprocessorKeyword => EToken.Preprocessor,
				//ClassificationTypeNames.PreprocessorText => EStyle.None,
				ClassificationTypeNames.PropertyName => EToken.Function,
				ClassificationTypeNames.Punctuation => EToken.Punctuation,
				ClassificationTypeNames.StringEscapeCharacter => EToken.StringEscape,
				ClassificationTypeNames.StringLiteral => EToken.String,
				ClassificationTypeNames.StructName => EToken.Type,
				//ClassificationTypeNames.Text => EStyle.None,
				ClassificationTypeNames.VerbatimStringLiteral => EToken.String,
				ClassificationTypeNames.TypeParameterName => EToken.Type,
				//ClassificationTypeNames.WhiteSpace => EStyle.None,

				ClassificationTypeNames.XmlDocCommentText => EToken.XmlDocText,
				ClassificationTypeNames.XmlDocCommentAttributeName => EToken.XmlDocTag,
				ClassificationTypeNames.XmlDocCommentAttributeQuotes => EToken.XmlDocTag,
				ClassificationTypeNames.XmlDocCommentAttributeValue => EToken.XmlDocTag,
				ClassificationTypeNames.XmlDocCommentCDataSection => EToken.XmlDocTag,
				ClassificationTypeNames.XmlDocCommentComment => EToken.XmlDocTag,
				ClassificationTypeNames.XmlDocCommentDelimiter => EToken.XmlDocTag,
				ClassificationTypeNames.XmlDocCommentEntityReference => EToken.XmlDocTag,
				ClassificationTypeNames.XmlDocCommentName => EToken.XmlDocTag,
				ClassificationTypeNames.XmlDocCommentProcessingInstruction => EToken.XmlDocTag,

				//FUTURE: Regex. But how to apply it to ARegex?
				//ClassificationTypeNames. => EStyle.,
				_ => EToken.None
				#endregion
			};

			EToken _TryResolveMethod()
			{ //ClassificationTypeNames.Identifier. Possibly method name when there are errors in arguments.
				var node = semo.Root.FindNode(v.TextSpan);
				if(node?.Parent is InvocationExpressionSyntax && !semo.GetMemberGroup(node).IsDefaultOrEmpty) return EToken.Function; //not too slow
				return EToken.None;
			}

			if(style == EToken.None) {
#if DEBUG
				switch(v.ClassificationType) {
				case ClassificationTypeNames.Identifier: break;
				case ClassificationTypeNames.LabelName: break;
				case ClassificationTypeNames.PreprocessorText: break;
				case ClassificationTypeNames.StaticSymbol: break;
				default: ADebug.PrintIf(!v.ClassificationType.Starts("regex"), $"<><c gray>{v.ClassificationType}, {v.TextSpan}<>"); break;
				}
#endif
				continue;
			}

			//int spanStart16 = v.TextSpan.Start, spanEnd16 = v.TextSpan.End;
			int spanStart16 = Math.Max(v.TextSpan.Start, start16), spanEnd16 = Math.Min(v.TextSpan.End, end16);
			int spanStart8 = doc.Pos8(spanStart16), spanEnd8 = doc.Pos8(spanEnd16);
			for(int i = spanStart8; i < spanEnd8; i++) b[i - start8] = (byte)style;
		}

#if PRINT
		p1.Next();
#endif
		doc.Call(SCI_STARTSTYLING, start8);
		unsafe { fixed(byte* bp = b) doc.Call(SCI_SETSTYLINGEX, b.Length, bp); }
		_modFromEnd = int.MaxValue;
		_visibleLines = minimal ? default : si.visibleFromLine..si.visibleToLine;
#if PRINT
		p1.Next('S');
#endif
		if(!minimal) _Fold(firstTime, cd, start8, end8);
#if PRINT
		p1.NW('F');
#endif
		if(!minimal) {
			_diagCounter = 6; //update diagnostics after 1.5 s
		} else {
			CodeInfo._diag.EraseIndicatorsInLine(doc, doc.Z.CurrentPos8);
		}
#if TRACE
		//if(!s_debugPerf) { s_debugPerf = true; APerf.NW('s'); }
#endif
	}
#if TRACE
	//static bool s_debugPerf;
#endif

	#region folding

	void _Fold(bool firstTime, in CodeInfo.Context cd, int start8, int end8)
	{
		//var p1 = APerf.Create();
		ADebug.PrintIf(!cd.document.TryGetSyntaxRoot(out _), "recreating syntax tree");
		var root = cd.document.GetSyntaxRootAsync().Result;
		//p1.Next('r');

		List<int> a = null;
		var doc = cd.sciDoc;
		var z = doc.Z;
		var code = cd.code;
		if(firstTime) { start8 = 0; end8 = doc.Len8; } //may need to restore saved folding
		int start16 = doc.Pos16(start8), end16 = doc.Pos16(end8), commentEnd = -1;

		//if start16 is in eg multiple //comments, need to start at the start of all the trivia block. Else may damage folding when editing.
		if(start16 > 0) {
			var span = root.FindToken(start16).LeadingTrivia.Span;
			if(start16 > span.Start && start16 < span.End) start16 = doc.Pos16(start8 = z.LineStartFromPos(false, doc.Pos8(span.Start)));
			//p1.Next('j');
		}

		//speed: the slowest is DescendantTrivia, then DescendantNodes. GetSyntaxRootAsync fast because the syntax tree is already created by the styling code.
		//rejected: async.
		//	Fast when eg typing. The syntax tree is already created, and folds only the visible part.
		//	Not too slow when folds all when opening large file. Eg 30 ms for 100K.

		foreach(var v in root.DescendantTrivia()) {
			var span = v.Span;
			if(span.End <= start16) continue;
			int pos = span.Start; if(pos >= end16) break;
			var kind = v.Kind();
			if(kind == SyntaxKind.WhitespaceTrivia || kind == SyntaxKind.EndOfLineTrivia) continue;
			//CiUtil.PrintNode(v);
			switch(kind) {
			case SyntaxKind.SingleLineCommentTrivia:
				if(code.Eq(pos, "//.")) {
					if(code.Length > pos + 3 && char.IsWhiteSpace(code[pos + 3])) _AddFoldPoint(pos, 1);
				} else if(code.Eq(pos, "//;")) {
					for(int j = pos + 2; j < code.Length && code[j] == ';'; j++) _AddFoldPoint(pos, -1);
				} else if(pos > commentEnd) {
					var k = v.Token.LeadingTrivia.Span;
					commentEnd = k.End;
					foreach(var g in s_rxComments.FindAllG(code, 0, k.Start..k.End)) {
						//AOutput.Write($"'{g}'");
						_AddFoldPoint(g.Start, 1);
						_AddFoldPoint(g.End, -1);
					}
					if(k.End > end16) end8 = doc.Pos8(end16 = k.End);
				}
				break;
			case SyntaxKind.SingleLineDocumentationCommentTrivia:
			case SyntaxKind.MultiLineDocumentationCommentTrivia:
			case SyntaxKind.MultiLineCommentTrivia:
				if(pos >= start16) _AddFoldPoint(pos, 1);
				_AddFoldPoint(span.End - 1, -1);
				break;
			case SyntaxKind.RegionDirectiveTrivia:
				_AddFoldPoint(pos, 1);
				break;
			case SyntaxKind.EndRegionDirectiveTrivia:
				_AddFoldPoint(pos, -1);
				break;
			case SyntaxKind.DisabledTextTrivia:
				if(pos > start16) _AddFoldPoint(pos - 1, 1);
				_AddFoldPoint(span.End - 1, -1);
				break;
			}
		}
		//p1.Next('t');

		void _AddFoldPoint(int pos16, int level)
		{
			//AOutput.Write(z.LineFromPos(true, pos16)+1, level);
			(a ??= new List<int>()).Add(doc.Pos8(pos16) | (level > 0 ? 0 : unchecked((int)0x80000000)));
		}

		int line = z.LineFromPos(false, start8), underlinedLine = line;

		foreach(var v in root.DescendantNodes()) {
			var span = v.Span;
			if(span.End <= start16) continue;
			int pos = span.Start; if(pos >= end16) break;
			//CiUtil.PrintNode(v);
			switch(v) {
			case BaseMethodDeclarationSyntax md when !(md.Body == null && md.ExpressionBody == null): //method, ctor, etc; not DllImport, interface, partial
			case BasePropertyDeclarationSyntax _: //property, event
			case BaseTypeDeclarationSyntax _: //class, struct, interface, enum
				if(pos >= start16) _AddFoldPoint(pos, 1);
				_AddFoldPoint(span.End, -1);

				//add separator below
				int li = z.LineFromPos(true, span.End);
				_DeleteUnderlinedLineMarkers(li);
				//if(underlinedLine != li) AOutput.Write("add", li + 1);
				if(underlinedLine != li) doc.Call(SCI_MARKERADD, li, SciCode.c_markerUnderline);
				else underlinedLine++;
				break;
			}
		}
		//p1.Next('n');

		_DeleteUnderlinedLineMarkers(z.LineFromPos(false, end8));

		void _DeleteUnderlinedLineMarkers(int beforeLine)
		{
			if((uint)underlinedLine > beforeLine) return;
			const int marker = 1 << SciCode.c_markerUnderline;
			for(; ; underlinedLine++) {
				underlinedLine = doc.Call(SCI_MARKERNEXT, underlinedLine, marker);
				if((uint)underlinedLine >= beforeLine) break;
				//AOutput.Write("delete", underlinedLine + 1);
				do doc.Call(SCI_MARKERDELETE, underlinedLine, SciCode.c_markerUnderline);
				while(0 != (marker & doc.Call(SCI_MARKERGET, underlinedLine)));
			}
		}
		//p1.Next('u');

		a?.Sort((p1, p2) => (p1 & 0x7fffffff) - (p2 & 0x7fffffff));
		//p1.Next('s');

		int lineTo = z.LineFromPos(false, end8); if(end8 > z.LineStart(false, lineTo)) lineTo++;
		//AOutput.Write(line + 1, lineTo + 1);
		unsafe { //we implement folding in Scintilla. Calling many SCI_SETFOLDLEVEL here would be slow.
			fixed(int* ip = a?.ToArray()) Sci_SetFoldLevels(doc.SciPtr, line, lineTo, a?.Count ?? 0, ip);
		}
		//p1.Next('f');
		doc._RestoreEditorData();
		//p1.NW('F');
	}
	static ARegex s_rxComments = new ARegex(@"(?m)^[ \t]*//(?!\.\s|;|/[^/]).*(\R\s*//(?!\.\s|;|/[^/]).*)+");

	static void _InitFolding(SciCode doc)
	{
		const int foldMrgin = SciCode.c_marginFold;
		doc.Call(SCI_SETMARGINTYPEN, foldMrgin, SC_MARGIN_SYMBOL);
		doc.Call(SCI_SETMARGINMASKN, foldMrgin, SC_MASK_FOLDERS);
		doc.Call(SCI_SETMARGINSENSITIVEN, foldMrgin, 1);

		doc.Call(SCI_MARKERDEFINE, SC_MARKNUM_FOLDEROPEN, SC_MARK_BOXMINUS);
		doc.Call(SCI_MARKERDEFINE, SC_MARKNUM_FOLDER, SC_MARK_BOXPLUS);
		doc.Call(SCI_MARKERDEFINE, SC_MARKNUM_FOLDERSUB, SC_MARK_VLINE);
		doc.Call(SCI_MARKERDEFINE, SC_MARKNUM_FOLDERTAIL, SC_MARK_LCORNER);
		doc.Call(SCI_MARKERDEFINE, SC_MARKNUM_FOLDEREND, SC_MARK_BOXPLUSCONNECTED);
		doc.Call(SCI_MARKERDEFINE, SC_MARKNUM_FOLDEROPENMID, SC_MARK_BOXMINUSCONNECTED);
		doc.Call(SCI_MARKERDEFINE, SC_MARKNUM_FOLDERMIDTAIL, SC_MARK_TCORNER);
		for(int i = 25; i < 32; i++) {
			doc.Call(SCI_MARKERSETFORE, i, 0xffffff);
			doc.Call(SCI_MARKERSETBACK, i, 0x808080);
			doc.Call(SCI_MARKERSETBACKSELECTED, i, i == SC_MARKNUM_FOLDER ? 0xFF : 0x808080);
		}
		doc.Call(SCI_MARKERENABLEHIGHLIGHT, 1);

		doc.Call(SCI_SETAUTOMATICFOLD, SC_AUTOMATICFOLD_SHOW //show hidden lines when header line deleted. Also when hidden text modified, and it is not always good.
									| SC_AUTOMATICFOLD_CHANGE); //show hidden lines when header line modified like '#region' -> '//#region'
		doc.Call(SCI_SETFOLDFLAGS, SC_FOLDFLAG_LINEAFTER_CONTRACTED);
		doc.Call(SCI_FOLDDISPLAYTEXTSETSTYLE, SC_FOLDDISPLAYTEXT_STANDARD);
		doc.Z.StyleForeColor(STYLE_FOLDDISPLAYTEXT, 0x808080);

		doc.Call(SCI_SETMARGINCURSORN, foldMrgin, SC_CURSORARROW);

		int wid = doc.Call(SCI_TEXTHEIGHT) - 4;
		doc.Z.MarginWidth(foldMrgin, Math.Max(wid, 12));
	}

	#endregion
}

partial class SciCode
{
	bool _FoldOnMarginClick(bool? fold, int startPos)
	{
		int line = Call(SCI_LINEFROMPOSITION, startPos);
		if(0 == (Call(SCI_GETFOLDLEVEL, line) & SC_FOLDLEVELHEADERFLAG)) return false;
		bool isExpanded = 0 != Call(SCI_GETFOLDEXPANDED, line);
		if(fold.HasValue && fold.GetValueOrDefault() != isExpanded) return false;
		if(isExpanded) {
			_FoldLine(line);
			//move caret out of contracted region
			int pos = Z.CurrentPos8;
			if(pos > startPos) {
				int i = Z.LineEnd(false, Call(SCI_GETLASTCHILD, line, -1));
				if(pos <= i) Z.CurrentPos8 = startPos;
			}
		} else {
			Call(SCI_FOLDLINE, line, 1);
		}
		return true;
	}

	void _FoldLine(int line)
	{
#if false
		Call(SCI_FOLDLINE, line);
#else
		string s = Z.LineText(line), s2 = "";
		for(int i = 0; i < s.Length; i++) {
			char c = s[i];
			if(c == '{') { s2 = "... }"; break; }
			if(c == '/' && i < s.Length - 1) {
				c = s[i + 1];
				if(c == '*') break;
				if(i < s.Length - 3 && c == '/' && s[i + 2] == '-' && s[i + 3] == '{') break;
			}
		}
		//quite slow. At startup ~250 mcs. The above code is fast.
		if(s2.Length == 0) Call(SCI_FOLDLINE, line); //slightly faster
		else Z.SetString(SCI_TOGGLEFOLDSHOWTEXT, line, s2);
#endif
	}

	internal void _RestoreEditorData()
	{
		if(_openState == 2) return;
		bool newFile = _openState == 1;
		_openState = 2;
		if(newFile) {
		} else {
			//restore saved folding, markers, scroll position and caret position
			var db = Program.Model.DB; if(db == null) return;
			try {
				using var p = db.Statement("SELECT top,pos,lines FROM _editor WHERE id=?", _fn.Id);
				if(p.Step()) {
					int cp = Z.CurrentPos8;
					int top = p.GetInt(0), pos = p.GetInt(1);
					var a = p.GetList<int>(2);
					if(a != null) {
						_savedLinesMD5 = _Hash(a);
						for(int i = a.Count - 1; i >= 0; i--) {
							int v = a[i];
							int line = v & 0x7FFFFFF, marker = v >> 27 & 31;
							if(marker == 31) _FoldLine(line);
							else Call(SCI_MARKERADDSET, line, 1 << marker);
						}
						if(cp > 0) Call(SCI_ENSUREVISIBLEENFORCEPOLICY, Z.LineFromPos(false, cp));
					}
					if(top + pos > 0 && cp == 0) {
						if(top > 0) Call(SCI_SETFIRSTVISIBLELINE, _savedTop = top);
						if(pos > 0 && pos <= Len8) Z.CurrentPos8 = _savedPos = pos;
					}
				}
			}
			catch(SLException ex) { ADebug.Print(ex); }
		}
	}
	byte _openState; //0 opened old file, 1 opened new file, 2 folding done

	/// <summary>
	/// Saves folding, markers etc in database.
	/// </summary>
	internal void _SaveEditorData()
	{
		//CONSIDER: save styling and fold levels of the visible part of current doc. Then at startup can restore everything fast, without waiting for warmup etc.
		//_TestSaveFolding();
		//return;

		//never mind: should update folding if edited and did not fold until end. Too slow. Not important.

		if(_openState < 2) return; //if did not have time to open editor data, better keep old data than delete. Also < 2 if not a code file.
		var db = Program.Model.DB; if(db == null) return;
		//var p1 = APerf.Create();
		var a = new List<int>();
		_GetLines(c_markerBookmark, a);
		_GetLines(c_markerBreakpoint, a);
		//p1.Next();
		_GetLines(31, a);
		//p1.Next();
		var hash = _Hash(a);
		//p1.Next();
		int top = Call(SCI_GETFIRSTVISIBLELINE), pos = Z.CurrentPos8;
		if(top != _savedTop || pos != _savedPos || hash != _savedLinesMD5) {
			//AOutput.Write("changed", a.Count);
			try {
				using var p = db.Statement("REPLACE INTO _editor (id,top,pos,lines) VALUES (?,?,?,?)");
				p.Bind(1, _fn.Id).Bind(2, top).Bind(3, pos).Bind(4, a).Step();
				_savedTop = top;
				_savedPos = pos;
				_savedLinesMD5 = hash;
			}
			catch(SLException ex) { ADebug.Print(ex); }
		}
		//p1.NW('D');

		/// <summary>
		/// Gets indices of lines containing markers or contracted folding points.
		/// </summary>
		/// <param name="marker">If 31, uses SCI_CONTRACTEDFOLDNEXT. Else uses SCI_MARKERNEXT; must be 0...24 (markers 25-31 are used for folding).</param>
		/// <param name="saved">Receives line indices | marker in high-order 5 bits.</param>
		void _GetLines(int marker, List<int> a/*, int skipLineFrom = 0, int skipLineTo = 0*/)
		{
			Debug.Assert((uint)marker < 32); //we have 5 bits for marker
			for(int i = 0; ; i++) {
				if(marker == 31) i = Call(SCI_CONTRACTEDFOLDNEXT, i);
				else i = Call(SCI_MARKERNEXT, i, 1 << marker);
				if((uint)i > 0x7FFFFFF) break; //-1 if no more; ensure we have 5 high-order bits for marker; max 134 M lines.
											   //if(i < skipLineTo && i >= skipLineFrom) continue;
				a.Add(i | (marker << 27));
			}
		}
	}

	//unsafe void _TestSaveFolding()
	//{
	//	//int n = Z.LineCount;
	//	//for(int i = 0; i < n; i++) AOutput.Write(i+1, (uint)Call(SCI_GETFOLDLEVEL, i));

	//	var a = new List<POINT>();
	//	for(int i = 0; ; i++) {
	//		i = Call(SCI_CONTRACTEDFOLDNEXT, i);
	//		if(i < 0) break;
	//		int j = Call(SCI_GETLASTCHILD, i, -1);
	//		//AOutput.Write(i, j);
	//		a.Add((i, j));
	//	}

	//	Call(SCI_FOLDALL, SC_FOLDACTION_EXPAND);
	//	Sci_SetFoldLevels(SciPtr, 0, Z.LineCount - 1, 0, null);
	//	ATimer.After(1000, _ => _TestRestoreFolding(a));
	//}

	//unsafe void _TestRestoreFolding(List<POINT> lines)
	//{
	//	var a = new int[lines.Count * 2];
	//	for(int i = 0; i < lines.Count; i++) {
	//		var p = lines[i];
	//		a[i * 2] = Z.LineStart(false, p.x);
	//		a[i * 2 + 1] = Z.LineStart(false, p.y) | unchecked((int)0x80000000);
	//	}
	//	Array.Sort(a, (e1, e2) => (e1 & 0x7fffffff) - (e2 & 0x7fffffff));
	//	fixed(int* ip = a) Sci_SetFoldLevels(SciPtr, 0, Z.LineCount - 1, a.Length, ip);
	//}

	int _savedTop, _savedPos;
	Au.Util.AHash.MD5Result _savedLinesMD5;

	static Au.Util.AHash.MD5Result _Hash(List<int> a)
	{
		if(a.Count == 0) return default;
		Au.Util.AHash.MD5 md5 = default;
		foreach(var v in a) md5.Add(v);
		return md5.Hash;
	}
}
