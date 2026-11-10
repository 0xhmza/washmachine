using ShieldGuard.Models;

namespace ShieldGuard.Services;

/// <summary>
/// Parses the YAML bundle emitted by <c>bin2license -w</c> into a <see cref="Bin2ShellWebOutput"/>.
/// Handles two output formats:
/// <list type="bullet">
///   <item>New format: separate top-level keys (cpp_includes, cpp_declarations, etc.)</item>
///   <item>Legacy format: single <c>code_template:</c> block with all C++ code concatenated</item>
/// </list>
/// </summary>
public static class Bin2ShellWebOutputParser
{
    private static readonly string[] TopLevelKeys =
    {
        "cpp_includes:",
        "cpp_declarations:",
        "cpp_web_fetch:",
        "cpp_licensepayload_init:",
        "cpp_decode:",
        "licensepayload:",
        "licensepayload_checksum:",
        "options:",
        "code_template:"
    };

    public static Bin2ShellWebOutput Parse(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            throw new InvalidOperationException("Bin2Shell returned empty YAML output.");

        var sections = SplitTopLevelSections(yaml);

        // Legacy format: single code_template block
        if (sections.ContainsKey("code_template") && !sections.ContainsKey("cpp_includes"))
        {
            return ParseLegacyFormat(sections);
        }

        return ParseNewFormat(sections);
    }

    private static Bin2ShellWebOutput ParseNewFormat(Dictionary<string, string> sections)
    {
        var result = new Bin2ShellWebOutput();

        result.CppIncludes = RepairCppEscapeSequences(GetBlockContent(sections, "cpp_includes"));
        result.CppDeclarations = RepairCppEscapeSequences(GetBlockContent(sections, "cpp_declarations"));
        result.CppWebFetch = RepairCppEscapeSequences(GetBlockContent(sections, "cpp_web_fetch"));
        result.CppLicensePayloadInit = RepairCppEscapeSequences(GetBlockContent(sections, "cpp_licensepayload_init"));
        result.CppDecode = RepairCppEscapeSequences(GetBlockContent(sections, "cpp_decode"));
        result.LicensePayload = GetBlockContent(sections, "licensepayload");

        // licensepayload_checksum may be top-level or nested under options.
        if (sections.TryGetValue("licensepayload_checksum", out var checksumBlock))
        {
            result.LicensePayloadChecksum = ParseNestedValue(checksumBlock, "value");
        }

        if (sections.TryGetValue("options", out var optionsBlock))
        {
            result.LicensePayloadLen = ParseNestedInt(optionsBlock, "licensepayload_len");
            if (string.IsNullOrEmpty(result.LicensePayloadChecksum))
                result.LicensePayloadChecksum = ParseNestedChecksum(optionsBlock);
        }

        return result;
    }

    private static Bin2ShellWebOutput ParseLegacyFormat(Dictionary<string, string> sections)
    {
        var result = new Bin2ShellWebOutput();

        string codeBlock = GetBlockContent(sections, "code_template");

        // The bundled bin2license embeds C++ code inside a Python non-raw string,
        // so escape sequences like \r \n \0 become actual control characters.
        // Repair them back to C escape sequences.
        codeBlock = RepairCppEscapeSequences(codeBlock);

        result.CppIncludes = codeBlock;
        result.LicensePayload = GetBlockContent(sections, "licensepayload");

        if (sections.TryGetValue("licensepayload_checksum", out var checksumBlock))
        {
            result.LicensePayloadChecksum = ParseNestedValue(checksumBlock, "value");
        }

        if (sections.TryGetValue("options", out var optionsBlock))
        {
            result.LicensePayloadLen = ParseNestedInt(optionsBlock, "licensepayload_len");
            if (string.IsNullOrEmpty(result.LicensePayloadChecksum))
                result.LicensePayloadChecksum = ParseNestedChecksum(optionsBlock);
        }

        return result;
    }

    /// <summary>
    /// The bundled bin2license uses a Python non-raw triple-quoted string for C++ code,
    /// so <c>\r</c>, <c>\n</c>, and <c>\0</c> inside C string literals become actual
    /// control characters. This repairs them back to C escape sequences.
    /// </summary>
    private static string RepairCppEscapeSequences(string code)
    {
        if (string.IsNullOrEmpty(code))
            return code;

        var sb = new System.Text.StringBuilder(code.Length + 256);
        bool inStringLiteral = false;
        bool inCharLiteral = false;
        char prev = '\0';

        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];

            // Track whether we are inside a C/C++ string or char literal.
            if (!inCharLiteral && c == '"' && prev != '\\')
            {
                // Check for L" prefix
                inStringLiteral = !inStringLiteral;
                sb.Append(c);
                prev = c;
                continue;
            }

            if (!inStringLiteral && c == '\'' && prev != '\\')
            {
                inCharLiteral = !inCharLiteral;
                sb.Append(c);
                prev = c;
                continue;
            }

            if (inStringLiteral || inCharLiteral)
            {
                // Replace actual control characters with C escape sequences.
                switch (c)
                {
                    case '\0':
                        sb.Append("\\0");
                        prev = '0';
                        continue;
                    case '\r':
                        // Check if followed by \n (CRLF).
                        if (i + 1 < code.Length && code[i + 1] == '\n')
                        {
                            sb.Append("\\r\\n");
                            i++;
                            prev = 'n';
                            continue;
                        }
                        sb.Append("\\r");
                        prev = 'r';
                        continue;
                    case '\n':
                        sb.Append("\\n");
                        prev = 'n';
                        continue;
                    case '\t':
                        sb.Append("\\t");
                        prev = 't';
                        continue;
                }
            }

            sb.Append(c);
            prev = c;
        }

        return sb.ToString();
    }

    private static Dictionary<string, string> SplitTopLevelSections(string yaml)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = yaml.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        string? currentKey = null;
        var currentLines = new List<string>();
        int blockIndent = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Check if this is a top-level key (starts at column 0)
            string? matchedKey = null;
            if (trimmed.Length > 0 && line.Length == trimmed.Length)
            {
                foreach (var key in TopLevelKeys)
                {
                    if (trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedKey = key.TrimEnd(':');
                        break;
                    }
                }
            }

            if (matchedKey != null)
            {
                if (currentKey != null)
                    sections[currentKey] = JoinBlockLines(currentLines);

                currentKey = matchedKey;
                currentLines.Clear();
                blockIndent = -1;
                continue;
            }

            if (currentKey != null)
            {
                if (blockIndent < 0 && trimmed.Length > 0)
                {
                    blockIndent = line.Length - trimmed.Length;
                }

                // Only include lines that are part of the indented block.
                if (blockIndent > 0 && trimmed.Length > 0 && (line.Length - trimmed.Length) < blockIndent)
                {
                    // Unindented non-empty line that didn't match a top-level key;
                    // treat it as end of the current block.
                    sections[currentKey] = JoinBlockLines(currentLines);
                    currentKey = null;
                    continue;
                }

                if (blockIndent >= 0 && line.Length > blockIndent)
                {
                    currentLines.Add(line[blockIndent..]);
                }
                else
                {
                    // Empty or short line inside the block — preserve as empty.
                    currentLines.Add(string.Empty);
                }
            }
        }

        if (currentKey != null)
            sections[currentKey] = JoinBlockLines(currentLines);

        return sections;
    }

    private static string JoinBlockLines(List<string> lines)
    {
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        return string.Join("\n", lines);
    }

    private static string GetBlockContent(Dictionary<string, string> sections, string key)
    {
        return sections.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static int ParseNestedInt(string block, string key)
    {
        foreach (var line in block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
            {
                var valuePart = trimmed[(key.Length + 1)..].Trim();
                if (int.TryParse(valuePart, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int result))
                    return result;
            }
        }
        return 0;
    }

    private static string ParseNestedValue(string block, string key)
    {
        foreach (var line in block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(key.Length + 1)..].Trim();
            }
        }
        return string.Empty;
    }

    private static string ParseNestedChecksum(string block)
    {
        bool inChecksum = false;
        foreach (var line in block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("licensepayload_checksum:", StringComparison.OrdinalIgnoreCase))
            {
                inChecksum = true;
                continue;
            }

            if (inChecksum && trimmed.StartsWith("value:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[6..].Trim();
            }

            if (inChecksum && !line.StartsWith(" ") && !line.StartsWith("\t") && trimmed.Length > 0)
                break;
        }
        return string.Empty;
    }
}
