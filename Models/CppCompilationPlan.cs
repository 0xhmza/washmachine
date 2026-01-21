namespace Washmachine.Models;

public sealed class CppCompilationPlan
{
    public List<string> AntiDebuggingSnippets { get; } = new();
    public List<string> GuardrailSnippets { get; } = new();
    public Dictionary<string, List<string>> CustomSnippetBlocks { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool UsesGenericShellcode { get; set; }

    public string? EncodedShellcodeSnippet { get; set; }
    public string? UrlShellcodeSnippet { get; set; }
    public string? GenericShellcodeSnippet { get; set; }
    public string? UacBypassSnippet { get; set; }
    public string? ProcessInjectionSnippet { get; set; }
    public string? ShellcodeExecutionSnippet { get; set; }
    public string? ProcessLookupHelper { get; set; }
}
