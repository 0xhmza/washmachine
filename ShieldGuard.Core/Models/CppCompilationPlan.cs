namespace ShieldGuard.Models;

public sealed class CppCompilationPlan
{
    public List<string> AntiDebuggingSnippets { get; } = new();
    public List<string> LicenseGuardrailSnippets { get; } = new();
    public Dictionary<string, List<string>> CustomSnippetBlocks { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tracks which snippet items were selected, keyed by the snippet section's
    /// template id (e.g. "uacb", "persistence", "evasion"). Each value is the
    /// ordered list of selected item IDs for that section. Used by the
    /// requires-contract validation to confirm that snippet dependencies
    /// (e.g. <c>requires: [uac_bypass]</c>) are satisfied with a non-stub pick.
    /// </summary>
    public Dictionary<string, List<string>> SelectedSnippets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Collected <c>#include</c> lines from selected snippets. Deduplicated before rendering.
    /// </summary>
    public List<string> SnippetIncludes { get; } = new();

    /// <summary>
    /// Collected function implementations from selected snippets. Placed before <c>main()</c>.
    /// </summary>
    public List<string> SnippetImplementations { get; } = new();

    public bool UsesGenericLicenseStub { get; set; }
    public bool UsesWebLicensePayload { get; set; }

    public string? EncodedLicenseStubSnippet { get; set; }
    public string? UrlLicenseStubSnippet { get; set; }
    public string? GenericLicenseStubSnippet { get; set; }
    public string? UacBypassSnippet { get; set; }
    public string? ProcessInjectionSnippet { get; set; }
    public string? LicenseStubExecutionSnippet { get; set; }
    public string? ProcessLookupHelper { get; set; }

    /// <summary>
    /// Assembled C++ code block from Bin2Shell web mode output (includes, declarations,
    /// fetch helper, licensepayload init, decode). Set when the user completes the web licensepayload wizard.
    /// </summary>
    public string? WebLicensePayloadCodeBlock { get; set; }

    /// <summary>
    /// File-scope preamble from web licensepayload (#includes + fetch helper function).
    /// Must be injected before main().
    /// </summary>
    public string? WebLicensePayloadPreamble { get; set; }
}
