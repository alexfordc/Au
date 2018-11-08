//This small C++ app is used to run a script in Au.Editor.exe through command line.
//Au.Editor.exe itself does not support it through its command line directly, because:
//	1. Its process starts slowly because is a .NET app.
//	2. If the caller process waits until script ends, it would have to wait until Au.Editor.exe exits.

#include "stdafx.h"
#include "Au.CL.h"

#if false
void Print(LPCWSTR frm, ...)
{
	if(!frm) frm = L"";
	wchar_t s[1028];
	wvsprintfW(s, frm, (va_list)(&frm + 1));
	HWND w = FindWindowW(L"QM_Editor", nullptr);
	SendMessageW(w, WM_SETTEXT, -1, (LPARAM)s);
}
#endif

int APIENTRY wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPWSTR lpCmdLine, int nCmdShow)
{
	//Find editor's message-only window. Start editor if need.
	HWND w = 0;
	PROCESS_INFORMATION pi = {};
	for(int i = 0; i < 1000; i++) {
		w = FindWindowW(L"Au.Editor.Msg", nullptr);
		if(w) break;
		if(i == 0) {
			//get path of this exe and replace filename with that of editor's
			wchar_t s[1000];
			int k = GetModuleFileNameW(0, s, 1000); if(k < 4 || k > 1000 - 20) return 1;
			for(--k; k > 0; k--) if(s[k] == '\\') break;
			LPCWSTR ed = L"Au.Editor.exe"; for(int j = 0; j < 14; ) s[++k] = ed[j++];

			//run editor
			STARTUPINFOW x = { sizeof(x) };
			if(!CreateProcessW(nullptr, s, nullptr, nullptr, false, 0, nullptr, nullptr, &x, &pi)) return 1;
			CloseHandle(pi.hThread);
		}
		if(WaitForSingleObject(pi.hProcess, 15) != WAIT_TIMEOUT) return 1;
	}
	if(pi.hProcess) CloseHandle(pi.hProcess);
	if(!w) return 1;

	COPYDATASTRUCT d;
	d.dwData = 99;
	d.cbData = wcslen(lpCmdLine) * 2;
	d.lpData = lpCmdLine;
	switch(SendMessageW(w, WM_COPYDATA, 1, (LPARAM)&d)) {
	case 1: break; //success
	case 2: return 2; //script not found
	default: return 1; //if 0, somehow the window did not receive the message
	}

	return 0;
}
