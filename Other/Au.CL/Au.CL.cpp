//This small C++ app is used to run a script in Au.Editor.exe through command line.
//Au.Editor.exe itself does not support it through its command line directly, because:
//	1. Its process starts slowly because is a .NET app.
//	2. If the caller process waits until script ends, it would have to wait until Au.Editor.exe exits.

#include "stdafx.h"
#include "Au.CL.h"

struct TaskResults
{
	HANDLE hPipe, hOut;

	TaskResults() { hPipe = 0; hOut = 0; };

	bool Init(LPWSTR& cmdLine)
	{
		hOut = GetStdHandle(STD_OUTPUT_HANDLE);
		if(hOut == 0) { //cmd.exe? Note: in cmd execute this to change cmd console code page to UTF-8: chcp 65001
			AttachConsole(ATTACH_PARENT_PROCESS);
			hOut = GetStdHandle(STD_OUTPUT_HANDLE);
		}
		if(hOut == 0) return false;

		DWORD tid = GetCurrentThreadId();
		wchar_t pipeName[30] = LR"(\\.\pipe\Au.CL-)"; _itow(tid, pipeName + 15, 10);
		_SecurityAttributes sa(L"D:(A;;0x12019b;;;AU)"); //like of PipeSecurity that allows ReadWrite for AuthenticatedUserSid
		hPipe = CreateNamedPipeW(pipeName,
			PIPE_ACCESS_INBOUND | FILE_FLAG_OVERLAPPED, //use async pipe because also need to wait for task process exit
			PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_REJECT_REMOTE_CLIENTS,
			1, 0, 0, 0, &sa);
		if(hPipe == INVALID_HANDLE_VALUE) { Print(L"%i", GetLastError()); return false; }

		//cmdLine = pipeName + " " + cmdLine
		int len1 = wcslen(pipeName), len2 = wcslen(cmdLine);
		LPWSTR t = (LPWSTR)malloc(2 * (len1 + len2 + 2));
		wcscpy(t, pipeName); t[len1] = ' '; wcscpy(t + len1 + 1, cmdLine);
		cmdLine = t;

		return true;
	}

	bool WaitAndRead(HANDLE hProcess)
	{
		bool R = false;
		HANDLE ev = CreateEventW(nullptr, true, false, nullptr);
		const int bLen = 5000; wchar_t b16[bLen]; char b8[bLen * 3];
		for(;;) {
			OVERLAPPED o = { }; o.hEvent = ev;
			if(!ConnectNamedPipe(hPipe, &o)) {
				int e = GetLastError(); if(e != ERROR_IO_PENDING) { Print(L"ConnectNamedPipe: %i", e); break; }
				HANDLE ha[2] = { ev, hProcess };
				int wr = WaitForMultipleObjects(2, ha, false, INFINITE);
				if(wr != 0) { CancelIo(hPipe); R = true; break; } //task ended
				DWORD u1 = 0; if(!GetOverlappedResult(hPipe, &o, &u1, false)) { Print(L"GetOverlappedResult: %i", GetLastError()); break; }
			}

			BOOL ok; DWORD n;
			while(((ok = ReadFile(hPipe, b16, sizeof(b16), &n, nullptr)) || (GetLastError() == ERROR_MORE_DATA)) && n > 0) {
				n = WideCharToMultiByte(CP_UTF8, 0, b16, n / 2, b8, sizeof(b8), nullptr, nullptr);
				WriteFile(hOut, b8, n, nullptr, nullptr);
				if(ok) break;
				//note: MSDN says must use OVERLAPPED with ReadFile too, but works without it.
			}
			if(!ok) Print(L"ReadFile: n=%i, err=%i", n, GetLastError());
			DisconnectNamedPipe(hPipe);
			if(!ok) break;
		}
		CloseHandle(ev);
		CloseHandle(hPipe);
		return R;
	}
};

enum ERunResult
{
	//the first 4 are as in AuTask.ERunResult.
	//we return these codes -1:
	failed = 0,
	deferred = -1,
	notFound = -2,
	editorThread = -3,
	//or we return:
	noEditor = -10,
	cannotWait = -11,
	cannotGetResult = -12,
};

int APIENTRY wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPWSTR lpCmdLine, int nCmdShow)
{
	//Find editor's message-only window. Start editor if need.
	HWND w = 0;
	PROCESS_INFORMATION pi = {};
	for(int i = 0; i < 1000; i++) { //if we started editor process, wait until it fully loaded, then it creates the message-only window
		w = FindWindowW(L"Au.Editor.Msg", nullptr);
		if(w) break;
		if(i == 0) {
			//get path of this exe and replace filename with that of editor's
			wchar_t s[1000];
			int k = GetModuleFileNameW(0, s, 1000); if(k < 4 || k > 1000 - 20) return noEditor;
			for(--k; k > 0; k--) if(s[k] == '\\') break;
			LPCWSTR ed = L"Au.Editor.exe"; for(int j = 0; j < 14; ) s[++k] = ed[j++];

			//run editor
			STARTUPINFOW x = { sizeof(x) };
			if(!CreateProcessW(nullptr, s, nullptr, nullptr, false, 0, nullptr, nullptr, &x, &pi)) return noEditor;
			CloseHandle(pi.hThread);
		}
		if(WaitForSingleObject(pi.hProcess, 15) != WAIT_TIMEOUT) return noEditor;
	}
	if(pi.hProcess) CloseHandle(pi.hProcess);
	if(!w) return noEditor;

	int mode = 0; //1 - wait, 3 - wait and get AuTask.WriteResult output
	TaskResults tr;
	if(*lpCmdLine == '*') {
		mode = 1;
		if(*(++lpCmdLine) == '*') {
			if(tr.Init(++lpCmdLine)) mode = 3;
			else return cannotGetResult;
		}
	}

	//this code is like in AuTask.cs

	COPYDATASTRUCT d;
	d.dwData = 99;
	d.cbData = wcslen(lpCmdLine) * 2;
	d.lpData = (PVOID)lpCmdLine;
	int pid = (int)SendMessageW(w, WM_COPYDATA, mode, (LPARAM)&d);

	switch(pid) {
	case editorThread: if(!(mode & 2)) break; //valid except when need to get AuTask.WriteResult output
	case failed: case deferred: case notFound: return pid - 1;
	}

	if(mode & 1) {
		HANDLE hProcess = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
		if(hProcess == 0) return cannotWait;

		int err = 0;
		if(mode & 2) {
			if(!tr.WaitAndRead(hProcess)) err = cannotGetResult;
		} else {
			if(0 != WaitForSingleObject(hProcess, INFINITE)) err = cannotWait;
		}

		if(err == 0 && !GetExitCodeProcess(hProcess, (DWORD*)&pid)) pid = INT_MIN;
		CloseHandle(hProcess);
		if(err) return err;
	}

	return pid;
}
