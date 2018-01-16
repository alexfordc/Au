#include "stdafx.h"
#include "cpp.h"
#include "acc.h"

#pragma comment(lib, "oleacc.lib")

bool AccMatchHtmlAttributes(IAccessible* iacc, NameValue* prop, int count);

class AccFinder
{
	//A parsed path part, when the role parameter is path like "A/B[4]/C". 
	struct _PathPart {
		STR role; //eg "B" if "B[4]" //stored in _roleStrings
		int startIndex; //eg 4 if "B[4]"
		bool exactIndex; //true if eg "B[4!]"

		_PathPart() { ZEROTHIS; }
	};

	AccFindCallback* _callback; //receives found AO
	STR _role; //null if used path or if the role parameter is null
	_PathPart* _path; //null if no path
	Bstr _controlClass; //used when role has prefix "class=x:". Then _flags2 has eAF2::InControls.
	str::Wildex _name; //name. If the name parameter is null, _name.Is()==false.
	NameValue* _prop; //other string properties and HTML attributes. Specified in the prop parameter, like L"value=XXX\0 a:id=YYY".
	STR* _skipRoles; //roles to skip (and descendants) when searching. Specified in the prop parameter.
	Bstr _roleStrings, _propStrings; //a copy of the input role/prop string when eg need to parse (modify) the string
	int _pathCount; //_path array element count
	int _propCount; //_prop array element count
	int _skipRolesCount; //_skipRoles array element count
	int _controlId; //used when role has prefix "id=x:". Then _flags2 has eAF2::InControls, and _controlClass is null.
	int _minLevel, _maxLevel; //min and max level to search in the object subtree. Specified in the prop parameter. Default 0 1000.
	int _maxChildren; //skip objects that have more children. Specified in the prop parameter. Default 10000.
	int _stateYes, _stateNo; //the AO must have all _stateYes flags and none of _stateNo flags. Specified in the prop parameter.
	int _elem; //child element id. Specified in the prop parameter. _flags2 has IsElem.
	RECT _rect; //AO location. Specified in the prop parameter. _flags2 has IsRect.
	eAF _flags; //user
	eAF2 _flags2; //internal
	bool _found; //true when the AO has been found
	IAccessible** _findDOCUMENT; //used by _FindDocumentSimple, else null
	BSTR* _errStr; //error string, when a parameter is invalid
	HWND _w; //window in which currently searching

	bool _Error(STR es) {
		if(_errStr) *_errStr = SysAllocString(es);
		return false;
	}

	HRESULT _ErrorHR(STR es) {
		_Error(es);
		return (HRESULT)eError::InvalidParameter;
	}

	bool _ParseRole(STR role, int roleLen)
	{
		if(role == null) return true;
		if(roleLen == 0) return _Error(L"role cannot be \"\".");

		//is prefix?
		int iColon = -1, iEq = -1;
		for(int i = 0; i < roleLen; i++) {
			auto c = role[i];
			if(c == ':') { iColon = i; break; }
			if(c == '=' && iEq < 0) iEq = i + 1;
		}
		if(iColon > 0) {
			int iCE = iEq < 0 ? iColon : iEq;
			int prefix = str::Switch(role, iCE, { L"class=", L"id=", L"web", L"firefox", L"chrome" });
			if(prefix > 0) {
				if(iColon == iEq) goto ge;
				switch(prefix) {
				case 1:
					_controlClass.Assign(role + 6, iColon - iEq);
					break;
				case 2:
					LPWSTR se;
					_controlId = wcstol(role + 3, &se, 0);
					if(se != role + iColon) goto ge;
					break;
				case 3: _flags2 |= eAF2::InWebPage; break; //auto-detect by window class name, or Cpp_AccFind already found IES and added InIES
				case 4: _flags2 |= eAF2::InFirefoxPage | eAF2::InWebPage; break;
				case 5: _flags2 |= eAF2::InChromePage | eAF2::InWebPage; break;
				}
				if(prefix <= 2) _flags2 |= eAF2::InControls;
				_flags2 |= eAF2::RoleHasPrefix;
				if(++iColon == roleLen) return true;
				role += iColon; roleLen -= iColon;
			}
		}

		//is path?
		if(_pathCount = (int)std::count(role, role + roleLen, '/')) {
			auto a = _path = new _PathPart[++_pathCount];
			int level = 0;
			LPWSTR s = _roleStrings.Assign(role, roleLen);
			for(LPWSTR partStart = s, eos = s + roleLen; s <= eos; s++) {
				auto c = *s;
				if(c == '/' || c == '[' || s == eos) {
					_PathPart& e = a[level];
					if(s > partStart) { //else can be any role at this level
						e.role = partStart;
						*s = 0;
					}
					if(c == '[') {
						auto s0 = s + 1;
						e.startIndex = wcstol(s0, &s, 0);
						if(s == s0) goto ge;
						if(*s == '!') { s++; e.exactIndex = true; }
						if(*s++ != ']') goto ge;
						if(s < eos && *s != '/') goto ge;
					}
					partStart = s + 1;
					level++;
				}
			}

			//Print(_pathCount); for(int i = 0; i < _pathCount; i++) Printf(L"'%s'  %i %i", a[i].role, a[i].startIndex, a[i].exactIndex);

			//FUTURE: "PART/PART/.../PART"
		} else {
			if(role[roleLen] == 0) _role = role;
			else _role = _roleStrings.Assign(role, roleLen);
		}

		return true;
	ge:
		return _Error(L"Invalid role.");
	}

	void _ParseSkipRoles(LPWSTR s, LPWSTR eos)
	{
		_skipRolesCount = (int)std::count(s, eos, ',') + 1;
		_skipRoles = new STR[_skipRolesCount];
		int i = 0;
		for(LPWSTR start = s; s <= eos; ) {
			if(*s == ',' || s == eos) {
				_skipRoles[i++] = start;
				*s++ = 0; if(*s == ' ') s++;
				start = s;
			} else s++;
		}

		//Print(_skipRolesCount); for(i = 0; i < _skipRolesCount; i++) Print(_skipRoles[i]);
	}

	bool _ParseState(LPWSTR s, LPWSTR eos)
	{
		for(LPWSTR start = s; s <= eos; ) {
			if(*s == ',' || s == eos) {
				bool not; if(*start == '!') { start++; not = true; } else not = false;
				int state;
				if(s > start && *start >= '0' && *start <= '9') {
					state = wcstol(start, null, 0);
				} else {
					state = ao::StateFromString(start, s - start);
					if(state == 0) return _Error(L"Unknown state name.");
				}
				if(not) _stateNo |= state; else _stateYes |= state;
				if(*++s == ' ') s++;
				start = s;
			} else s++;
		}
		//Printf(L"0x%X  0x%X", _stateYes, _stateNo);
		return true;
	}

	bool _ParseRect(LPWSTR s, LPWSTR eos)
	{
		if(*s++ != '{' || *(--eos) != '}') goto ge;
		for(; s < eos; s++) {
			LPWSTR s1 = s++, s2;
			if(*s++ != '=') goto ge;
			int t = wcstol(s, &s2, 0);
			if(s2 == s) goto ge; s = s2;
			switch(*s1) {
			case 'L': _rect.left = t; _flags2 |= eAF2::IsRectL; break;
			case 'T': _rect.top = t; _flags2 |= eAF2::IsRectT; break;
			case 'W': _rect.right = t; _flags2 |= eAF2::IsRectW; break;
			case 'H': _rect.bottom = t; _flags2 |= eAF2::IsRectH; break;
			default: goto ge;
			}
		}
		//Printf(L"{%i %i %i %i}", _rect.left, _rect.top, _rect.right, _rect.bottom);
		return true;
	ge:
		return _Error(L"Invalid rect format.");
	}

	bool _ParseProp(STR prop, int propLen)
	{
		if(prop == null) return true;

		int elemCount = (int)std::count(prop, prop + propLen, '\0') + 1;
		_prop = new NameValue[elemCount]; _propCount = 0; //info: finally can be _propCount<elemCount, ie not all elements used
		LPWSTR s0 = _propStrings.Assign(prop, propLen), s2, s3;
		for(LPWSTR s = s0, na = s0, va = null, eos = s0 + propLen; s <= eos; s++) {
			auto c = *s;
			if(c == 0) {
				if(s > s0) {
					if(va == null) return _Error(L"Missing = in prop string.");
					//Printf(L"na='%s' va='%s'    naLen=%i vaLen=%i", na, va, va - 1 - na, s - va);

					bool addToProp = true;
					if(na[0] != '@') { //HTML attribute names have prefix "@"
						int i = str::Switch(na, va - 1 - na, {
							L"value", L"description", L"help", L"action", L"key", L"uiaAutomationId", //string props
							L"state", L"level", L"maxChildren", L"skipRoles", L"rect", L"elem",
						});

						if(i == 0) return _Error(L"Unknown property. For HTML attributes use prefix @.");
						const int nStrProp = 6;
						if(i > nStrProp) {
							addToProp = false;
							switch(i - nStrProp) {
							case 1:
								if(!_ParseState(va, s)) return false;
								break;
							case 2:
								if(_path != null) return _Error(L"Path and level.");
								_minLevel = wcstol(va, &s2, 0);
								if(s2 == va || _minLevel<0) goto ge;
								if(s2 == s) _maxLevel = _minLevel;
								else if(s2 < s && *s2 == ' ') {
									_maxLevel = wcstol(++s2, &s3, 0);
									if(s3 != s || _maxLevel < _minLevel) goto ge;
								} else goto ge;
								break;
							case 3:
								_maxChildren = wcstol(va, &s2, 0);
								if(_maxChildren <= 0 || s2 != s) goto ge;
								break;
							case 4:
								_ParseSkipRoles(va, s);
								break;
							case 5:
								if(!_ParseRect(va, s)) return false;
								break;
							case 6:
								_elem = wcstol(va, &s2, 0);
								if(s2 != s) goto ge;
								_flags2 |= eAF2::IsElem;
								break;
							}
						}
					}
					if(addToProp) {
						assert(_propCount < elemCount);
						NameValue& x = _prop[_propCount++];
						x.name = na;
						if(!x.value.Parse(va, s - va, true, _errStr)) return false;
					}
				}
				while(++s <= eos && *s <= ' '); //allow space before name, eg "name1=value1\0 name2=..."
				na = s--;
				va = null;
			} else if(c == '=' && va == null) {
				*s = 0;
				va = s + 1;
			}
		}

		return true;
	ge: return _Error(L"Invalid prop string.");
	}

public:

	AccFinder(BSTR* errStr = null) {
		ZEROTHIS;
		_errStr = errStr;
		_maxLevel = 1000;
		_maxChildren = 10000;
	}

	~AccFinder()
	{
		delete[] _path;
		delete[] _prop;
		delete[] _skipRoles;
	}

	bool SetParams(const Cpp_AccParams& ap, eAF2 flags2)
	{
		_flags = ap.flags;
		_flags2 = flags2;
		if(!_ParseRole(ap.role, ap.roleLength)) return false;
		if(ap.name != null && !_name.Parse(ap.name, ap.nameLength, true, _errStr)) return false;
		if(!_ParseProp(ap.prop, ap.propLength)) return false;
		if(!!(_flags2&eAF2::InWebPage)) _flags |= eAF::MenuToo;

		return true;
	}

	HRESULT Find(HWND w, const Cpp_Acc* a, AccFindCallback* callback)
	{
		assert(!!w == !a);
		_callback = callback;

		if(a) {
			if(!!(_flags2&eAF2::RoleHasPrefix)) return _ErrorHR(L"role cannot have a prefix when searching in Acc.");
			assert(!(_flags&eAF::UIA));

			_FindInAcc(ref *a, 0);
		} else if(!!(_flags2 & eAF2::InWebPage)) {
			if(!!(_flags&eAF::UIA)) return _ErrorHR(L"Cannot use flag UIA when searching in web page.");

			if(!!(_flags2 & eAF2::InIES)) { //info: Cpp_AccFind finds IES control and adds this flag
				_FindInWnd(w);
				//info: the hierarchy is WINDOW/CLIENT/PANE, therefore PANE will be at level 0
			} else {
				AccDtorIfElem0 aDoc;
				//Perf.First();
				HRESULT hr = _FindDocument(w, out aDoc);
				//Perf.NW();
				if(hr) return hr;

				switch(_Match(ref aDoc, 0)) {
				case _eMatchResult::SkipChildren: return (HRESULT)eError::NotFound;
				case _eMatchResult::Continue: _FindInAcc(ref aDoc, 1);
				}
			}
		} else if(!!(_flags2&eAF2::InControls)) {
			wnd::EnumChildWindows(w, [this](HWND c)
			{
				if(!(_flags&eAF::HiddenToo) && !IsWindowVisible(c)) return true;
				if(_controlClass) {
					if(!wnd::ClassNameIs(c, _controlClass)) return true;
				} else {
					if(GetDlgCtrlID(c) != _controlId) return true;
				}
				return (bool)_FindInWnd(c);
			});
		} else {
			_w = w;
			_FindInWnd(w);
		}

		return _found ? 0 : (HRESULT)eError::NotFound;
	}

private:
	HRESULT _FindInWnd(HWND w)
	{
		AccDtorIfElem0 aw;
		HRESULT hr;
		if(!!(_flags&eAF::UIA)) {
			hr = AccUiaFromWindow(w, &aw.acc);
			aw.misc.flags = eAccMiscFlags::UIA;
			//FUTURE: to make faster, add option to use IUIAutomationElement::FindFirst or FindAll.
			//	Problems: 1. No Level. 2. Cannot apply many flags; then in some cases can be slower or less reliable.
			//	Not very important. Now fast enough. Edge only 3 times slower (outproc); many times faster than outproc Chrome. JavaFX almost same speed (inproc).
		} else {
			hr = ao::AccFromWindowSR(w, OBJID_WINDOW, &aw.acc);
			aw.misc.role = ROLE_SYSTEM_WINDOW;
		}
		if(hr) return hr;
		if(_FindInAcc(ref aw, 0)) return 0; //note: caller also must check _found; this is just for EnumChildWindows.
		return (HRESULT)eError::NotFound;
	}

	//Returns true to stop.
	bool _FindInAcc(const Cpp_Acc& aParent, int level)
	{
		int startIndex = 0; bool exactIndex = false;
		if(_path != null) {
			startIndex = _path[level].startIndex;
			if(_path[level].exactIndex) exactIndex = true;
		}

		AccChildren c(ref aParent, startIndex, exactIndex, !!(_flags&eAF::Reverse), _maxChildren);
		if(c.Count() == 0) {
			if(_w) {
				//Java?
				if(level == 1 && aParent.misc.role == ROLE_SYSTEM_CLIENT && !(_flags2&eAF2::InControls) && !(GetWindowLongW(_w, GWL_STYLE)&WS_CHILD)) {
					if(wnd::ClassNameIs(_w, L"SunAwt*")) {
						AccDtorIfElem0 aw(AccJavaFromWindow(_w), 0, eAccMiscFlags::Java);
						if(aw.acc) {
							_w = 0;
							return _FindInAcc(ref aw, 1);
						}
					}
				}
				//rejected: enable Chrome web AOs. Difficult to implement (lazy, etc). Let use prefix "web:".
			}
			return false;
		}
		for(;;) {
			AccDtorIfElem0 aChild;
			if(!c.GetNext(out aChild)) break;

			switch(_Match(ref aChild, level)) {
			case _eMatchResult::Stop: return true;
			case _eMatchResult::SkipChildren: continue;
			}

			if(_FindInAcc(ref aChild, level + 1)) return true;
		} //now a.a is released if a.elem==0
		return false;
	}

	enum class _eMatchResult { Continue, Stop, SkipChildren };

	_eMatchResult _Match(ref AccDtorIfElem0& a, int level)
	{
		if(_findDOCUMENT && a.elem != 0) return _eMatchResult::SkipChildren;

		bool skipChildren = a.elem != 0 || level >= _maxLevel;
		bool hiddenToo = !!(_flags&eAF::HiddenToo);
		STR roleNeeded = _path != null ? _path[level].role : _role;
		_AccState state(ref a);

		_variant_t varRole;
		int role = a.get_accRole(out varRole);
		a.SetRole(role);
		a.SetLevel(level);
		STR roleString = null;

		//a.PrintAcc();

		if(_findDOCUMENT) {
			auto fdr = _FindDocumentCallback(ref a);
			if(skipChildren && fdr == _eMatchResult::Continue) fdr = _eMatchResult::SkipChildren;
			return fdr;
		}

		//skip AO of user-specified roles
		if(_skipRoles) {
			roleString = ao::RoleToString(ref varRole);
			for(int i = 0; i < _skipRolesCount; i++) if(!wcscmp(_skipRoles[i], roleString)) return _eMatchResult::SkipChildren;
		}

		if(level < _minLevel) goto gr;

		if(roleNeeded != null) {
			if(!roleString) roleString = ao::RoleToString(ref varRole);
			if(wcscmp(roleNeeded, roleString)) {
				if(_path != null) return _eMatchResult::SkipChildren;
				goto gr;
			}
		}
		if(_path != null) {
			if(level < _pathCount - 1) goto gr;
			skipChildren = true;
		}

		if(!!(_flags2&eAF2::IsElem) && a.elem != _elem) goto gr;

		if(_name.Is() && !a.MatchStringProp(L"name", ref _name)) goto gr;

		if(!hiddenToo) {
			switch(state.IsInvisible()) {
			case 2: //INVISISBLE and OFFSCREEN
				if(!_IsRoleToSkipIfInvisible(role)) break; //info: _IsRoleToSkipIfInvisible prevents finding background DOCUMENT in Firefox
			case 1: //only INVISIBLE
				skipChildren = true; goto gr;
			}
		}

		if(!!(_stateYes | _stateNo)) {
			auto k = state.State();
			if((k&_stateYes) != _stateYes) goto gr;
			if(!!(k&_stateNo)) goto gr;
		}

		if(!!(_flags2&eAF2::IsRect)) {
			long L, T, W, H;
			if(0 != a.acc->accLocation(&L, &T, &W, &H, ao::VE(a.elem))) goto gr;
			if(!!(_flags2&eAF2::IsRectL) && L != _rect.left) goto gr;
			if(!!(_flags2&eAF2::IsRectT) && T != _rect.top) goto gr;
			if(!!(_flags2&eAF2::IsRectW) && W != _rect.right) goto gr;
			if(!!(_flags2&eAF2::IsRectH) && H != _rect.bottom) goto gr;
		}

		if(_propCount) {
			bool hasHTML = false;
			for(int i = 0; i < _propCount; i++) {
				NameValue& p = _prop[i];
				if(p.name[0] == '@') hasHTML = true;
				else if(!a.MatchStringProp(p.name, ref p.value)) goto gr;
			}
			if(hasHTML) {
				if(a.elem || !AccMatchHtmlAttributes(a.acc, _prop, _propCount)) goto gr;
			}
		}

		switch((*_callback)(a)) {
		case eAccFindCallbackResult::Continue: goto gr;
		case eAccFindCallbackResult::StopFound: _found = true;
		//case eAccFindCallbackResult::StopNotFound: break;
		}
		return _eMatchResult::Stop;

	gr:
		if(!skipChildren) {
			//depending on flags, skip children of AO that often have many descendants (eg MENUITEM, LIST, OUTLINE)
			skipChildren = _IsRoleToSkipDescendants(role, roleNeeded, a.acc);

			//skip children of invisible AO that often have many descendants (eg DOCUMENT, WINDOW)
			if(!skipChildren && !hiddenToo && _IsRoleToSkipIfInvisible(role)) skipChildren = state.IsInvisible();
		}
		return skipChildren ? _eMatchResult::SkipChildren : _eMatchResult::Continue;
	}

	//Gets AO state.
	//The first time calls get_accState. Later returns cached value.
	class _AccState {
		const AccRaw& _a;
		long _state;
	public:
		_AccState(ref const AccRaw& a) : _a(a) { _state = -1; }

		int State() {
			if(_state == -1) _a.get_accState(out _state);
			return _state;
		}

		//Returns: 1 INVISIBLE and not OFFSCREEN, 2 INVISIBLE and OFFSCREEN, 0 none.
		int IsInvisible() {
			switch(State()&(STATE_SYSTEM_INVISIBLE | STATE_SYSTEM_OFFSCREEN)) {
			case STATE_SYSTEM_INVISIBLE: return 1;
			case STATE_SYSTEM_INVISIBLE | STATE_SYSTEM_OFFSCREEN: return 2;
			}
			return 0;
		}
	};

	static bool _IsRoleToSkipIfInvisible(int roleE)
	{
		switch(roleE) {
		//case ROLE_SYSTEM_MENUBAR: case ROLE_SYSTEM_TITLEBAR: case ROLE_SYSTEM_SCROLLBAR: case ROLE_SYSTEM_GRIP: //nonclient, already skipped
		case ROLE_SYSTEM_WINDOW: //child control
		case ROLE_SYSTEM_DOCUMENT: //web page in Firefox, Chrome
		case ROLE_SYSTEM_PROPERTYPAGE: //page in multi-tab dialog or window
		case ROLE_SYSTEM_GROUPING: //eg some objects in Firefox
		case ROLE_SYSTEM_ALERT: //eg web browser message box. In Firefox can be some invisible alerts.
		case ROLE_SYSTEM_MENUPOPUP: //eg in Firefox.
			return true;
			//note: these roles must be the same as in Acc.IsInvisible
		}
		return false;
		//note: don't add CLIENT. It is often used as default role. Although in some windows it can make faster.
		//note: don't add PANE. Too often used for various purposes. Bug in Edge: active non-first tab is PANE, state INVISIBLE|OFFSCREEN.

		//problem: some frameworks mark visible offscreen objects as invisible. Eg IE, WPF, Windows controls. Not Firefox, Chrome.
		//	Can be even parent marked as invisible when child not. Then we'll not find child if parent's role is one of above.
		//	Never mind. This probably will be rare with these roles. Then user can add flag HiddenToo.
		//	But code tools should somehow detect it and add the flag.
	}

	bool _IsRoleToSkipDescendants(int role, STR roleNeeded, IAccessible* a)
	{
		switch(role) {
		case ROLE_SYSTEM_MENUITEM:
			if(!(_flags&eAF::MenuToo))
				if(!str::Switch(roleNeeded, { L"MENUITEM", L"MENUPOPUP" })) return true;
			break;
		case ROLE_SYSTEM_OUTLINE:
		case ROLE_SYSTEM_LIST:
			if(!!(_flags&eAF::SkipLists)) return true;
			break;
		case ROLE_SYSTEM_DOCUMENT:
			if(!!(_flags&eAF::SkipWeb)) return true;
			break;
		case ROLE_SYSTEM_PANE:
			if(!!(_flags&eAF::SkipWeb)) {
				HWND w;
				if(0 == WindowFromAccessibleObject(a, out &w) && wnd::ClassNameIs(w, c_IES)) return true;
			}
			break;
		}
		return false;
	}

	//Finds DOCUMENT of Firefox, Chrome or some other program.
	//Enables Chrome web page AOs.
	//Returns 0, NotFound or WaitChromeDisabled.
	HRESULT _FindDocument(HWND w, out AccRaw& ar)
	{
		assert(ar.IsEmpty());

		AccDtorIfElem0 ap_;
		if(AccessibleObjectFromWindow(w, OBJID_CLIENT, IID_IAccessible, (void**)&ap_.acc)) return (HRESULT)eError::NotFound;
		IAccessible* ap = ap_.acc;

		if(!(_flags2 & (eAF2::InFirefoxPage | eAF2::InChromePage))) {
			switch(wnd::ClassNameIs(w, { L"Mozilla*", L"Chrome*" })) {
			case 1: _flags2 |= eAF2::InFirefoxPage; break;
			case 2: _flags2 |= eAF2::InChromePage; break;
			}
		}

		if(!!(_flags2 & eAF2::InFirefoxPage)) {
			//To get DOCUMENT, use Navigate(0x1009). It is documented and tested on FF>=2.
			_variant_t vNav;
			int hr = ap->accNavigate(0x1009, ao::VE(), out &vNav);
			if(hr == 0 && vNav.vt == VT_DISPATCH && vNav.pdispVal && 0 == vNav.pdispVal->QueryInterface(&ar.acc) && ar.acc) {
				return 0;
				//note: don't check BUSY state, it's unreliable.
			}

			//In some Firefox versions (56, 57) accNavigate(0x1009) is broken.
			//In very old Firefox, Navigate() used to fail when calling first time after starting Firefox.
			//Also ocassionally fails in some pages, even if page is loaded, maybe when executing scripts.
			//Then _FindDocumentSimple finds it. Also, the caller by default waits.

		} else if(!!(_flags2 & eAF2::InChromePage)) {
			return GetChromeDOCUMENT(w, ap, out ar);
		}

		PRINTS(L"unknown browser, or failed Firefox accNavigate(0x1009)");
		return _FindDocumentSimple(ap, out ar, _flags2);
	}

	//Finds DOCUMENT with AccFinder::Find. Skips OUTLINE etc.
	//Returns 0 or NotFound.
	static HRESULT _FindDocumentSimple(IAccessible* ap, out AccRaw& ar, eAF2 flags2)
	{
		AccFinder f;
		f._findDOCUMENT = &ar.acc;
		f._flags2 = flags2&(eAF2::InChromePage | eAF2::InFirefoxPage);
		f._maxLevel = 10; //DOCUMENT is at level 3 in current version
		Cpp_Acc a(ap, 0);
		if(0 != f.Find(0, &a, null)) return (HRESULT)eError::NotFound;
		return 0;

		//when outproc, sometimes fails to get DOCUMENT role while enabling Chrome AOs. The caller will wait/retry.
	}

	//Used by _FindDocumentSimple.
	_eMatchResult _FindDocumentCallback(ref const AccRaw& a)
	{
		int role = a.misc.role;
		if(role == ROLE_SYSTEM_DOCUMENT) {
			long state; if(0 != a.get_accState(out state) || !!(state&STATE_SYSTEM_INVISIBLE)) return _eMatchResult::SkipChildren;
			if(!!(_flags2&eAF2::InChromePage)) { //skip devtools DOCUMENT
				Bstr b;
				if(0 == a.acc->get_accValue(ao::VE(), &b) && b) {
					//Print(b);
					if(b.Length() >= 16 && !wcsncmp(b, L"chrome-devtools:", 16)) return _eMatchResult::SkipChildren;
				}
			}
			a.acc->AddRef();
			*_findDOCUMENT = a.acc;
			_found = true;
			return _eMatchResult::Stop;
		}

		static const BYTE b[] = { ROLE_SYSTEM_MENUBAR, ROLE_SYSTEM_TITLEBAR, ROLE_SYSTEM_MENUPOPUP, ROLE_SYSTEM_TOOLBAR,
			ROLE_SYSTEM_STATUSBAR, ROLE_SYSTEM_OUTLINE, ROLE_SYSTEM_LIST, ROLE_SYSTEM_SCROLLBAR, ROLE_SYSTEM_GRIP,
			ROLE_SYSTEM_SEPARATOR, ROLE_SYSTEM_PUSHBUTTON, ROLE_SYSTEM_TEXT, ROLE_SYSTEM_STATICTEXT, ROLE_SYSTEM_TOOLTIP,
			ROLE_SYSTEM_TABLE,
		};
		for(int i = 0; i < _countof(b); i++) if(role == b[i]) return _eMatchResult::SkipChildren;
		return _eMatchResult::Continue;
	}

	//#include "IAccessible2.h"
	MIDL_INTERFACE("E89F726E-C4F4-4c19-BB19-B647D7FA8478")
		IAccessible2 : public IAccessible{ };

public:
	//Finds Chrome DOCUMENT (web page) and enables its descendant AOs.
	//Returns 0, NotFound or WaitChromeDisabled.
	static HRESULT GetChromeDOCUMENT(HWND w, IAccessible* ap, out AccRaw& ar)
	{
		assert(ar.IsEmpty());

		HRESULT hr = _FindDocumentSimple(ap, out ar, eAF2::InChromePage);

		//we use a window prop for the AO enabling status
		auto enablingStatus = WinFlags::Get(w)&eWinFlags::AccEnableMask;

		if(hr != 0) {
			//when not in-proc, sometimes does not find while enabling, eg fails to get role because the AO is disconnected
			if(enablingStatus == eWinFlags::AccEnableStarted) return (HRESULT)eError::WaitChromeDisabled;
			return hr;
		}
		if(!!(enablingStatus& eWinFlags::AccEnableYes)) return 0;

		//when Chrome web page AOs disabled, DOCUMENT has BUSY state, until enabling finished. Later never has BUSY state.
		bool isEnabled = false; long state, cc;
		if(0 == ar.get_accState(out state) && !(state&STATE_SYSTEM_BUSY)) isEnabled = true; //not BUSY
		else if(0 == ar.acc->get_accChildCount(&cc) && cc) isEnabled = true; //or has children

		WinFlags::Set(w, isEnabled ? eWinFlags::AccEnableYes : eWinFlags::AccEnableStarted, eWinFlags::AccEnableMask);

		if(isEnabled) return 0;
		ar.Dispose();

		//enable web page AOs
		IAccessible2* a2 = null;
		if(QueryService(ap, &a2, &IID_IAccessible)) a2->Release();
		//succeeds inproc, fails outproc, but enables AOs anyway. QI always fails.
		//speed: < 1% of Find. First time 5%.
		//note: this is undocumented and may stop working with a new Chrome version.
		//note: with old Chrome versions need QS(ar.a), not QS(ap).
		//note: does not enable AOs if Find not called before.

		return (HRESULT)eError::WaitChromeDisabled; //let the caller wait/retry, because we cannot wait inproc. By default waits when NotFound too, but much shorter.
	}
};

HRESULT AccFind(AccFindCallback& callback, HWND w, Cpp_Acc* aParent, const Cpp_AccParams& ap, eAF2 flags2, out BSTR& errStr)
{
	AccFinder f(&errStr);
	if(!f.SetParams(ref ap, flags2)) return (HRESULT)eError::InvalidParameter;
	return f.Find(w, aParent, &callback);
}

HRESULT GetChromeDOCUMENT(HWND w, IAccessible* aCLIENT, out IAccessible** ar)
{
	AccRaw a;
	HRESULT hr = AccFinder::GetChromeDOCUMENT(w, aCLIENT, out a);
	*ar = a.acc;
	return hr;
}

namespace inproc
{
HRESULT AccEnableChrome(MarshalParams_AccElem* p)
{
	HWND w = (HWND)(LPARAM)p->elem;
	IAccessiblePtr aCLIENT;
	HRESULT hr = AccessibleObjectFromWindow(w, OBJID_CLIENT, IID_IAccessible, (void**)&aCLIENT);
	if(hr) return hr;
	AccDtorIfElem0 a;
	return AccFinder::GetChromeDOCUMENT(w, aCLIENT, out a);
}
}

namespace outproc
{
//Returns: 0 not Chrome, 1 Chrome was already enabled, 2 Chrome enabled now.
int AccEnableChrome(HWND w, bool checkClassName)
{
	assert(!(GetWindowLongW(w, GWL_STYLE)&WS_CHILD));

	if(checkClassName && !wnd::ClassNameIs(w, L"Chrome*")) return 0;

	auto wf = WinFlags::Get(w);
	if(!!(wf&eWinFlags::AccEnableYes)) return 1;
	if(!!(wf&eWinFlags::AccEnableMask)) return 0; //No or Started

	//Perf.First();
	IAccessible* iAgent;
	if(0 != InjectDllAndGetAgent(w, out iAgent)) return 0;
	//Perf.NW();

	InProcCall c;
	auto p = (MarshalParams_AccElem*)c.AllocParams(iAgent, InProcAction::IPA_AccEnableChrome, sizeof(MarshalParams_AccElem));
	p->elem = (int)(LPARAM)w;

	int R = 0;
	for(int i = 0; i < 100; i++) {
		//Perf.First();
		HRESULT hr = c.Call();
		//Perf.NW();
		if(hr) {
			if(hr == (HRESULT)eError::WaitChromeDisabled) R = 2;
			if(R == 2) {
				Sleep(10);
				continue;
			}
		} else {
			if(R == 2) Sleep(10);
			else R = 1;
		}
		break;
	}
	if(R == 0) WinFlags::Set(w, eWinFlags::AccEnableNo);
	return R;
}
}
