// ============================================================================
// wash_proc_lookup.h — process & thread lookup helper
//
// Provides GetProcessOrThreadId(), used by the {{PROCESS_LOOKUP_HELPER}}
// template placeholder when a Process Injection snippet has been selected.
//
// Emitted into the generated C++ source verbatim by CompilerService.
// Lives next to default.yaml so it can be edited, syntax-highlighted and
// linted independently from the C# build orchestrator.
// ============================================================================

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <string>
#include <algorithm>

DWORD GetProcessOrThreadId(const std::wstring& processName, bool returnProcessId)
{
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE)
        return 0;

    PROCESSENTRY32W entry{};
    entry.dwSize = sizeof(entry);

    DWORD result = 0;

    if (Process32FirstW(snapshot, &entry))
    {
        do
        {
            std::wstring exe = entry.szExeFile;
            std::wstring exeLower = exe;
            std::wstring targetLower = processName;
            std::transform(exeLower.begin(), exeLower.end(), exeLower.begin(), ::towlower);
            std::transform(targetLower.begin(), targetLower.end(), targetLower.begin(), ::towlower);

            if (exeLower == targetLower)
            {
                if (returnProcessId)
                {
                    result = entry.th32ProcessID;
                }
                else
                {
                    HANDLE threadSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
                    if (threadSnapshot != INVALID_HANDLE_VALUE)
                    {
                        THREADENTRY32 threadEntry{};
                        threadEntry.dwSize = sizeof(threadEntry);
                        if (Thread32First(threadSnapshot, &threadEntry))
                        {
                            do
                            {
                                if (threadEntry.th32OwnerProcessID == entry.th32ProcessID)
                                {
                                    result = threadEntry.th32ThreadID;
                                    break;
                                }
                            } while (Thread32Next(threadSnapshot, &threadEntry));
                        }

                        CloseHandle(threadSnapshot);
                    }
                }

                break;
            }
        } while (Process32NextW(snapshot, &entry));
    }

    CloseHandle(snapshot);
    return result;
}
