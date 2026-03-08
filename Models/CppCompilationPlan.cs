namespace Washmachine.Models;

public sealed class CppCompilationPlan
{
    public List<string> AntiDebuggingSnippets { get; } = new();
    public List<string> GuardrailSnippets { get; } = new();
    public Dictionary<string, List<string>> CustomSnippetBlocks { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Collected <c>#include</c> lines from selected snippets. Deduplicated before rendering.
    /// </summary>
    public List<string> SnippetIncludes { get; } = new();

    /// <summary>
    /// Collected function implementations from selected snippets. Placed before <c>main()</c>.
    /// </summary>
    public List<string> SnippetImplementations { get; } = new();

    public bool UsesGenericShellcode { get; set; }
    public bool UsesWebPayload { get; set; }

    public string? EncodedShellcodeSnippet { get; set; }
    public string? UrlShellcodeSnippet { get; set; }
    public string? GenericShellcodeSnippet { get; set; }
    public string? UacBypassSnippet { get; set; }
    public string? ProcessInjectionSnippet { get; set; }
    public string? ShellcodeExecutionSnippet { get; set; }
    public string? ProcessLookupHelper { get; set; }

    /// <summary>
    /// Assembled C++ code block from Bin2Shell web mode output (includes, declarations,
    /// fetch helper, payload init, decode). Set when the user completes the web payload wizard.
    /// </summary>
    public string? WebPayloadCodeBlock { get; set; }

    /// <summary>
    /// File-scope preamble from web payload (#includes + fetch helper function).
    /// Must be injected before main().
    /// </summary>
    public string? WebPayloadPreamble { get; set; }
}
