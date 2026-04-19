using System.Text.RegularExpressions;

namespace Washmachine.Services;

public enum MemoryProtectionMode
{
    Unknown,
    ReadOnly,
    ReadWrite,
    ReadExecute,
    ReadWriteExecute,
}

public sealed record ExecutionSnippetAnalysis(
    string SnippetId,
    string Snippet,
    MemoryProtectionMode FinalProtection,
    IReadOnlyList<string> DetectedApis,
    bool HasSelfModifyingSafeMemory,
    string Summary)
{
    public bool IsSgnCompatible => HasSelfModifyingSafeMemory;
}

/// <summary>
/// Lightweight regex-based scanner for shellcode-execution snippets. Answers:
/// "will this memory region be writable at execution time, so SGN's self-modifying
/// decoder stub can run?" Works on any user-supplied snippet, not just the bundled catalog.
/// </summary>
public static class ExecutionSnippetAnalyzer
{
    private static readonly Regex VirtualProtectRx = new(
        @"VirtualProtect(?:Ex)?\s*\([^;]*?,(?:[^;]*?,){1,3}\s*(PAGE_[A-Z_]+)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex VirtualAllocProtRx = new(
        @"VirtualAlloc(?:Ex)?\s*\([^;]*?,(?:[^;]*?,){2,3}\s*(PAGE_[A-Z_]+|0x[0-9A-Fa-f]+)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex NtMapViewRx = new(
        @"NtMapViewOfSection\s*\([^;]*?,(?:[^;]*?,){8}\s*(PAGE_[A-Z_]+)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex ApiRx = new(
        @"\b(VirtualAlloc(?:Ex)?|VirtualProtect(?:Ex)?|HeapAlloc|CreateThread|NtCreateThreadEx|NtMapViewOfSection|NtCreateSection|CreateFiber|EnumWindows|CreateThreadpoolWork|QueueUserAPC|WriteProcessMemory|CreateRemoteThread|memcpy|SecureZeroMemory)\b",
        RegexOptions.Compiled);

    public static ExecutionSnippetAnalysis Analyze(string snippetId, string snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return new ExecutionSnippetAnalysis(
                snippetId ?? "",
                snippet ?? "",
                MemoryProtectionMode.Unknown,
                Array.Empty<string>(),
                HasSelfModifyingSafeMemory: false,
                Summary: "Empty or missing snippet.");
        }

        var apis = ApiRx
            .Matches(snippet)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        var protectionCalls = new List<string>();
        foreach (Match m in VirtualProtectRx.Matches(snippet))
            protectionCalls.Add(m.Groups[1].Value);
        foreach (Match m in NtMapViewRx.Matches(snippet))
            protectionCalls.Add(m.Groups[1].Value);

        MemoryProtectionMode finalProtection = MemoryProtectionMode.Unknown;

        if (protectionCalls.Count > 0)
        {
            finalProtection = ClassifyProtection(protectionCalls[^1]);
        }
        else
        {
            foreach (Match m in VirtualAllocProtRx.Matches(snippet))
            {
                var mode = ClassifyProtection(m.Groups[1].Value);
                if (mode != MemoryProtectionMode.Unknown)
                    finalProtection = mode;
            }
        }

        bool hasSafeMemory = finalProtection == MemoryProtectionMode.ReadWriteExecute;

        var summary = finalProtection switch
        {
            MemoryProtectionMode.ReadWriteExecute =>
                "RWX execution memory — safe for SGN (self-modifying decoder).",
            MemoryProtectionMode.ReadExecute =>
                "RX-only execution memory — SGN's self-modifying decoder will fault.",
            MemoryProtectionMode.ReadWrite =>
                "RW-only — shellcode cannot execute here. Check the snippet.",
            MemoryProtectionMode.ReadOnly =>
                "Read-only memory — shellcode will not execute.",
            _ =>
                "Memory protection could not be determined statically.",
        };

        return new ExecutionSnippetAnalysis(
            snippetId ?? "",
            snippet,
            finalProtection,
            apis,
            hasSafeMemory,
            summary);
    }

    private static MemoryProtectionMode ClassifyProtection(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return MemoryProtectionMode.Unknown;

        var t = token.Trim().ToUpperInvariant();

        if (t == "PAGE_EXECUTE_READWRITE" || t == "0X40" || t == "0X40U")
            return MemoryProtectionMode.ReadWriteExecute;
        if (t == "PAGE_EXECUTE_READ" || t == "0X20" || t == "0X20U")
            return MemoryProtectionMode.ReadExecute;
        if (t == "PAGE_EXECUTE" || t == "0X10" || t == "0X10U")
            return MemoryProtectionMode.ReadExecute;
        if (t == "PAGE_READWRITE" || t == "0X04" || t == "0X4")
            return MemoryProtectionMode.ReadWrite;
        if (t == "PAGE_READONLY" || t == "0X02" || t == "0X2")
            return MemoryProtectionMode.ReadOnly;

        return MemoryProtectionMode.Unknown;
    }
}
