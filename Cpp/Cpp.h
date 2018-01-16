#pragma once

//This file is used internally.
//	In the future it also could be used by C++ projects. Currently the C++ declarations are not added.
//	Currently this dll is used only by a C# project.

#ifdef Cpp_EXPORTS
#include "stdafx.h"
#define EXPORT extern "C" __declspec(dllexport)
#else
#define EXPORT extern "C" __declspec(dllimport)
#endif

//Cpp_Acc::MISC::flags.
enum class eAccMiscFlags : BYTE {
	InProc = 1, //retrieved inproc
	UIA = 2,
	Java = 4,
};
ENABLE_BITMASK_OPERATORS(eAccMiscFlags);

//IAccessible* and child element id.
//Has only ctors. Does not have a dtor (does not Release etc), operator=, etc.
struct Cpp_Acc
{
	IAccessible* acc;
	long elem;
	struct MISC {
		eAccMiscFlags flags;
		BYTE role; //for optimization. 0 if not set or failed to get or VT_BSTR or does not fit in BYTE.
		WORD level; //for ToString. 0 if not set.
	} misc;

	Cpp_Acc() noexcept { Zero(); }

	//Does not AddRef.
	Cpp_Acc(IAccessible* acc_, int elem_, eAccMiscFlags flags_ = (eAccMiscFlags)0) noexcept
	{
		acc = acc_;
		elem = elem_;
		*(DWORD*)&misc = (DWORD)flags_;
	}

#ifdef Cpp_EXPORTS
	void Zero() { memset(this, 0, sizeof(*this)); }
	void SetRole();
	void SetRole(DWORD role) { misc.role = (BYTE)(role <= 0xff ? role : 0); }
	void SetLevel(DWORD level) { misc.level = (WORD)(level <= 0xffff ? level : 0xffff); }
#endif
};

//Flags for Cpp_AccFind.
//The same as C# AFFlags. Documented there.
//[Flags]
enum class eAF
{
	Reverse = 1,
	HiddenToo = 2,
	MenuToo = 4,
	SkipLists = 8,
	SkipWeb = 16,
	NotInProc=0x100,
	UIA=0x200,
};
ENABLE_BITMASK_OPERATORS(eAF);

//Parameters for Cpp_AccFind.
struct Cpp_AccParams {
	STR role, name, prop;
	int roleLength, nameLength, propLength;
	eAF flags;
	int skip;
	WCHAR resultProp;

	Cpp_AccParams() noexcept { memset(this, 0, sizeof(*this)); }
};

//Cpp_AccFind callback function type.
//Must Release a.iacc. Preferably later, in spare time. Can do it in another thread.
using Cpp_AccCallbackT = BOOL(__stdcall*)(Cpp_Acc a);

enum class eError
{
	NotFound = 0x1001, //AO not found. With FindAll - no errors. This is actually not an error.
	InvalidParameter = 0x1002, //invalid parameter, for example wildcard expression (or regular expression in it)
	WindowClosed = 0x1003, //the specified window handle is invalid or the window was destroyed while injecting
	WaitChromeDisabled = 0x1004, //need to wait while enabling Chrome AOs
#ifdef Cpp_EXPORTS
	Inject = 0x1100, //failed to inject this dll into the target process
	WindowOfThisThread = 0x1101, //the specified window belongs to the caller thread
	UseNotInProc = 0x1102, //window class name is Windows.UI.Core.CoreWindow etc
#endif
};

//Our custom OBJID for Cpp_AccFromWindow.
#define OBJID_JAVA -100
#define OBJID_UIA -101

//Our custom NAVDIR_ for Cpp_AccNavigate.
#define NAVDIR_PARENT 9
#define NAVDIR_CHILD 10

//FUTURE: declare exports (when stable), like:
//EXPORT HRESULT Cpp_AccFind(...);

#ifdef Cpp_EXPORTS
#include "util.h"
#include "internal.h"
#endif
