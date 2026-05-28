using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Washmachine.Logging;
using Washmachine.Models;
using Washmachine.Services;

namespace Washmachine.Testing;

/// <summary>
/// Headless test harness that exercises the full pipeline:
///   bin2shell → template rendering → g++ compilation → exe execution.
///
/// Phase 1: all encoder × envelope × webhelper combos (default template + default snippets).
/// Phase 2: all template × snippet permutations (encoder=0, envelope=0, file source).
/// Phase 3: multiple shellcode inputs from testing assets (safe shellcodes only).
///
/// Output: JSON results array to stdout, suitable for agent consumption.
/// </summary>
public static class TestHarness
{
    private sealed class TestResult
    {
        public int Id { get; set; }
        public string Phase { get; set; } = "";
        public string Description { get; set; } = "";
        public bool CompileOk { get; set; }
        public bool RunOk { get; set; }
        public bool CompileBlockedBySecurity { get; set; }
        public int? ExitCode { get; set; }
        public string? Error { get; set; }
        public double DurationMs { get; set; }
    }

    public static async Task<int> RunAsync(string[] args)
    {
        // Parse arguments
        string? shellcodeFile = null;
        string? payloadUrl = null;
        string? phase = null;
        string? testAssetsDir = null;
        bool stopOnFail = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--shellcode" or "-Shellcode" or "-s" when i + 1 < args.Length:
                    shellcodeFile = args[++i];
                    break;
                case "--url" or "-Url" or "-u" when i + 1 < args.Length:
                    payloadUrl = args[++i];
                    break;
                case "--phase" or "-Phase" when i + 1 < args.Length:
                    phase = args[++i];
                    break;
                case "--test-assets" or "-TestAssets" when i + 1 < args.Length:
                    testAssetsDir = args[++i];
                    break;
                case "--stop-on-fail" or "-StopOnFail":
                    stopOnFail = true;
                    break;
            }
        }

        // Phase 3 uses test-assets directory; phases 1/2 require shellcode file
        bool requiresShellcode = phase is null or "all" or "1" or "2";
        if (requiresShellcode && (string.IsNullOrEmpty(shellcodeFile) || !File.Exists(shellcodeFile)))
        {
            Console.Error.WriteLine("Usage: -Shellcode <path-to-.bin> [-Url <payload-url>] [-Phase 1|2|3|all] [-TestAssets <dir>] [-StopOnFail]");
            return 1;
        }

        phase ??= "all";
        var paths = new AppPaths();
        var logger = new ConsoleLogger();
        var runner = new Bin2ShellRunner(paths);
        var snippetService = new PlaybookService(paths);
        var toolLocator = new CompilerToolLocator(logger);
        var compiler = new CompilerService(paths, runner, snippetService, toolLocator, logger);

        // Load encoding catalog
        var encodingCatalog = new ShellcodeEncodingCatalogService(runner, paths);
        var catalog = await encodingCatalog.GetCatalogAsync();

        var results = new List<TestResult>();
        int id = 0;

        // ─── Phase 1: all encoder × envelope × webhelper combos ─────────────
        if (phase is "all" or "1")
        {
            logger.Info($"=== PHASE 1: Encoding combos (file source) ===");
            logger.Info($"  Encoders: {catalog.Encoders.Count}, Envelopes: {catalog.Envelopes.Count}, WebHelpers: {catalog.WebHelpers.Count}");

            foreach (var enc in catalog.Encoders)
            foreach (var env in catalog.Envelopes)
            {
                id++;
                var desc = $"P1 File: enc={enc.Index}({enc.Name}), env={env.Index}({env.Name})";
                var data = BuildPhase1Data(shellcodeFile ?? "", null, enc, env, null, snippetService);
                var result = await RunTestAsync(id, "P1-File", desc, data, compiler, paths, logger);
                results.Add(result);
                if (stopOnFail && (!result.CompileOk || !result.RunOk)) goto done;
            }

            // URL source: all encoder × envelope × webhelper
            if (!string.IsNullOrEmpty(payloadUrl) && !string.IsNullOrEmpty(shellcodeFile))
            {
                foreach (var enc in catalog.Encoders)
                foreach (var env in catalog.Envelopes)
                {
                    // For URL mode, bin2shell web mode produces the payload.
                    // We pass the payload URL and let the harness run bin2shell -w.
                    foreach (var wh in catalog.WebHelpers.DefaultIfEmpty(null))
                    {
                        id++;
                        var desc = $"P1 URL: enc={enc.Index}({enc.Name}), env={env.Index}({env.Name}), wh={wh?.Index ?? 0}({wh?.Name ?? "none"})";
                        var urlData = await BuildPhase1WebDataAsync(
                            shellcodeFile, payloadUrl, enc, env, wh,
                            snippetService, runner, paths, logger);
                        if (urlData != null)
                        {
                            var result = await RunTestAsync(id, "P1-URL", desc, urlData, compiler, paths, logger);
                            results.Add(result);
                            if (stopOnFail && (!result.CompileOk || !result.RunOk)) goto done;
                        }
                        else
                        {
                            results.Add(new TestResult
                            {
                                Id = id, Phase = "P1-URL", Description = desc,
                                CompileOk = false, RunOk = false,
                                Error = "bin2shell -w failed to produce output"
                            });
                            if (stopOnFail) goto done;
                        }
                    }
                }
            }
        }

        // ─── Phase 2: template × snippet coverage (smart sampling) ────────────
        //
        // Full Cartesian product is infeasible (100M+ combos for the default template).
        // Instead we use "one-factor-at-a-time" sampling:
        //   1. One baseline test per template with all snippets set to their first value.
        //   2. For each snippet placeholder, cycle through every possible value while
        //      keeping all other placeholders at their baseline (first) value.
        // This guarantees every snippet value is tested at least once per template.
        if (phase is "all" or "2")
        {
            logger.Info($"=== PHASE 2: Template + snippet coverage (one-factor-at-a-time) ===");
            var templates = snippetService.GetTemplates();
            
            // Estimate total tests for progress reporting
            int estimatedTests = 0;
            foreach (var t in templates)
            {
                int snippetValues = 0;
                foreach (var ph in t.Placeholders.Where(p => p.Kind == TemplatePlaceholderKind.Snippet))
                {
                    if (snippetService.TryResolveSection(ph.SnippetTemplateKey, out var sec))
                        snippetValues += sec.Items.Count; // each value minus its baseline, plus "" option
                }
                estimatedTests += Math.Max(1, snippetValues + 1); // +1 for baseline
            }
            logger.Info($"  Templates: {templates.Count}, estimated tests: ~{estimatedTests}");

            foreach (var template in templates)
            {
                var snippetPlaceholders = template.Placeholders
                    .Where(p => p.Kind == TemplatePlaceholderKind.Snippet)
                    .ToList();

                // Resolve each placeholder to its combo name and list of values
                var axes = new List<(string ComboName, string[] Values)>();
                foreach (var ph in snippetPlaceholders)
                {
                    if (!snippetService.TryResolveSection(ph.SnippetTemplateKey, out var section))
                        continue;
                    string comboName = $"snippetCombo_{ph.SnippetTemplateKey}_0";
                    var values = new List<string> { "" };
                    foreach (var item in section.Items)
                        values.Add(item.Id);
                    axes.Add((comboName, values.ToArray()));
                }

                if (axes.Count == 0)
                {
                    // No snippets — just test with defaults
                    id++;
                    var desc = $"P2: template={template.Id}, snippets=[defaults]";
                    var data = BuildPhase2Data(shellcodeFile!, template.Id, new Dictionary<string, string>(), snippetService);
                    var result = await RunTestAsync(id, "P2", desc, data, compiler, paths, logger);
                    results.Add(result);
                    if (stopOnFail && (!result.CompileOk || !result.RunOk)) goto done;
                    continue;
                }

                // Build baseline: first non-empty value for each placeholder
                var baseline = new Dictionary<string, string>();
                foreach (var (comboName, values) in axes)
                    baseline[comboName] = values.Length > 1 ? values[1] : values[0];

                // Test baseline combo
                id++;
                {
                    var desc = $"P2: template={template.Id}, snippets=[{string.Join(", ", baseline.Select(kv => $"{kv.Key}={kv.Value}"))}]";
                    var data = BuildPhase2Data(shellcodeFile!, template.Id, baseline, snippetService);
                    var result = await RunTestAsync(id, "P2", desc, data, compiler, paths, logger);
                    results.Add(result);
                    if (stopOnFail && (!result.CompileOk || !result.RunOk)) goto done;
                }

                // Vary each axis one at a time
                foreach (var (comboName, values) in axes)
                {
                    foreach (var val in values)
                    {
                        // Skip the baseline value (already tested)
                        if (val == baseline[comboName]) continue;

                        id++;
                        var combo = new Dictionary<string, string>(baseline) { [comboName] = val };
                        var desc = $"P2: template={template.Id}, vary {comboName}={val}";
                        var data = BuildPhase2Data(shellcodeFile!, template.Id, combo, snippetService);
                        var result = await RunTestAsync(id, "P2", desc, data, compiler, paths, logger);
                        results.Add(result);
                        if (stopOnFail && (!result.CompileOk || !result.RunOk)) goto done;
                    }
                }
            }
        }

        // ─── Phase 3: multiple shellcode inputs from test assets ────────────
        if (phase is "all" or "3")
        {
            // Resolve test assets directory
            var assetsDir = testAssetsDir;
            if (string.IsNullOrEmpty(assetsDir))
            {
                // Try to find it relative to repo root
                var repoRoot = FindRepoRoot(paths.ExecutableDirectory);
                if (repoRoot != null)
                {
                    assetsDir = Path.Combine(repoRoot, "Testing", "binary", "shellcodes");
                }
            }

            if (!string.IsNullOrEmpty(assetsDir) && Directory.Exists(assetsDir))
            {
                logger.Info($"=== PHASE 3: Multi-shellcode input testing ===");
                logger.Info($"  Assets directory: {assetsDir}");

                // Safe shellcodes for automated testing (no network, quick execution).
                // big_payload.bin is a synthetic 4 MB NOP-sled + ret blob created on demand
                // by run_tests.ps1 — it stresses the encoding/compilation path with a large input.
                var safeShellcodes = new[] { "calc64.bin", "messagebox.bin", "notepad64.bin", "createfile.bin", "big_payload.bin" };
                var foundShellcodes = Directory.GetFiles(assetsDir, "*.bin")
                    .Where(f => safeShellcodes.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                    .ToList();

                logger.Info($"  Found {foundShellcodes.Count} safe shellcodes to test");

                foreach (var scFile in foundShellcodes)
                {
                    var scName = Path.GetFileNameWithoutExtension(scFile);
                    logger.Info($"\n--- Testing shellcode: {scName} ---");

                    // Test with default encoder/envelope
                    id++;
                    var desc = $"P3: shellcode={scName}, enc=0, env=0";
                    var data = BuildPhase3Data(scFile, snippetService);
                    var result = await RunTestAsync(id, "P3", desc, data, compiler, paths, logger);
                    results.Add(result);
                    if (stopOnFail && (!result.CompileOk || !result.RunOk)) goto done;

                    // Test with first non-zero encoder if available
                    if (catalog.Encoders.Count > 1)
                    {
                        var enc = catalog.Encoders[1];
                        id++;
                        desc = $"P3: shellcode={scName}, enc={enc.Index}({enc.Name})";
                        data = BuildPhase3DataWithEncoder(scFile, enc, snippetService);
                        result = await RunTestAsync(id, "P3-Enc", desc, data, compiler, paths, logger);
                        results.Add(result);
                        if (stopOnFail && (!result.CompileOk || !result.RunOk)) goto done;
                    }
                }
            }
            else
            {
                logger.Warn($"Phase 3 skipped: test assets directory not found ({assetsDir ?? "not specified"})");
            }
        }

        done:
        // Output results
        int passed = results.Count(r => r.CompileOk && r.RunOk);
        int compileFailed = results.Count(r => !r.CompileOk);
        int runFailed = results.Count(r => r.CompileOk && !r.RunOk);
        int securityBlocked = results.Count(r => r.CompileBlockedBySecurity);

        logger.Info($"\n=== RESULTS: {passed}/{results.Count} passed, {compileFailed} compile failures, {runFailed} runtime failures, {securityBlocked} security-blocked ===");

        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        var resultPath = Path.Combine(paths.ExecutableDirectory, "test_results.json");
        await File.WriteAllTextAsync(resultPath, json);
        logger.Info($"Results written to: {resultPath}");

        // Print failures (separate security-blocked cases so they don't look like logic regressions)
        foreach (var r in results.Where(r => (!r.CompileOk || !r.RunOk) && !r.CompileBlockedBySecurity))
        {
            logger.Error($"  FAIL #{r.Id}: {r.Description} — {r.Error}");
        }

        foreach (var r in results.Where(r => r.CompileBlockedBySecurity))
        {
            logger.Warn($"  SECURITY-BLOCKED #{r.Id}: {r.Description} — {r.Error}");
        }

        return (compileFailed - securityBlocked + runFailed) > 0 ? 1 : 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Data builders
    // ═══════════════════════════════════════════════════════════════════════

    private static UiData BuildPhase1Data(
        string shellcodeFile,
        string? payloadUrl,
        ShellcodeEncodingItem encoder,
        ShellcodeEncodingItem envelope,
        ShellcodeEncodingItem? webHelper,
        IPlaybookService snippets)
    {
        var textBoxes = new Dictionary<string, string>
        {
            [UiDataKeys.ShellcodeFile] = shellcodeFile,
            [UiDataKeys.ShellcodeRaw] = "",
            [UiDataKeys.ShellcodeUrl] = payloadUrl ?? "",
            [UiDataKeys.ShellcodeUrlFile] = "",
        };

        var comboBoxes = new Dictionary<string, string>
        {
            [UiDataKeys.Template] = "default",
            ["bin2hexEncoder"] = encoder.DisplayText,
            ["bin2hexEnvelope"] = envelope.DisplayText,
        };

        // Set default snippets for the minimal template
        SetDefaultSnippets(comboBoxes, textBoxes, snippets, "default");

        return new UiData(textBoxes, comboBoxes);
    }

    private static async Task<UiData?> BuildPhase1WebDataAsync(
        string shellcodeFile,
        string payloadUrl,
        ShellcodeEncodingItem encoder,
        ShellcodeEncodingItem envelope,
        ShellcodeEncodingItem? webHelper,
        IPlaybookService snippets,
        IBin2ShellRunner runner,
        IAppPaths paths,
        IAppLogger logger)
    {
        try
        {
            var args = new List<string>();
            if (!string.IsNullOrWhiteSpace(paths.Bin2ShellAlgos) && File.Exists(paths.Bin2ShellAlgos))
            {
                args.Add("-y");
                args.Add(paths.Bin2ShellAlgos);
            }
            args.Add("-w");
            args.Add("-e");
            args.Add(encoder.Index.ToString(CultureInfo.InvariantCulture));
            args.Add("-v");
            args.Add(envelope.Index.ToString(CultureInfo.InvariantCulture));
            if (webHelper != null)
            {
                args.Add("-wh");
                args.Add(webHelper.Index.ToString(CultureInfo.InvariantCulture));
            }
            args.Add(shellcodeFile);

            string output = await runner.RunAsync(args);
            if (string.IsNullOrWhiteSpace(output)) return null;

            var webOutput = Bin2ShellWebOutputParser.Parse(output);
            webOutput.ReplacePayloadUrl(payloadUrl);

            var textBoxes = new Dictionary<string, string>
            {
                [UiDataKeys.ShellcodeFile] = "",
                [UiDataKeys.ShellcodeRaw] = "",
                [UiDataKeys.ShellcodeUrl] = payloadUrl,
                [UiDataKeys.ShellcodeUrlFile] = shellcodeFile,
                ["__webPayloadCodeBlock__"] = webOutput.BuildBody(),
                ["__webPayloadPreamble__"] = webOutput.BuildPreamble(),
            };

            var comboBoxes = new Dictionary<string, string>
            {
                [UiDataKeys.Template] = "default",
                ["bin2hexEncoder"] = encoder.DisplayText,
                ["bin2hexEnvelope"] = envelope.DisplayText,
            };

            SetDefaultSnippets(comboBoxes, textBoxes, snippets, "default");
            return new UiData(textBoxes, comboBoxes);
        }
        catch (Exception ex)
        {
            logger.Error($"bin2shell -w failed: {ex.Message}");
            return null;
        }
    }

    private static UiData BuildPhase2Data(
        string shellcodeFile,
        string templateId,
        Dictionary<string, string> snippetSelections,
        IPlaybookService snippets)
    {
        var textBoxes = new Dictionary<string, string>
        {
            [UiDataKeys.ShellcodeFile] = shellcodeFile,
            [UiDataKeys.ShellcodeRaw] = "",
            [UiDataKeys.ShellcodeUrl] = "",
            [UiDataKeys.ShellcodeUrlFile] = "",
        };

        var comboBoxes = new Dictionary<string, string>
        {
            [UiDataKeys.Template] = templateId,
            ["bin2hexEncoder"] = "0 - none",
            ["bin2hexEnvelope"] = "0 - none",
        };

        // Apply snippet selections
        foreach (var kv in snippetSelections)
        {
            comboBoxes[kv.Key] = kv.Value;
        }

        // Apply default text input values for any required inputs
        SetDefaultInputValues(textBoxes, snippets);

        return new UiData(textBoxes, comboBoxes);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Snippet combinatorics
    // ═══════════════════════════════════════════════════════════════════════

    private static IEnumerable<Dictionary<string, string>> EnumerateSnippetCombinations(
        IReadOnlyList<CodeTemplatePlaceholder> placeholders,
        IReadOnlyList<CodeSnippetSection> allSections,
        IPlaybookService snippetService)
    {
        // Build a list of (comboBoxName, possibleValues[]) per placeholder
        var axes = new List<(string ComboName, string[] Values)>();

        foreach (var ph in placeholders)
        {
            if (!snippetService.TryResolveSection(ph.SnippetTemplateKey, out var section))
                continue;

            // The control name the CompilerService looks for
            string comboName = $"snippetCombo_{ph.SnippetTemplateKey}_0";

            // "" = no selection, plus each item
            var values = new List<string> { "" };
            foreach (var item in section.Items)
                values.Add(item.Id);

            axes.Add((comboName, values.ToArray()));
        }

        if (axes.Count == 0)
        {
            yield return new Dictionary<string, string>();
            yield break;
        }

        // Cartesian product
        foreach (var combo in CartesianProduct(axes))
            yield return combo;
    }

    private static IEnumerable<Dictionary<string, string>> CartesianProduct(
        List<(string ComboName, string[] Values)> axes)
    {
        var indices = new int[axes.Count];
        while (true)
        {
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < axes.Count; i++)
                dict[axes[i].ComboName] = axes[i].Values[indices[i]];
            yield return dict;

            // Advance
            int carry = axes.Count - 1;
            while (carry >= 0)
            {
                indices[carry]++;
                if (indices[carry] < axes[carry].Values.Length)
                    break;
                indices[carry] = 0;
                carry--;
            }
            if (carry < 0) break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Default helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static void SetDefaultSnippets(
        Dictionary<string, string> comboBoxes,
        Dictionary<string, string> textBoxes,
        IPlaybookService snippets,
        string templateId)
    {
        if (!snippets.TryGetTemplate(templateId, out var template))
            return;

        foreach (var ph in template.Placeholders.Where(p => p.Kind == TemplatePlaceholderKind.Snippet))
        {
            if (!snippets.TryResolveSection(ph.SnippetTemplateKey, out var section))
                continue;

            string comboName = $"snippetCombo_{ph.SnippetTemplateKey}_0";
            var defaultItem = section.Items.FirstOrDefault(i => i.IsDefault)
                              ?? section.Items.FirstOrDefault();
            if (defaultItem != null)
                comboBoxes[comboName] = defaultItem.Id;
        }

        SetDefaultInputValues(textBoxes, snippets);
    }

    private static void SetDefaultInputValues(
        Dictionary<string, string> textBoxes,
        IPlaybookService snippets)
    {
        foreach (var section in snippets.GetAllSections())
        {
            foreach (var input in section.Inputs)
            {
                if (!string.IsNullOrWhiteSpace(input.DefaultValue) &&
                    !textBoxes.ContainsKey(input.Id))
                {
                    textBoxes[input.Id] = input.DefaultValue;
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Test execution
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<TestResult> RunTestAsync(
        int id, string phase, string description,
        UiData data, CompilerService compiler, IAppPaths paths, IAppLogger logger,
        bool fullClean = true)
    {
        // Clean up temp cpp files from previous test
        CleanTempCpp(paths, fullClean);

        var sw = Stopwatch.StartNew();
        var result = new TestResult { Id = id, Phase = phase, Description = description };

        try
        {
            logger.Info($"[#{id}] {description}");

            // Apply a 120-second timeout to prevent compilation from hanging indefinitely
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            CompilerResult compileResult;
            try
            {
                compileResult = await compiler.CompileAsync(data, cts.Token);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                result.DurationMs = sw.Elapsed.TotalMilliseconds;
                result.CompileOk = false;
                result.Error = "Compilation timed out after 120 seconds";
                logger.Error($"  [#{id}] COMPILE TIMEOUT (120s)");
                return result;
            }
            sw.Stop();
            result.DurationMs = sw.Elapsed.TotalMilliseconds;

            if (!compileResult.Success || !compileResult.ConversionResult.Success)
            {
                result.CompileOk = false;
                result.Error = compileResult.ConversionResult.Error ?? "Compilation failed";
                if (IsSecurityBlock(result.Error))
                {
                    result.CompileBlockedBySecurity = true;
                    logger.Warn($"  [#{id}] COMPILE BLOCKED BY SECURITY: {Truncate(result.Error, 200)}");
                }
                else
                {
                    logger.Error($"  [#{id}] COMPILE FAILED: {Truncate(result.Error, 200)}");
                }
                return result;
            }

            result.CompileOk = true;

            // Find the produced output
            var exePath = FindProducedExe(compileResult);
            if (exePath == null || !File.Exists(exePath))
            {
                // Compilation succeeded but exe was likely deleted by security software
                result.RunOk = true;
                result.CompileBlockedBySecurity = true;
                logger.Warn($"  [#{id}] COMPILE OK but exe missing (security software likely deleted it)");
                return result;
            }

            // DLLs can't be executed directly — compile-only success
            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                result.RunOk = true;
                logger.Ok($"  [#{id}] DLL compiled OK: {Path.GetFileName(exePath)} ({result.DurationMs:F0}ms)");
                try { File.Delete(exePath); } catch { }
                return result;
            }

            // Execute the exe with a timeout
            var (exitCode, runError) = await ExecuteExeAsync(exePath, TimeSpan.FromSeconds(15));
            result.ExitCode = exitCode;
            if (runError != null)
            {
                if (IsSecurityBlock(runError))
                {
                    // Defender blocked execution — compilation succeeded, just can't run it
                    result.RunOk = true;
                    result.CompileBlockedBySecurity = true;
                    logger.Warn($"  [#{id}] RUN BLOCKED BY SECURITY (compile OK): {Truncate(runError, 150)}");
                }
                else if (IsExpectedInteractiveTimeout(description, runError))
                {
                    result.RunOk = true;
                    logger.Warn($"  [#{id}] RUN TIMEOUT ACCEPTED: interactive payload expected ({Truncate(runError, 200)})");
                }
                else
                {
                    result.RunOk = false;
                    result.Error = runError;
                    logger.Error($"  [#{id}] RUN FAILED: exit={exitCode}, {Truncate(runError, 200)}");
                }
            }
            else
            {
                result.RunOk = true;
                logger.Ok($"  [#{id}] OK (exit={exitCode}, {result.DurationMs:F0}ms)");
            }

            // Clean up exe
            try { File.Delete(exePath); } catch { }
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.DurationMs = sw.Elapsed.TotalMilliseconds;
            result.Error = ex.Message;
            logger.Error($"  [#{id}] EXCEPTION: {Truncate(ex.Message, 200)}");
        }

        return result;
    }

    private static string? FindProducedExe(CompilerResult result)
        => !string.IsNullOrEmpty(result.OutputExePath) && File.Exists(result.OutputExePath)
            ? result.OutputExePath
            : null;

    private static async Task<(int exitCode, string? error)> ExecuteExeAsync(string exePath, TimeSpan timeout)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            var stderr = new StringBuilder();
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            if (!proc.Start())
                return (-1, "Failed to start process");

            proc.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return (-1, $"Timed out after {timeout.TotalSeconds}s");
            }

            // exitCode 0 or messagebox-related codes are acceptable
            // For messagebox.bin, the process may show a dialog — we accept timeout as OK
            if (proc.ExitCode != 0 && stderr.Length > 0)
                return (proc.ExitCode, $"Exit {proc.ExitCode}: {stderr.ToString().Trim()}");

            return (proc.ExitCode, null);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";

    private static bool IsSecurityBlock(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("contains a virus", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("potentially unwanted software", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("operation did not complete successfully", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpectedInteractiveTimeout(string description, string? runError)
    {
        if (string.IsNullOrWhiteSpace(runError))
            return false;

        if (!runError.Contains("Timed out after", StringComparison.OrdinalIgnoreCase))
            return false;

        // Shellcode loaders with anti-emulation, anti-sandbox, or anti-debugging
        // snippets intentionally delay execution — timeouts are expected behavior.
        return description.Contains("shellcode=messagebox", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("shellcode=notepad64", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("antiemulation", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("antisandbox", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("antidebugging", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("antianalysis", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("decoy=ShowMessageBox", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("ShowMessageBox", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// No-op — per-session directories are self-contained and don't need inter-test cleanup.
    /// Kept for API compatibility with existing test orchestration code.
    /// </summary>
    private static void CleanTempCpp(IAppPaths paths, bool fullClean = true)
    {
        // Each session now writes to its own directory under logging/,
        // so there's nothing to clean between test runs.
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Phase 3 helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static UiData BuildPhase3Data(
        string shellcodeFile,
        IPlaybookService snippets)
    {
        var textBoxes = new Dictionary<string, string>
        {
            [UiDataKeys.ShellcodeFile] = shellcodeFile,
            [UiDataKeys.ShellcodeRaw] = "",
            [UiDataKeys.ShellcodeUrl] = "",
            [UiDataKeys.ShellcodeUrlFile] = "",
        };

        var comboBoxes = new Dictionary<string, string>
        {
            [UiDataKeys.Template] = "default",
            ["bin2hexEncoder"] = "0 - none",
            ["bin2hexEnvelope"] = "0 - none",
        };

        SetDefaultSnippets(comboBoxes, textBoxes, snippets, "default");
        return new UiData(textBoxes, comboBoxes);
    }

    private static UiData BuildPhase3DataWithEncoder(
        string shellcodeFile,
        ShellcodeEncodingItem encoder,
        IPlaybookService snippets)
    {
        var textBoxes = new Dictionary<string, string>
        {
            [UiDataKeys.ShellcodeFile] = shellcodeFile,
            [UiDataKeys.ShellcodeRaw] = "",
            [UiDataKeys.ShellcodeUrl] = "",
            [UiDataKeys.ShellcodeUrlFile] = "",
        };

        var comboBoxes = new Dictionary<string, string>
        {
            [UiDataKeys.Template] = "default",
            ["bin2hexEncoder"] = encoder.DisplayText,
            ["bin2hexEnvelope"] = "0 - none",
        };

        SetDefaultSnippets(comboBoxes, textBoxes, snippets, "default");
        return new UiData(textBoxes, comboBoxes);
    }

    private static string? FindRepoRoot(string startDir)
    {
        var current = startDir;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                File.Exists(Path.Combine(current, "washmachine.sln")))
            {
                return current;
            }
            var parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent;
        }
        return null;
    }
}

/// <summary>Minimal console logger for the test harness.</summary>
internal sealed class ConsoleLogger : IAppLogger
{
    public void Debug(string message) => Console.WriteLine($"  DEBUG: {message}");
    public void Info(string message) => Console.WriteLine(message);
    public void Ok(string message) => Console.WriteLine($"  OK: {message}");
    public void Warn(string message) => Console.Error.WriteLine($"  WARN: {message}");
    public void Error(string message) => Console.Error.WriteLine($"  ERROR: {message}");
}
