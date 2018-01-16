#pragma once
#include "stdafx.h"

#define ZEROTHIS memset(this, 0, sizeof(*this))
#define ZEROTHISFROM(member) memset((LPBYTE)this+((LPBYTE)&member-(LPBYTE)this), 0, sizeof(*this)-((LPBYTE)&member-(LPBYTE)this))

//#define PRINT_ALWAYS 1
#if _DEBUG || PRINT_ALWAYS

void Print(STR s);
void Printf(STR frm, ...);

inline void Print(LPCSTR s) { Printf(L"%S", s); }
inline void Print(const std::wstring& s) { Print(s.c_str()); }
inline void Print(int i) { Printf(L"%i", i); }
inline void Print(unsigned int i) { Print((int)i); }
inline void Print(long i) { Print((int)i); }
inline void Print(unsigned long i) { Print((int)i); }
inline void Print(__int64 i) { Printf(L"%I64i", i); }
inline void Print(unsigned __int64 i) { Print((__int64)i); }
inline void Print(void* i) { Printf(sizeof(void*) == 8 ? L"%I64i" : L"%i", i); }

inline void Printx(int i) { Printf(L"0x%X", i); }
inline void Printx(unsigned int i) { Printx((int)i); }
inline void Printx(long i) { Printx((int)i); }
inline void Printx(unsigned long i) { Printx((int)i); }
inline void Printx(__int64 i) { Printf(L"0x%I64X", i); }

#define PRINTI(x) Printf(L"debug: " __FILEW__ "(" _CRT_STRINGIZE(__LINE__) "):  %i", x)
#define PRINTS(x) Printf(L"debug: " __FILEW__ "(" _CRT_STRINGIZE(__LINE__) "):  %s", x)
#define PRINTX(x) Printf(L"debug: " __FILEW__ "(" _CRT_STRINGIZE(__LINE__) "):  0x%X", x)
#define PRINTF(formatString, ...) Printf(L"debug: " __FILEW__ "(" _CRT_STRINGIZE(__LINE__) "):  " formatString, __VA_ARGS__)
#define PRINTF_IF(condition, formatString, ...) { if(condition) PRINTF(formatString, __VA_ARGS__); }

inline void PrintComRefCount(IUnknown* u) {
	if(u) {
		u->AddRef();
		int i = u->Release();
		Printf(L"%p  %i", u, i);
	} else Print(L"null");
}

#else
#define Print __noop
#define Printf __noop
#define Printx __noop
#define PRINT __noop
#define PRINTX __noop
#define PRINTF __noop
#define PRINTF_IF __noop
#define PrintComRefCount __noop
#endif

#if _DEBUG || PRINT_ALWAYS

struct Perf_Inst
{
private:
	int _counter;
	bool _incremental;
	int _nMeasurements; //used with incremental to display n measurements and average times
	__int64 _time0;
	static const int _nElem = 16;
	__int64 _a[_nElem];
	wchar_t _aMark[_nElem];

public:
	//Perf_Inst() noexcept { ZEROTHIS; } //not used because then does not work shared data section
	Perf_Inst() {}
	Perf_Inst(bool isLocal) { if(isLocal) ZEROTHIS; }

	void First();
	void Next(char cMark = '\0');
	void Write();

	void NW(char cMark = '\0') { Next(cMark); Write(); }

	void SetIncremental(bool yes)
	{
		if(_incremental = yes) {
			for(int i = 0; i < _nElem; i++) _a[i] = 0;
			_nMeasurements = 0;
		}
	}
};

extern Perf_Inst Perf;

#endif

#include "str.h"


bool IsOS64Bit();
bool IsProcess64Bit(DWORD pid, out bool& is);

inline bool IsThisProcess64Bit()
{
#ifdef _WIN64
	return true;
#else
	return false;
#endif
}

//SECURITY_ATTRIBUTES that allows UAC low integrity level processes to open the kernel object.
//can instead use CSecurityAttributes/CSecurityDesc, but this added before including ATL, and don't want to change now.
class SecurityAttributes
{
	DWORD nLength;
	LPVOID lpSecurityDescriptor;
	BOOL bInheritHandle;
public:

	SecurityAttributes()
	{
		nLength = sizeof(SecurityAttributes);
		bInheritHandle = false;
		lpSecurityDescriptor = null;
		BOOL ok = ConvertStringSecurityDescriptorToSecurityDescriptorW(L"D:NO_ACCESS_CONTROLS:(ML;;NW;;;LW)", 1, &lpSecurityDescriptor, null);
		assert(ok);
	}

	~SecurityAttributes()
	{
		LocalFree(lpSecurityDescriptor);
	}

	//SECURITY_ATTRIBUTES* operator&() {
	//	return (SECURITY_ATTRIBUTES*)this;
	//}

	static SECURITY_ATTRIBUTES* Common()
	{
		static SecurityAttributes s_sa;
		return (SECURITY_ATTRIBUTES*)&s_sa;
	}
};

class AutoReleaseMutex
{
	HANDLE _mutex;
public:
	AutoReleaseMutex(HANDLE mutex) noexcept {
		_mutex = mutex;
	}

	~AutoReleaseMutex() {
		if(_mutex) ReleaseMutex(_mutex);
	}

	void ReleaseNow() {
		if(_mutex) ReleaseMutex(_mutex);
		_mutex = 0;
	}
};

//currently not used.
////can instead use CAtlFileMappingBase from atlfile.h, but this added before including ATL, and don't want to change now.
//class SharedMemory
//{
//	HANDLE _hmapfile;
//	LPBYTE _mem;
//public:
//	SharedMemory() { _hmapfile = 0; _mem = 0; }
//	~SharedMemory() { Close(); }
//
//	bool Create(STR name, DWORD size)
//	{
//		Close();
//		_hmapfile = CreateFileMappingW((HANDLE)(-1), SecurityAttributes::Common(), PAGE_READWRITE, 0, size, name);
//		if(!_hmapfile) return false;
//		_mem = (LPBYTE)MapViewOfFile(_hmapfile, FILE_MAP_ALL_ACCESS, 0, 0, 0);
//		return _mem != null;
//	}
//
//	bool Open(STR name)
//	{
//		Close();
//		_hmapfile = OpenFileMappingW(FILE_MAP_ALL_ACCESS, 0, name);
//		if(!_hmapfile) return false;
//		_mem = (LPBYTE)MapViewOfFile(_hmapfile, FILE_MAP_ALL_ACCESS, 0, 0, 0);
//		return _mem != null;
//	}
//
//	void Close()
//	{
//		if(_mem) { UnmapViewOfFile(_mem); _mem = 0; }
//		if(_hmapfile) { CloseHandle(_hmapfile); _hmapfile = 0; }
//	}
//
//	LPBYTE Mem() { return _mem; }
//
//	bool Is0() { return _mem == null; }
//};

//currently not used.
//template<class T>
//class AutoResetVariable
//{
//	T* _b;
//public:
//	AutoResetVariable(T* b, T value) { _b = b; *b = value; }
//	~AutoResetVariable() { *_b = 0; }
//};

//IStream helpers.
class istream
{
public:
	static LARGE_INTEGER LI(__int64 i) {
		LARGE_INTEGER r; r.QuadPart = i;
		return r;
	}

	static ULARGE_INTEGER ULI(__int64 i) {
		ULARGE_INTEGER r; r.QuadPart = i;
		return r;
	}

	static bool ResetPos(IStream* x) {
		return 0 == x->Seek(LI(0), STREAM_SEEK_SET, null);
	}

	static bool GetPos(IStream* x, out DWORD& pos) {
		pos = 0;
		__int64 pos64;
		if(x->Seek(LI(0), STREAM_SEEK_CUR, (ULARGE_INTEGER*)&pos64)) return false;
		pos = (DWORD)pos64;
		return true;
	}

	static bool GetSize(IStream* x, out DWORD& size) {
		size = 0;
		STATSTG stat;
		if(x->Stat(&stat, STATFLAG_NONAME)) return false;
		size = stat.cbSize.LowPart;
		return true;
	}

	static bool Clear(IStream* x) {
		return 0 == x->SetSize(ULI(0));
	}
};

namespace wnd
{
bool ClassName(HWND w, out Bstr& s);
int ClassNameIs(HWND w, std::initializer_list<STR> a);
inline bool ClassNameIs(HWND w, STR s) { return 0 != ClassNameIs(w, { s }); }
bool Name(HWND w, out Bstr& s);

using WNDENUMPROCL = const std::function <bool(HWND c)>;

BOOL EnumChildWindows(HWND w, WNDENUMPROCL& callback);
HWND FindChildByClassName(HWND w, STR className, bool visible);

#if _DEBUG || PRINT_ALWAYS
void PrintWnd(HWND w);
#else
#define PrintWnd __noop
#endif
}

bool QueryService_(IUnknown* iFrom, OUT void** iTo, REFIID iid, const GUID* guidService=null);

template<class T>
bool QueryService(IUnknown* iFrom, OUT T** iTo, const GUID* guidService = null) {
	return QueryService_(iFrom, (void**)iTo, __uuidof(T), guidService);
}

namespace util
{
//Swaps values of variables a and b: <c>T t = a; a = b; b = t;</c>
template<class T>
void Swap(ref T& a, ref T& b)
{
	T t = a; a = b; b = t;
}

}