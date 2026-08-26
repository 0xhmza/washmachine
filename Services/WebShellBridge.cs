using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using Washmachine.Logging;
using Washmachine.Models;
using Washmachine.Services;
using Washmachine.Views;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Washmachine.Services;

/// <summary>
/// Owns the JS ↔ C# RPC channel for the WebView2-hosted UI.
///
/// Wire protocol (JSON, one message per call):
///   JS → C#  : { id: <int>, method: <string>, args: <object> }
///   C# → JS  : { id: <int>, result: <object> }      // success
///              { id: <int>, error: <string> }       // failure
///   C# → JS (push): { event: <string>, payload: <object> }
///
/// Methods are resolved by name from a static dispatch table; add a new
/// entry to <see cref="Methods"/> to expose more backend operations.
/// </summary>
public sealed class WebShellBridge
{
    private readonly CoreWebView2 _core;
    private readonly DispatcherQueue _ui;
    private readonly AppPaths _paths;
    private readonly CliExecutor _cli;
    private readonly WebShellWindow? _host;
    private readonly Dictionary<string, Func<JsonElement, Task<object?>>> _methods;
    private readonly DiagnosticsLogger _logger = new();

    private CancellationTokenSource? _buildCts;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public WebShellBridge(CoreWebView2 core, WebShellWindow? host = null)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
        _ui = DispatcherQueue.GetForCurrentThread();
        _paths = new AppPaths();
        _cli = new CliExecutor();
        _host = host;

        _methods = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ping"]                = Method_Ping,
            ["build"]               = Method_Build,
            ["stop"]                = Method_Stop,
            ["dry-run"]             = Method_DryRun,
            ["browse-file"]         = Method_BrowseFile,
            ["browse-folder"]       = Method_BrowseFolder,
            ["read-text"]           = Method_ReadText,
            ["app-info"]            = Method_AppInfo,
            ["list-playbooks"]      = Method_ListPlaybooks,
            ["catalog-encoders"]    = Method_CatalogEncoders,
            ["catalog-envelopes"]   = Method_CatalogEnvelopes,
            ["catalog-compilers"]   = Method_CatalogCompilers,
            ["catalog-snippets"]    = Method_CatalogSnippets,
            ["analyze-shellcode"]   = Method_AnalyzeShellcode,
            ["analyze-pe"]          = Method_AnalyzePe,
            ["history-list"]        = Method_HistoryList,
            ["history-delete"]      = Method_HistoryDelete,
            ["history-clear"]       = Method_HistoryClear,
            ["reveal-file"]         = Method_RevealFile,
            ["open-file"]           = Method_OpenFile,
            ["set-playbook"]        = Method_SetPlaybook,
            ["window-minimize"]     = Method_WindowMinimize,
            ["window-maximize"]     = Method_WindowMaximize,
            ["window-close"]        = Method_WindowClose,
        };

        _core.WebMessageReceived += OnWebMessageReceived;
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string raw = args.TryGetWebMessageAsString();
        _ = HandleAsync(raw);
    }

    private async Task HandleAsync(string raw)
    {
        int id = 0;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
            string method = root.TryGetProperty("method", out var mEl) ? (mEl.GetString() ?? string.Empty) : string.Empty;
            JsonElement argsEl = root.TryGetProperty("args", out var aEl) ? aEl : default;

            if (!_methods.TryGetValue(method, out var handler))
                throw new InvalidOperationException($"Unknown method: {method}");

            var result = await handler(argsEl);
            await PostResultAsync(id, result);
        }
        catch (Exception ex)
        {
            await PostErrorAsync(id, ex.Message);
        }
    }

    /// <summary>Push a server-initiated event to JS (build progress, log lines, etc.).</summary>
    public void PushEvent(string eventName, object? payload = null)
    {
        var msg = new { @event = eventName, payload };
        var json = JsonSerializer.Serialize(msg, JsonOpts);
        // Marshall to UI thread before talking to WebView2
        _ui.TryEnqueue(() =>
        {
            try { _core.PostWebMessageAsJson(json); }
            catch { /* WebView torn down */ }
        });
    }

    private Task PostResultAsync(int id, object? result)
    {
        var json = JsonSerializer.Serialize(new { id, result }, JsonOpts);
        _ui.TryEnqueue(() =>
        {
            try { _core.PostWebMessageAsJson(json); }
            catch { /* WebView torn down */ }
        });
        return Task.CompletedTask;
    }

    private Task PostErrorAsync(int id, string error)
    {
        var json = JsonSerializer.Serialize(new { id, error }, JsonOpts);
        _ui.TryEnqueue(() =>
        {
            try { _core.PostWebMessageAsJson(json); }
            catch { /* WebView torn down */ }
        });
        return Task.CompletedTask;
    }

    // ── Method handlers ────────────────────────────────────────────

    private Task<object?> Method_Ping(JsonElement _) =>
        Task.FromResult<object?>(new { ok = true, ts = DateTimeOffset.UtcNow });

    private async Task<object?> Method_Build(JsonElement args)
    {
        if (!_cli.IsAvailable)
            return new { ok = false, message = "CLI not built — run dotnet build Washmachine.Cli." };

        // Cancel any previous build
        var prevCts = _buildCts;
        prevCts?.Cancel();

        _buildCts = new CancellationTokenSource();
        var ct = _buildCts.Token;

        var cliArgs = new List<string> { "encode" };

        // Source
        string sourceKind = GetStr(args, "sourceKind", "file");
        switch (sourceKind)
        {
            case "raw":
                string hex = GetStr(args, "shellcodeRawInput", "").Replace(" ", "");
                if (string.IsNullOrWhiteSpace(hex))
                    return new { ok = false, message = "No shellcode hex provided." };
                cliArgs.Add("-ShellcodeHex"); cliArgs.Add(hex);
                break;
            case "url":
                string url = GetStr(args, "shellcodeUrlValue", "");
                if (string.IsNullOrWhiteSpace(url))
                    return new { ok = false, message = "No shellcode URL provided." };
                cliArgs.Add("-ShellcodeUrl"); cliArgs.Add(url);
                break;
            default: // file
                string filePath = GetStr(args, "shellcodeFileInput", "");
                if (string.IsNullOrWhiteSpace(filePath))
                    return new { ok = false, message = "No shellcode file path provided." };
                cliArgs.Add("-Shellcode"); cliArgs.Add(filePath);
                break;
        }

        // Template
        string template = GetStr(args, "templateCombo", "");
        if (!string.IsNullOrWhiteSpace(template)) { cliArgs.Add("-Template"); cliArgs.Add(template); }

        // Encoder / Envelope
        string encoder = GetStr(args, "encoderCombo", "");
        if (!string.IsNullOrWhiteSpace(encoder)) { cliArgs.Add("-Encoder"); cliArgs.Add(encoder); }

        string envelope = GetStr(args, "envelopeCombo", "");
        if (!string.IsNullOrWhiteSpace(envelope)) { cliArgs.Add("-Envelope"); cliArgs.Add(envelope); }

        // SGN
        if (GetStr(args, "shikataGaNaiEnabledCheckBox", "False").Equals("True", StringComparison.OrdinalIgnoreCase))
        {
            cliArgs.Add("-Sgn");
            string sgnCount = GetStr(args, "shikataGaNaiEncodeCountInput", "1");
            cliArgs.Add("-SgnCount"); cliArgs.Add(sgnCount);
            string sgnMax = GetStr(args, "shikataGaNaiMaxBytesInput", "50");
            cliArgs.Add("-SgnMax"); cliArgs.Add(sgnMax);
            string sgnPlacement = GetStr(args, "shikataGaNaiPlacement", "pre");
            cliArgs.Add("-SgnPlacement"); cliArgs.Add(sgnPlacement);
        }

        // Snippets: { sectionTemplate: [itemId, ...] | "itemId" }
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("snippetSelections", out var snipSel)
            && snipSel.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in snipSel.EnumerateObject())
            {
                List<string> ids = new();
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in prop.Value.EnumerateArray())
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) ids.Add(s);
                    }
                }
                else if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var s = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) ids.Add(s);
                }
                if (ids.Count > 0)
                {
                    cliArgs.Add("-Snippet");
                    cliArgs.Add($"{prop.Name}={string.Join(",", ids)}");
                }
            }
        }

        // Text inputs: { "section.item.input": "value" }
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("snippetInputs", out var textInps)
            && textInps.ValueKind == JsonValueKind.Object)
        {
            var pairs = new List<string>();
            foreach (var prop in textInps.EnumerateObject())
            {
                string v = prop.Value.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(prop.Name) && !string.IsNullOrWhiteSpace(v))
                    pairs.Add($"{prop.Name}={v}");
            }
            if (pairs.Count > 0) { cliArgs.Add("-Text"); cliArgs.Add(string.Join(";", pairs)); }
        }

        // Clone donor
        string donorPath = GetStr(args, "DonorPathInput", "");
        if (!string.IsNullOrWhiteSpace(donorPath))
        {
            cliArgs.Add("-CloneFrom"); cliArgs.Add(donorPath);
            cliArgs.Add("-CloneMetadata");
            if (GetStr(args, "CloneIcon", "True").Equals("True", StringComparison.OrdinalIgnoreCase))
                cliArgs.Add("-CloneIcon");
            else
                cliArgs.Add("-NoCloneIcon");
            bool cloneRsrc = GetStr(args, "CloneRsrc", "True").Equals("True", StringComparison.OrdinalIgnoreCase);
            if (cloneRsrc)
                cliArgs.Add("-CloneResources");
            else
                cliArgs.Add("-NoCloneResources");
        }

        // NOP padding
        string nopPad = GetStr(args, "NopPaddingInput", "0");
        if (int.TryParse(nopPad, out int nopCount) && nopCount > 0)
        {
            cliArgs.Add("-PadNops"); cliArgs.Add(nopPad);
        }

        // Compilation backend
        string backend = GetStr(args, "compilationBackend", "Deterministic");
        if (backend.Equals("LlvmObfuscated", StringComparison.OrdinalIgnoreCase))
        {
            cliArgs.Add("-Backend"); cliArgs.Add("LlvmObfuscated");
            var passes = GetStrArr(args, "llvmObfuscationPasses");
            if (passes.Length > 0) { cliArgs.Add("-LlvmPass"); cliArgs.Add(string.Join(",", passes)); }
        }

        // Verbose
        if (GetStr(args, "VerboseBuildCheck", "False").Equals("True", StringComparison.OrdinalIgnoreCase))
            cliArgs.Add("-Verbose");

        // Machine-readable output so we can parse results
        cliArgs.Add("-Json");

        PushEvent("build-started", new { startedAt = DateTimeOffset.UtcNow });

        bool openAfter = GetStr(args, "OpenFolderAfterCompile", "True")
            .Equals("True", StringComparison.OrdinalIgnoreCase);

        try
        {
            var result = await _cli.RunAsync(cliArgs, line => PushEvent("build-log", new { line }), ct);

            // The CLI writes a JSON block ending with a line starting with '{' when -Json is set.
            // Extract the output path from that JSON if present.
            string? outputPath = null;
            foreach (var line in result.OutputLines)
            {
                if (!line.TrimStart().StartsWith('{')) continue;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("outputPath", out var el) ||
                        doc.RootElement.TryGetProperty("OutputPath", out el))
                        outputPath = el.GetString();
                    break;
                }
                catch { /* not JSON, skip */ }
            }

            PushEvent("build-finished", new { success = result.Success, exitCode = result.ExitCode, outputPath });
            if (result.Success && openAfter && !string.IsNullOrWhiteSpace(outputPath))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outputPath}\"");
            return new { ok = result.Success, exitCode = result.ExitCode, outputPath };
        }
        catch (OperationCanceledException)
        {
            PushEvent("build-finished", new { success = false, cancelled = true, exitCode = -1 });
            return new { ok = false, cancelled = true };
        }
        finally
        {
            _buildCts = null;
        }
    }

    private Task<object?> Method_Stop(JsonElement _)
    {
        var cts = _buildCts;
        if (cts != null)
        {
            cts.Cancel();
            _buildCts = null;
        }
        PushEvent("build-stopped", new { at = DateTimeOffset.UtcNow });
        return Task.FromResult<object?>(new { ok = true });
    }

    private async Task<object?> Method_DryRun(JsonElement args)
    {
        if (!_cli.IsAvailable)
            return new { ok = false, message = "CLI not built." };

        PushEvent("build-log", new { line = "[dry-run] Validating recipe — no artifacts will be written." });
        await Task.CompletedTask;
        return new { ok = true };
    }

    // ── Catalog endpoints ──────────────────────────────────────────

    private async Task<object?> Method_CatalogEncoders(JsonElement _)
    {
        try
        {
            var runner = new Bin2ShellRunner(_paths);
            var svc = new ShellcodeEncodingCatalogService(runner, _paths);
            var cat = await svc.GetCatalogAsync();
            var items = cat.Encoders.Select(e => new { index = e.Index, name = e.Name, description = e.Description }).ToArray();
            return new { ok = true, items };
        }
        catch (Exception ex)
        {
            return new { ok = false, message = ex.Message, items = Array.Empty<object>() };
        }
    }

    private async Task<object?> Method_CatalogEnvelopes(JsonElement _)
    {
        try
        {
            var runner = new Bin2ShellRunner(_paths);
            var svc = new ShellcodeEncodingCatalogService(runner, _paths);
            var cat = await svc.GetCatalogAsync();
            var items = cat.Envelopes.Select(e => new { index = e.Index, name = e.Name, description = e.Description }).ToArray();
            return new { ok = true, items };
        }
        catch (Exception ex)
        {
            return new { ok = false, message = ex.Message, items = Array.Empty<object>() };
        }
    }

    private async Task<object?> Method_CatalogCompilers(JsonElement _)
    {
        try
        {
            var locator = new CompilerToolLocator(_logger);
            var result = await locator.DiscoverAsync();
            var items = result.Candidates.Select(c => new
            {
                path = c.Path,
                kind = c.Kind,
                version = c.InstanceVersion ?? c.Edition,
                edition = c.Edition,
                year = c.Year,
                validated = c.Validated,
            }).ToArray();
            return new { ok = true, items, best = result.Best?.Path ?? "" };
        }
        catch (Exception ex)
        {
            return new { ok = false, message = ex.Message, items = Array.Empty<object>() };
        }
    }

    private Task<object?> Method_CatalogSnippets(JsonElement _)
    {
        try
        {
            var svc = new YamlCodeSnippetCatalogService(_paths);
            var templates = svc.GetTemplates().Select(t => new
            {
                id = t.Id,
                display = t.Display,
                description = t.Description,
            }).ToArray();

            var sections = svc.GetAllSections()
                .Where(s => !string.Equals(s.Template, "shellcodeexecution", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(s.Template, "genericshellcode", StringComparison.OrdinalIgnoreCase))
                .Select(s => new
                {
                    header = s.Header,
                    template = s.Template,
                    display = s.Display,
                    allowMultiple = s.AllowMultiple,
                    inputs = s.Inputs.Select(inp => new
                    {
                        id = inp.Id,
                        label = inp.Label,
                        placeholder = inp.Placeholder,
                        defaultValue = inp.DefaultValue,
                    }).ToArray(),
                    items = s.Items
                        .Where(i => !string.IsNullOrWhiteSpace(i.Id)
                                 && !i.Id.Equals("None", StringComparison.OrdinalIgnoreCase)
                                 && !i.Display.Contains("Skip", StringComparison.OrdinalIgnoreCase))
                        .Select(i => new
                        {
                            id = i.Id,
                            display = i.Display,
                            isDefault = i.IsDefault,
                            inputs = i.Inputs.Select(inp => new
                            {
                                id = inp.Id,
                                label = inp.Label,
                                placeholder = inp.Placeholder,
                                defaultValue = inp.DefaultValue,
                            }).ToArray(),
                        }).ToArray(),
                }).ToArray();

            return Task.FromResult<object?>(new { ok = true, templates, sections });
        }
        catch (Exception ex)
        {
            return Task.FromResult<object?>(new { ok = false, message = ex.Message, templates = Array.Empty<object>(), sections = Array.Empty<object>() });
        }
    }

    // ── Analysis endpoints ─────────────────────────────────────────

    private async Task<object?> Method_AnalyzeShellcode(JsonElement args)
    {
        if (!args.TryGetProperty("path", out var pEl) || pEl.GetString() is not string path || !File.Exists(path))
            return new { ok = false, message = "File not found." };

        var bytes = await File.ReadAllBytesAsync(path);

        string sha256;
        using (var sha = SHA256.Create())
            sha256 = Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();

        double entropy = ComputeEntropy(bytes);
        string arch = DetectShellcodeArch(bytes);
        string sizeText = FormatBytes(bytes.Length);
        string bytesPreview = FormatHexPreview(bytes, 64);

        return new { ok = true, size = bytes.Length, sizeText, entropy = Math.Round(entropy, 2), arch, sha256, bytesPreview };
    }

    private async Task<object?> Method_AnalyzePe(JsonElement args)
    {
        if (!args.TryGetProperty("path", out var pEl) || pEl.GetString() is not string path || !File.Exists(path))
            return new { ok = false, message = "File not found." };

        try
        {
            var svc = new PeAnalyzerService(_logger);
            var r = await svc.AnalyzeAsync(path);

            if (!r.IsValid)
                return new { ok = false, message = r.ValidationError };

            return new
            {
                ok = true,
                filename = r.FileName,
                fileSize = r.FileSize,
                fileSizeFormatted = r.FileSizeFormatted,
                is64Bit = r.Is64Bit,
                isDll = r.IsDll,
                architecture = r.Architecture,
                machineType = r.MachineType,
                subsystem = r.Subsystem,
                sections = r.Sections.Count,
                entropy = Math.Round(r.OverallEntropy, 2),
                isPossiblyPacked = r.IsPossiblyPacked,
                hasSig = r.Security?.HasAuthenticode ?? false,
                sigInfo = r.Security?.SignatureInfo ?? "",
                codeCaves = r.CodeCaves.Select(c => new
                {
                    sectionName = c.SectionName,
                    fileOffset = c.FileOffset,
                    virtualAddress = c.VirtualAddress,
                    size = c.Size,
                    fillByte = c.FillByte,
                    isExecutable = c.IsExecutable,
                    suitableForInjection = c.SuitableForInjection,
                }).ToArray(),
                imports = r.Imports.Select(i => new
                {
                    dll = i.Name,
                    functions = i.Functions.Select(f => f.Name).ToArray(),
                }).ToArray(),
                hasVersion = r.HasVersionInfo,
                hasIcon = r.HasIcon,
                tlsCallbacks = r.Tls?.NumberOfCallbacks ?? 0,
            };
        }
        catch (Exception ex)
        {
            return new { ok = false, message = ex.Message };
        }
    }

    // ── History endpoints ──────────────────────────────────────────

    private Task<object?> Method_HistoryList(JsonElement _)
    {
        var entries = PayloadHistoryStore.Load();
        var items = entries.Select(e => new
        {
            id = e.Id,
            timestamp = e.GeneratedAtUtc,
            displayTimestamp = e.DisplayTimestamp,
            sourceFile = e.SourceFileName,
            encoderIndex = e.EncoderIndex,
            encoderName = e.EncoderName,
            envelopeIndex = e.EnvelopeIndex,
            envelopeName = e.EnvelopeName,
            payloadLengthBytes = e.PayloadLengthBytes,
        }).ToArray();
        return Task.FromResult<object?>(new { ok = true, items });
    }

    private Task<object?> Method_HistoryDelete(JsonElement args)
    {
        if (!args.TryGetProperty("id", out var idEl)) throw new ArgumentException("id required");
        var id = idEl.GetString() ?? "";
        bool deleted = PayloadHistoryStore.Delete(id);
        return Task.FromResult<object?>(new { ok = deleted });
    }

    private Task<object?> Method_HistoryClear(JsonElement _)
    {
        PayloadHistoryStore.Clear();
        return Task.FromResult<object?>(new { ok = true });
    }

    // ── Shell helper endpoints ─────────────────────────────────────

    private Task<object?> Method_RevealFile(JsonElement args)
    {
        if (!args.TryGetProperty("path", out var pEl)) throw new ArgumentException("path required");
        var path = pEl.GetString() ?? "";
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{Path.GetDirectoryName(path)}\"",
                    UseShellExecute = true,
                });
            }
            catch { /* ignore */ }
        }
        return Task.FromResult<object?>(new { ok = true });
    }

    private Task<object?> Method_OpenFile(JsonElement args)
    {
        if (!args.TryGetProperty("path", out var pEl)) throw new ArgumentException("path required");
        var path = pEl.GetString() ?? "";
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { /* ignore */ }
        }
        return Task.FromResult<object?>(new { ok = true });
    }

    private Task<object?> Method_SetPlaybook(JsonElement args)
    {
        if (!args.TryGetProperty("path", out var pEl)) throw new ArgumentException("path required");
        var path = pEl.GetString() ?? "";
        bool success = _paths.SetActivePlaybook(path);
        return Task.FromResult<object?>(new { ok = success, active = _paths.ActivePlaybookFullPath });
    }

    // ── Static helpers ─────────────────────────────────────────────

    private static string GetStr(JsonElement el, string key, string fallback)
    {
        if (el.ValueKind != JsonValueKind.Object) return fallback;
        if (el.TryGetProperty(key, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
                return prop.GetString() ?? fallback;
            if (prop.ValueKind == JsonValueKind.True) return "True";
            if (prop.ValueKind == JsonValueKind.False) return "False";
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetRawText();
        }
        return fallback;
    }

    private static string[] GetStrArr(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
        if (!el.TryGetProperty(key, out var prop)) return Array.Empty<string>();
        if (prop.ValueKind == JsonValueKind.Array)
            return prop.EnumerateArray().Select(x => x.GetString() ?? "")
                       .Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return Array.Empty<string>();
    }

    private static double ComputeEntropy(byte[] data)
    {
        if (data.Length == 0) return 0.0;
        var freq = new int[256];
        foreach (var b in data) freq[b]++;
        double e = 0.0, len = data.Length;
        foreach (var f in freq) { if (f == 0) continue; double p = f / len; e -= p * Math.Log2(p); }
        return e;
    }

    private static string DetectShellcodeArch(byte[] b)
    {
        if (b.Length < 2) return "unknown";
        // x64 msfvenom/CobaltStrike typical prelude: FC 48 ...
        if (b[0] == 0xFC && b[1] == 0x48) return "x86_64";
        // x86: FC E8 ...
        if (b[0] == 0xFC && b[1] == 0xE8) return "x86";
        // x64 REX prefix
        if (b.Length > 3 && b[0] == 0x48 && (b[1] == 0x83 || b[1] == 0x89)) return "x86_64";
        return "unknown";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F2} MB";
    }

    private static string FormatHexPreview(byte[] data, int maxBytes)
    {
        var sb = new StringBuilder();
        int count = Math.Min(data.Length, maxBytes);
        for (int i = 0; i < count; i += 16)
        {
            sb.AppendFormat("{0:x8}  ", i);
            int lineEnd = Math.Min(i + 16, count);
            for (int j = i; j < lineEnd; j++) sb.AppendFormat("{0:x2} ", data[j]);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private async Task<object?> Method_BrowseFile(JsonElement args)
    {
        if (_host == null) return new { ok = false, message = "no host window" };

        // Optional filter list: { filters: [{ name: "Binary", patterns: [".bin", ".exe"] }] }
        var patterns = new List<string>();
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("filters", out var fEl)
            && fEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fEl.EnumerateArray())
            {
                if (f.TryGetProperty("patterns", out var pEl) && pEl.ValueKind == JsonValueKind.Array)
                    foreach (var p in pEl.EnumerateArray())
                    {
                        var s = p.GetString();
                        if (!string.IsNullOrEmpty(s)) patterns.Add(s);
                    }
            }
        }
        if (patterns.Count == 0) patterns.Add("*");

        // Marshal to UI thread; file picker requires it.
        var tcs = new TaskCompletionSource<string?>();
        _ui.TryEnqueue(async () =>
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.List,
                    SuggestedStartLocation = PickerLocationId.Desktop,
                };
                foreach (var p in patterns) picker.FileTypeFilter.Add(p);

                InitializeWithWindow.Initialize(picker, _host.Hwnd);
                var file = await picker.PickSingleFileAsync();
                tcs.SetResult(file?.Path);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        var path = await tcs.Task;
        if (string.IsNullOrEmpty(path)) return new { ok = false, cancelled = true };
        return new { ok = true, path };
    }

    private async Task<object?> Method_BrowseFolder(JsonElement args)
    {
        if (_host == null) return new { ok = false, message = "no host window" };

        var tcs = new TaskCompletionSource<string?>();
        _ui.TryEnqueue(async () =>
        {
            try
            {
                var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Desktop };
                picker.FileTypeFilter.Add("*");
                InitializeWithWindow.Initialize(picker, _host.Hwnd);
                var folder = await picker.PickSingleFolderAsync();
                tcs.SetResult(folder?.Path);
            }
            catch (Exception ex) { tcs.SetException(ex); }
        });

        var path = await tcs.Task;
        if (string.IsNullOrEmpty(path)) return new { ok = false, cancelled = true };
        return new { ok = true, path };
    }

    private Task<object?> Method_WindowMinimize(JsonElement _)
    {
        _ui.TryEnqueue(() => _host?.MinimizeFromBridge());
        return Task.FromResult<object?>(new { ok = true });
    }

    private Task<object?> Method_WindowMaximize(JsonElement _)
    {
        _ui.TryEnqueue(() => _host?.MaximizeFromBridge());
        return Task.FromResult<object?>(new { ok = true });
    }

    private Task<object?> Method_WindowClose(JsonElement _)
    {
        _ui.TryEnqueue(() => _host?.CloseFromBridge());
        return Task.FromResult<object?>(new { ok = true });
    }

    private async Task<object?> Method_ReadText(JsonElement args)
    {
        if (!args.TryGetProperty("path", out var pEl)) throw new ArgumentException("path required");
        var path = pEl.GetString() ?? "";
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new { ok = false, message = "path not found" };
        var text = await File.ReadAllTextAsync(path);
        return new { ok = true, path, text };
    }

    private Task<object?> Method_AppInfo(JsonElement _)
    {
        return Task.FromResult<object?>(new
        {
            version = "2.1.0",
            cliAvailable = _cli.IsAvailable,
            cliPath = _cli.CliPath,
            assetsDirectory = _paths.AssetsDirectory,
            executableDirectory = _paths.ExecutableDirectory,
            playbookPath = _paths.ActivePlaybookPath,
        });
    }

    private Task<object?> Method_ListPlaybooks(JsonElement _)
    {
        var list = _paths.GetAvailablePlaybookFiles() ?? Array.Empty<string>();
        return Task.FromResult<object?>(new
        {
            active = _paths.ActivePlaybookFullPath,
            files = list,
        });
    }
}
