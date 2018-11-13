#pragma once

#include "stdafx.h"


#if true //TODO
void Print(LPCWSTR frm, ...)
{
	if(!frm) frm = L"";
	wchar_t s[1028];
	wvsprintfW(s, frm, (va_list)(&frm + 1));
	HWND w = FindWindowW(L"QM_Editor", nullptr);
	SendMessageW(w, WM_SETTEXT, -1, (LPARAM)s);
}
#else
#define Print __noop
#endif

class _SecurityAttributes :public SECURITY_ATTRIBUTES
{
public:
	_SecurityAttributes(LPCWSTR securityDescriptor)
	{
		nLength = sizeof(SECURITY_ATTRIBUTES);
		if(!ConvertStringSecurityDescriptorToSecurityDescriptor(securityDescriptor, 1, &lpSecurityDescriptor, nullptr))
			Print(L"SECURITY_ATTRIBUTES: %i", GetLastError());
	}

	~_SecurityAttributes() {  LocalFree(lpSecurityDescriptor); }
};
