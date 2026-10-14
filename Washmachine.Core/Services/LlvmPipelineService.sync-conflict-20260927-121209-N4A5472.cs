using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Washmachine.Logging;
using Washmachine.Models;

namespace Washmachine.Services;

/// <summary>
/// Fine-grained controls passed to <see cref="LlvmPipelineService.CompileAsync"/>.
/// All fields are optional — null/empty falls back to the historical defaults.
/// </summary>
public sealed class LlvmCompileOptions
{
    public string Toolchain { get; init; } = "auto";  // auto | clang-cl | clang++
    public string OptLevel  { get; init; } = "O2";    // O0..O3, Os, Oz
    public string Arch      { get; init; } = "x64";   // x64 | x86
    public string Subsystem { get; init; } = "windows"; // windows | console
    public string CppStandard { get; init; } = "17";  // 14 | 17 | 20
    public IReadOnlyList<string> Defines    { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExtraFlags { get; init; } = Array.Empty<string>();
    public bool StripSymbols { get; init; }
    public bool Lto          { get; init; }
    public bool NoGcSections { get; init; }
    public bool DebugInfo    { get; init; }

    /// <summary>
    /// Time-Stretch pass intensity (0..100). Forwarded to pass-runner.exe via
    /// the SLOWDOWN_LEVEL environment variable. Has no effect unless the
    /// time-stretch pass is in the pipeline.
    /// </summary>
    public int SlowdownLevel { get; init; }
}

/// <summary>
/// Compiles C++ source using the bundled LLVM clang++/clang-cl with optional IR-level
/// obfuscation passes injected via <c>-fpass-plugin=</c>.
///
/// <para>
/// Toolchain selection (in priority order):
/// <list type="number">
///   <item>If MSVC (vcvars) is discoverable, uses <b>clang-cl.exe</b> with MSVC-compatible flags
///         so no MinGW sysroot is needed.</item>
///   <item>Otherwise, falls back to <b>clang++.exe</b> with <c>-target x86_64-w64-mingw32</c>
///         (requires MinGW headers in PATH or <c>Tools\mingw\</c>).</item>
/// </list>
/// </para>
/// </summary>
public static class LlvmPipelineService
{
    private const string ClangClExe = "clang-cl.exe";
    private const string ClangPlusPlusExe = "clang++.exe";

    /// <summary>
    /// Compiles all .cpp files in <paramref name="sourceDirectory"/> using the bundled LLVM toolchain,
    /// applying <paramref name="enabledPasses"/> as IR-level plugins.
    /// </summary>
    public static Task<CppFileConversionResult> CompileAsync(
        string sourceDirectory,
        string llvmBinDirectory,
        string buildDirectory,
        IReadOnlyList<LlvmPassDefinition> enabledPasses,
        CompilerToolDiscoveryResult? discovery,
        IAppLogger logger,
        CancellationToken cancellationToken = default)
        => CompileAsync(sourceDirectory, llvmBinDirectory, buildDirectory, enabledPasses, discovery,
                        new LlvmCompileOptions(), logger, cancellationToken);

    public static async Task<CppFileConversionResult> CompileAsync(
        string sourceDirectory,
        string llvmBinDirectory,
        string buildDirectory,
        IReadOnlyList<LlvmPassDefinition> enabledPasses,
        CompilerToolDiscoveryResult? discovery,
        LlvmCompileOptions options,
        IAppLogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentNullException.ThrowIfNull(llvmBinDirectory);
        ArgumentNullException.ThrowIfNull(buildDirectory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        CppFileConversionResult Fail(string msg)
        {
            logger.Warn(msg);
            return new CppFileConversionResult(false, msg);
        }

        if (!Directory.Exists(sourceDirectory))
            return Fail($"LLVM: Source directory not found: {sourceDirectory}");

        if (!Directory.Exists(llvmBinDirectory))
            return Fail($"LLVM: Bundled LLVM bin directory not found: {llvmBinDirectory}. " +
                        "Run a build to ensure Tools\\LLVM\\bin\\ is populated.");

        var sources = Directory.GetFiles(sourceDirectory, "*.cpp", SearchOption.TopDirectoryOnly);
        if (sources.Length == 0)
            return Fail("LLVM: No .cpp files found to compile.");

        Directory.CreateDirectory(buildDirectory);

        bool isDll = SourceContainsDllMain(sources);
        string outputExt = isDll ? ".dll" : ".exe";
        var tempExe = Path.Combine(buildDirectory, $"llvm-build-{Guid.NewGuid():N}{outputExt}");
        var extraLibs = DetectRequiredLibraries(sources);
        var staticLibs = Directory.GetFiles(sourceDirectory, "*.lib", SearchOption.TopDirectoryOnly);

        // Passes are scheduled by name through pass-runner.exe (the monolithic
        // tool that statically links every pass). We no longer load individual
        // pass.dll files — the standalone DLL route is ABI-broken on Windows
        // and is documented as deprecated in Assets/llvm-passes/README.md.
        var passPipeline = string.Join(",", enabledPasses
            .Where(p => p.IsBuilt && !string.IsNullOrWhiteSpace(p.OptPassName))
            .Select(p => p.OptPassName!));

        if (enabledPasses.Count > 0 && string.IsNullOrWhiteSpace(passPipeline))
            logger.Warn("LLVM: Enabled passes have no opt_name in pass.json — compiling without obfuscation.");
        else if (!string.IsNullOrWhiteSpace(passPipeline))
            logger.Info($"LLVM: Scheduling {enabledPasses.Count} obfuscation pass(es) via pass-runner: " +
                        string.Join(", ", enabledPasses.Select(p => p.Name)));

        // Honor the forced-toolchain selection, else auto-detect MSVC vs MinGW.
        var toolchain = (options.Toolchain ?? "auto").ToLowerInvariant();
        var vcVarsScript = FindVcVarsScript(discovery);

        bool useClangCl = toolchain switch
        {
            "clang-cl" => true,
            "clang++"  => false,
            _          => vcVarsScript is not null,
        };

        if (useClangCl && vcVarsScript is null)
        {
            logger.Warn("LLVM: clang-cl forced but no MSVC vcvars script was found. " +
                        "Install Visual Studio Build Tools or pick a different toolchain mode.");
            return Fail("LLVM: clang-cl requires MSVC (vcvars). None detected.");
        }

        if (useClangCl)
        {
            logger.Info("LLVM: Using clang-cl (MSVC-compatible) toolchain.");

            // Derive pass-runner.exe path from LLVM bin directory structure:
            // llvmBinDirectory = <exe>/Tools/LLVM/bin
            // passRunner       = <exe>/Assets/llvm-passes/pass-runner.exe
            var exeDir = Path.GetFullPath(Path.Combine(llvmBinDirectory, "..", "..", ".."));
            var passRunnerExe = Path.Combine(exeDir, "Assets", "llvm-passes", "pass-runner.exe");

            return await CompileWithClangClAsync(
                sources, tempExe, extraLibs, staticLibs, isDll,
                passPipeline, passRunnerExe,
                vcVarsScript!, llvmBinDirectory,
                buildDirectory, options, logger, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(passPipeline))
        {
            logger.Warn("LLVM: clang++ MinGW path does not apply obfuscation passes (pass-runner needs the clang-cl/MSVC path). " +
                        "Compiling without passes.");
        }
        logger.Info("LLVM: Using clang++ with MinGW target.");
        return await CompileWithClangPlusPlusAsync(
            sources, tempExe, extraLibs, staticLibs, isDll,
            llvmBinDirectory,
            buildDirectory, options, logger, cancellationToken);
    }

    // ─── MSVC path (clang-cl.exe) ────────────────────────────────────────────

    private static async Task<CppFileConversionResult> CompileWithClangClAsync(
        string[] sources,
        string outputExe,
        IReadOnlyList<string> extraLibs,
        string[] staticLibs,
        bool isDll,
        string passPipeline,
        string passRunnerExe,
        string vcVarsScript,
        string llvmBinDir,
        string buildDir,
        LlvmCompileOptions opt,
        IAppLogger logger,
        CancellationToken ct)
    {
        var clangCl = Path.Combine(llvmBinDir, ClangClExe);
        if (!File.Exists(clangCl))
            return new CppFileConversionResult(false, $"LLVM: {ClangClExe} not found at '{clangCl}'.");

        // When passes are present, use the 3-step ABI-safe pipeline:
        //   1. clang-cl emits unoptimized LLVM bitcode (MSVC C++ ABI IR, no crash risk)
        //   2. pass-runner.exe applies obfuscation passes (statically linked)
        //   3. clang-cl compiles + optimizes obfuscated bitcode and links the final binary
        if (!string.IsNullOrWhiteSpace(passPipeline))
        {
            if (!File.Exists(passRunnerExe))
                return new CppFileConversionResult(false,
                    $"LLVM: pass-runner.exe not found at '{passRunnerExe}'. " +
                    "Run Assets/llvm-passes/build-all.ps1 to build it.");

            return await CompileWithPassRunnerAsync(
                sources, outputExe, extraLibs, staticLibs, isDll,
                passPipeline, passRunnerExe,
                vcVarsScript, clangCl, buildDir, opt, logger, ct);
        }

        // ── Single-step path (no obfuscation passes) ──────────────────────────
        string optFlag = TranslateClFromOpt(opt.OptLevel);
        string cppStd  = $"/std:c++{opt.CppStandard}";
        string subsystem = isDll
            ? "/DLL"
            : opt.Subsystem.Equals("console", StringComparison.OrdinalIgnoreCase)
                ? "/SUBSYSTEM:CONSOLE"
                : "/SUBSYSTEM:WINDOWS /entry:mainCRTStartup";

        var sb = new StringBuilder();
        sb.Append($"/nologo {optFlag} /Gy /EHsc {cppStd} ");
        if (opt.DebugInfo) sb.Append("/Z7 ");
        foreach (var define in opt.Defines)
            sb.Append($"/D{define} ");

        // LTO with clang-cl requires the lld linker
        if (opt.Lto)
            sb.Append("/clang:-flto /clang:-fuse-ld=lld ");

        foreach (var flag in opt.ExtraFlags)
            sb.Append($"/clang:{flag} ");

        sb.Append($"/Fe:{Q(outputExe)} ");
        sb.Append(string.Join(" ", sources.Select(Q)));
        sb.Append($" /link /OPT:REF /OPT:ICF /INCREMENTAL:NO {subsystem}");
        sb.Append(" kernel32.lib user32.lib gdi32.lib advapi32.lib shell32.lib ole32.lib");
        sb.Append(" comdlg32.lib ntdll.lib");

        foreach (var lib in extraLibs.Where(l => l.StartsWith("-l", StringComparison.Ordinal)))
            sb.Append($" {lib[2..]}.lib");

        foreach (var lib in staticLibs)
            sb.Append($" {Q(lib)}");

        var argsStr = sb.ToString().Trim();
        logger.Debug($"LLVM clang-cl args: {Truncate(argsStr, 600)}");

        var cmdLine = $"cmd.exe /c \"\"{vcVarsScript}\" && \"{clangCl}\" {argsStr}\"";
        return await RunCompilerAsync(cmdLine, buildDir, outputExe, "clang-cl", logger, ct);
    }

    // ── 3-step pipeline (clang-cl with obfuscation passes) ───────────────────
    //
    // Step 1: clang-cl + vcvars → emit unoptimized LLVM bitcode (.bc) per source
    // Step 2: pass-runner.exe  → apply obfuscation passes (MinGW ABI, no crash)
    // Step 3: clang-cl + vcvars → optimize + compile + link all .obf.bc → EXE
    //
    // This avoids the C++ ABI mismatch crash that occurs when MSVC-ABI clang-cl
    // tries to call into GNU-ABI pass DLLs via -fpass-plugin.
    private static async Task<CppFileConversionResult> CompileWithPassRunnerAsync(
        string[] sources,
        string outputExe,
        IReadOnlyList<string> extraLibs,
        string[] staticLibs,
        bool isDll,
        string passPipeline,
        string passRunnerExe,
        string vcVarsScript,
        string clangCl,
        string buildDir,
        LlvmCompileOptions opt,
        IAppLogger logger,
        CancellationToken ct)
    {
        string cppStd = $"/std:c++{opt.CppStandard}";
        string subsystem = isDll
            ? "/DLL"
            : opt.Subsystem.Equals("console", StringComparison.OrdinalIgnoreCase)
                ? "/SUBSYSTEM:CONSOLE"
                : "/SUBSYSTEM:WINDOWS /entry:mainCRTStartup";

        // ── Reproducibility & forensic logging ────────────────────────────
        // 1. Pin OBFUSCATION_SEED for pass-runner so the IR transformations
        //    are reproducible from the manifest (and so we know what was
        //    used even when the caller left it unset).
        // 2. Snapshot pre/post-pass bitcode into <buildDir>/ir/ — the
        //    working *.bc files in buildDir root are still deleted at the
        //    end of the run; the snapshots survive.
        // 3. Write <buildDir>/llvm_pipeline.json with seed, plugins,
        //    runner, per-step timings + exit codes, and artifact hashes —
        //    even on partial failure.
        var (seedValue, seedSource) = ResolveSeed();
        logger.Info($"LLVM obfuscation seed: {seedValue} (source: {seedSource})");

        var irDir = Path.Combine(buildDir, "ir");
        Directory.CreateDirectory(irDir);
        var llvmBinDir = Path.GetDirectoryName(clangCl) ?? string.Empty;

        var manifestSteps     = new List<Dictionary<string, object?>>();
        var manifestArtifacts = new Dictionary<string, object>();
        var pipelineStart     = DateTime.UtcNow;
        string? manifestError = null;
        CppFileConversionResult? result = null;

        // Base flags for step 1 (frontend only, no optimization — passes apply after)
        var step1Base = new StringBuilder();
        step1Base.Append($"/nologo /Od /Gy /EHsc {cppStd} ");
        if (opt.DebugInfo) step1Base.Append("/Z7 ");
        foreach (var define in opt.Defines)
            step1Base.Append($"/D{define} ");
        step1Base.Append("/c /clang:-emit-llvm ");

        var bcFiles    = new List<string>(sources.Length);
        var obfBcFiles = new List<string>(sources.Length);

        try
        {
            // Step 1: emit unoptimized bitcode for every source file
            for (int i = 0; i < sources.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var bcPath = Path.Combine(buildDir, $"src_{i}.bc");
                bcFiles.Add(bcPath);

                var step1Args = $"{step1Base}{Q(sources[i])}";
                logger.Debug($"LLVM clang-cl [step 1, src {i}]: {Truncate(step1Args, 400)}");

                // clang-cl outputs <basename>.bc in the working directory;
                // we want it at the explicit bcPath, so rename after compile.
                var stem    = Path.GetFileNameWithoutExtension(sources[i]);
                var autoOut = Path.Combine(buildDir, $"{stem}.bc");

                var cmd1 = $"cmd.exe /c \"\"{vcVarsScript}\" && \"{clangCl}\" {step1Args}\"";
                var sw1  = System.Diagnostics.Stopwatch.StartNew();
                var (s1ok, s1err) = await RunIntermediateAsync(cmd1, buildDir, autoOut, "clang-cl[emit-bc]", logger, ct);
                sw1.Stop();
                manifestSteps.Add(new() {
                    ["step"] = 1, ["tool"] = "clang-cl[emit-bc]",
                    ["source"] = stem, ["ok"] = s1ok, ["durationMs"] = sw1.ElapsedMilliseconds,
                });
                if (!s1ok)
                {
                    result = new CppFileConversionResult(false,
                        $"LLVM: step 1 (emit bitcode) failed for source {i}: {s1err}");
                    return result;
                }

                // Rename the auto-named output to our indexed name to avoid collisions
                if (!autoOut.Equals(bcPath, StringComparison.OrdinalIgnoreCase))
                    File.Move(autoOut, bcPath, overwrite: true);

                // Snapshot the pre-pass IR into ir/<stem>.pre.bc so it survives cleanup
                var preSnap = Path.Combine(irDir, $"{stem}.pre.bc");
                File.Copy(bcPath, preSnap, overwrite: true);
                manifestArtifacts[ToRelKey(buildDir, preSnap)] = FileMeta(preSnap);
                TryDisassemble(llvmBinDir, preSnap, Path.ChangeExtension(preSnap, ".ll"), logger);
            }

            // Step 2: apply obfuscation passes to each bitcode file
            for (int i = 0; i < bcFiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var bcPath  = bcFiles[i];
                var obfPath = Path.ChangeExtension(bcPath, ".obf.bc");
                obfBcFiles.Add(obfPath);
                var stem    = Path.GetFileNameWithoutExtension(sources[i]);

                var psi2 = new ProcessStartInfo
                {
                    FileName = passRunnerExe,
                    WorkingDirectory = buildDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi2.ArgumentList.Add($"-passes={passPipeline}");
                psi2.ArgumentList.Add(bcPath);
                psi2.ArgumentList.Add("-o");
                psi2.ArgumentList.Add(obfPath);
                // Pin OBFUSCATION_SEED for the child only; the parent env
                // is unmodified. With UseShellExecute=false, EnvironmentVariables
                // starts as a copy of the parent's environment.
                psi2.EnvironmentVariables["OBFUSCATION_SEED"] = seedValue;

                // The time-stretch pass reads this; harmless for the others.
                // Always set so the value can't leak in from the parent shell.
                var slow = opt.SlowdownLevel;
                if (slow < 0) slow = 0;
                if (slow > 100) slow = 100;
                psi2.EnvironmentVariables["SLOWDOWN_LEVEL"] = slow.ToString(System.Globalization.CultureInfo.InvariantCulture);

                logger.Debug($"LLVM pass-runner [step 2, src {i}]: -passes={passPipeline}");
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                var (s2ok, s2err) = await RunPassRunnerAsync(psi2, obfPath, "pass-runner", logger, ct);
                sw2.Stop();
                manifestSteps.Add(new() {
                    ["step"] = 2, ["tool"] = "pass-runner",
                    ["source"] = stem, ["passes"] = passPipeline,
                    ["ok"] = s2ok, ["durationMs"] = sw2.ElapsedMilliseconds,
                });
                if (!s2ok)
                {
                    result = new CppFileConversionResult(false,
                        $"LLVM: step 2 (obfuscate) failed for source {i}: {s2err}");
                    return result;
                }

                // Snapshot the post-pass IR into ir/<stem>.post.bc
                var postSnap = Path.Combine(irDir, $"{stem}.post.bc");
                File.Copy(obfPath, postSnap, overwrite: true);
                manifestArtifacts[ToRelKey(buildDir, postSnap)] = FileMeta(postSnap);
                TryDisassemble(llvmBinDir, postSnap, Path.ChangeExtension(postSnap, ".ll"), logger);
            }

            // Step 3: optimize + compile obfuscated bitcode and link
            // Running the LLVM optimizer here (after obfuscation) matches the EP-callback
            // behavior of -fpass-plugin where late optimization runs after our passes.
            string optFlag = TranslateClFromOpt(opt.OptLevel);
            var sb3 = new StringBuilder();
            sb3.Append($"/nologo {optFlag} /Gy /EHsc {cppStd} ");
            foreach (var flag in opt.ExtraFlags)
                sb3.Append($"/clang:{flag} ");

            sb3.Append($"/Fe:{Q(outputExe)} ");
            sb3.Append(string.Join(" ", obfBcFiles.Select(Q)));
            sb3.Append($" /link /OPT:REF /OPT:ICF /INCREMENTAL:NO {subsystem}");
            sb3.Append(" kernel32.lib user32.lib gdi32.lib advapi32.lib shell32.lib ole32.lib");
            sb3.Append(" comdlg32.lib ntdll.lib");

            foreach (var lib in extraLibs.Where(l => l.StartsWith("-l", StringComparison.Ordinal)))
                sb3.Append($" {lib[2..]}.lib");
            foreach (var lib in staticLibs)
                sb3.Append($" {Q(lib)}");

            var step3Args = sb3.ToString().Trim();
            logger.Debug($"LLVM clang-cl [step 3 link]: {Truncate(step3Args, 600)}");

            var cmd3 = $"cmd.exe /c \"\"{vcVarsScript}\" && \"{clangCl}\" {step3Args}\"";
            var sw3  = System.Diagnostics.Stopwatch.StartNew();
            result   = await RunCompilerAsync(cmd3, buildDir, outputExe, "clang-cl[link]", logger, ct);
            sw3.Stop();
            manifestSteps.Add(new() {
                ["step"] = 3, ["tool"] = "clang-cl[link]",
                ["ok"] = result.Success, ["durationMs"] = sw3.ElapsedMilliseconds,
                ["outputPath"] = result.OutputExePath,
            });
            return result;
        }
        catch (Exception ex)
        {
            manifestError = ex.Message;
            throw;
        }
        finally
        {
            // Clean up the working .bc files in buildDir root; the IR
            // snapshots in ir/ persist.
            foreach (var f in bcFiles.Concat(obfBcFiles))
            {
                try { File.Delete(f); } catch { }
            }

            // Write the manifest unconditionally so even partial failures
            // leave a record of what was tried, which seed was used, and
            // what intermediate artifacts were captured.
            try
            {
                WritePipelineManifest(
                    buildDir, seedValue, seedSource, passPipeline,
                    passRunnerExe, clangCl, vcVarsScript,
                    opt, manifestSteps, manifestArtifacts,
                    pipelineStart, manifestError, result?.Success, logger);
            }
            catch (Exception ex)
            {
                logger.Warn($"LLVM: failed to write pipeline manifest: {ex.Message}");
            }
        }
    }

    // ─── Manifest / IR helpers ────────────────────────────────────────────

    /// <summary>
    /// Resolves the obfuscation seed for the pass-runner child process.
    /// If OBFUSCATION_SEED is already set in the parent environment we
    /// reuse it (and report "env"); otherwise we generate one with
    /// crypto-grade entropy and report "generated". Either way the seed
    /// is logged so a future run with the same value reproduces the IR.
    /// </summary>
    private static (string Value, string Source) ResolveSeed()
    {
        var existing = Environment.GetEnvironmentVariable("OBFUSCATION_SEED");
        if (!string.IsNullOrWhiteSpace(existing) && ulong.TryParse(existing, out _))
            return (existing!, "env");

        Span<byte> b = stackalloc byte[8];
        RandomNumberGenerator.Fill(b);
        // Clear the top bit so the result fits in a signed int64 string too
        b[0] &= 0x7F;
        ulong v = BitConverter.ToUInt64(b);
        if (v == 0) v = 1;
        return (v.ToString(System.Globalization.CultureInfo.InvariantCulture), "generated");
    }

    private static string ToRelKey(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static Dictionary<string, object?> FileMeta(string path)
    {
        var fi = new FileInfo(path);
        return new()
        {
            ["size"] = fi.Length,
            ["sha256"] = HashFile(path),
        };
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>
    /// Best-effort .bc → .ll disassembly so humans can read the IR. Uses
    /// llvm-dis.exe if it's in the LLVM bin dir; silently skips if not.
    /// </summary>
    private static void TryDisassemble(string llvmBinDir, string bcPath, string llPath, IAppLogger logger)
    {
        if (string.IsNullOrWhiteSpace(llvmBinDir)) return;
        var dis = Path.Combine(llvmBinDir, "llvm-dis.exe");
        if (!File.Exists(dis)) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = dis,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            psi.ArgumentList.Add(bcPath);
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(llPath);
            using var p = Process.Start(psi);
            if (p is null) return;
            if (!p.WaitForExit(10_000))
            {
                try { p.Kill(); } catch { }
                logger.Debug($"llvm-dis timed out on {Path.GetFileName(bcPath)}");
            }
        }
        catch (Exception ex)
        {
            logger.Debug($"llvm-dis on {Path.GetFileName(bcPath)} failed: {ex.Message}");
        }
    }

    private static void WritePipelineManifest(
        string buildDir,
        string seedValue, string seedSource,
        string passPipeline,
        string passRunnerExe,
        string clangCl, string vcVarsScript,
        LlvmCompileOptions opt,
        IReadOnlyList<Dictionary<string, object?>> steps,
        IReadOnlyDictionary<string, object> artifacts,
        DateTime startedUtc, string? error, bool? success,
        IAppLogger logger)
    {
        var manifest = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["startedUtc"]    = startedUtc.ToString("o"),
            ["finishedUtc"]   = DateTime.UtcNow.ToString("o"),
            ["success"]       = success,
            ["error"]         = error,
            ["seed"]          = new { value = seedValue, source = seedSource, envVar = "OBFUSCATION_SEED" },
            ["passes"]        = passPipeline.Split(',', StringSplitOptions.RemoveEmptyEntries),
            ["passRunner"]    = new
            {
                path   = passRunnerExe,
                exists = File.Exists(passRunnerExe),
                size   = File.Exists(passRunnerExe) ? new FileInfo(passRunnerExe).Length : -1L,
                sha256 = File.Exists(passRunnerExe) ? HashFile(passRunnerExe) : null,
            },
            ["toolchain"]     = new { clangCl, vcvars64 = vcVarsScript },
            ["compileOptions"] = new
            {
                opt.OptLevel, opt.Arch, opt.Subsystem,
                cppStandard = opt.CppStandard,
                opt.StripSymbols, opt.Lto, opt.NoGcSections, opt.DebugInfo,
                opt.SlowdownLevel,
                defines    = opt.Defines.ToArray(),
                extraFlags = opt.ExtraFlags.ToArray(),
            },
            ["steps"]         = steps,
            ["artifacts"]     = artifacts,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(manifest,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var manifestPath = Path.Combine(buildDir, "llvm_pipeline.json");
        File.WriteAllText(manifestPath, json);
        logger.Info($"LLVM pipeline manifest: {Path.GetRelativePath(buildDir, manifestPath)}");
    }

    // ─── MinGW path (clang++.exe) ────────────────────────────────────────────

    private static async Task<CppFileConversionResult> CompileWithClangPlusPlusAsync(
        string[] sources,
        string outputExe,
        IReadOnlyList<string> extraLibs,
        string[] staticLibs,
        bool isDll,
        string llvmBinDir,
        string buildDir,
        LlvmCompileOptions opt,
        IAppLogger logger,
        CancellationToken ct)
    {
        var clangPP = Path.Combine(llvmBinDir, ClangPlusPlusExe);
        if (!File.Exists(clangPP))
            return new CppFileConversionResult(false, $"LLVM: {ClangPlusPlusExe} not found at '{clangPP}'.");

        var target = opt.Arch.Equals("x86", StringComparison.OrdinalIgnoreCase)
            ? "i686-w64-mingw32"
            : "x86_64-w64-mingw32";

        var args = new List<string>
        {
            "-target", target,
            $"-{opt.OptLevel}",
            $"-std=c++{opt.CppStandard}",
        };

        if (opt.StripSymbols) args.Add("-s");
        if (opt.DebugInfo)    args.Add("-g");
        if (opt.Lto)          args.Add("-flto");
        if (!opt.NoGcSections)
        {
            args.AddRange(["-ffunction-sections", "-fdata-sections", "-Wl,--gc-sections"]);
        }

        foreach (var define in opt.Defines)
            args.Add($"-D{define}");

        // Note: -fpass-plugin used to be injected here when pass.dll files
        // shipped. Those DLLs are no longer in the payload (the monolithic
        // pass-runner.exe handles obfuscation via the clang-cl path). The
        // MinGW clang++ fallback compiles only — no obfuscation.

        if (isDll)
        {
            args.Add("-shared");
        }
        else
        {
            args.Add("-static-libgcc");
            var subsystem = opt.Subsystem.Equals("console", StringComparison.OrdinalIgnoreCase)
                ? "console" : "windows";
            args.Add($"-Wl,--subsystem,{subsystem}");
        }

        foreach (var flag in opt.ExtraFlags)
            args.Add(flag);

        args.AddRange(["-o", outputExe]);
        args.AddRange(sources);
        args.AddRange(extraLibs);
        args.AddRange(staticLibs);

        var argPreview = string.Join(" ", args.Select(QuoteArgForLog));
        logger.Debug($"LLVM clang++ args: {Truncate(argPreview, 600)}");

        var psi = new ProcessStartInfo
        {
            FileName = clangPP,
            WorkingDirectory = buildDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        return await RunCompilerProcessAsync(psi, outputExe, "clang++", logger, ct);
    }

    /// <summary>Maps the universal opt token to a clang-cl /O flag. Defaults to /O2.</summary>
    private static string TranslateClFromOpt(string opt) => opt.ToUpperInvariant() switch
    {
        "O0" => "/Od",
        "O1" => "/O1",
        "O2" => "/O2",
        "O3" => "/O2 /clang:-O3",
        "OS" => "/Os",
        "OZ" => "/Os /clang:-Oz",
        _    => "/O2",
    };

    // ─── Process runners ─────────────────────────────────────────────────────

    private static async Task<CppFileConversionResult> RunCompilerAsync(
        string cmdLine,
        string workingDir,
        string expectedOutput,
        string compilerLabel,
        IAppLogger logger,
        CancellationToken ct)
    {
        logger.Debug($"LLVM {compilerLabel}: {Truncate(cmdLine, 300)}");

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/S /C \"{cmdLine}\"",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        return await RunCompilerProcessAsync(psi, expectedOutput, compilerLabel, logger, ct);
    }

    private static async Task<CppFileConversionResult> RunCompilerProcessAsync(
        ProcessStartInfo psi,
        string expectedOutputPath,
        string compilerLabel,
        IAppLogger logger,
        CancellationToken ct)
    {
        using var process = new Process { StartInfo = psi };

        if (!process.Start())
            return new CppFileConversionResult(false, $"LLVM: Failed to start {compilerLabel} process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        var combinedOutput = string.IsNullOrWhiteSpace(stdout) ? stderr :
                             string.IsNullOrWhiteSpace(stderr) ? stdout :
                             $"{stdout}{Environment.NewLine}{stderr}";

        if (!string.IsNullOrWhiteSpace(stdout))
            logger.Debug($"LLVM {compilerLabel} stdout: {Truncate(stdout, 800)}");
        if (!string.IsNullOrWhiteSpace(stderr))
            logger.Debug($"LLVM {compilerLabel} stderr: {Truncate(stderr, 800)}");

        if (process.ExitCode != 0)
        {
            var errorMsg = string.IsNullOrWhiteSpace(combinedOutput)
                ? $"LLVM {compilerLabel} exited with code {process.ExitCode}."
                : $"LLVM {compilerLabel} failed (exit {process.ExitCode}):\n{combinedOutput}";
            logger.Warn(errorMsg);
            return new CppFileConversionResult(false, errorMsg,
                CompilerStdout: stdout, CompilerStderr: stderr);
        }

        if (!File.Exists(expectedOutputPath))
        {
            var msg = $"LLVM {compilerLabel} exited cleanly but produced no output at '{expectedOutputPath}'.";
            logger.Warn(msg);
            return new CppFileConversionResult(false, msg,
                CompilerStdout: stdout, CompilerStderr: stderr);
        }

        // Hash and rename to final deterministic filename
        var finalPath = await HashAndRenameAsync(expectedOutputPath, logger).ConfigureAwait(false);
        logger.Ok($"LLVM build succeeded: {Path.GetFileName(finalPath)}");
        return new CppFileConversionResult(true, null,
            OutputExePath: finalPath, CompilerStdout: stdout, CompilerStderr: stderr);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches the compiler discovery result and known environment variables for
    /// a vcvars64.bat bootstrap script. Returns null when MSVC is not available.
    /// </summary>
    private static string? FindVcVarsScript(CompilerToolDiscoveryResult? discovery)
    {
        // Walk up from the best MSVC candidate's path to find vcvars64.bat
        var best = discovery?.Best;
        if (best is not null && best.Kind.Equals("msvc", StringComparison.OrdinalIgnoreCase))
        {
            var searchPath = string.IsNullOrWhiteSpace(best.InstallationPath)
                ? Path.GetDirectoryName(best.Path)
                : best.InstallationPath;

            var found = WalkForVcVars(searchPath);
            if (found is not null) return found;
        }

        // Fallback: all candidates
        if (discovery?.Candidates is not null)
        {
            foreach (var c in discovery.Candidates.Where(c => c.Kind.Equals("msvc", StringComparison.OrdinalIgnoreCase)))
            {
                var searchPath = string.IsNullOrWhiteSpace(c.InstallationPath)
                    ? Path.GetDirectoryName(c.Path)
                    : c.InstallationPath;
                var found = WalkForVcVars(searchPath);
                if (found is not null) return found;
            }
        }

        // Env vars
        foreach (var envVar in new[] { "VSINSTALLDIR", "VCINSTALLDIR", "VCToolsInstallDir" })
        {
            var val = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(val))
            {
                var found = WalkForVcVars(val);
                if (found is not null) return found;
            }
        }

        return null;
    }

    private static string? WalkForVcVars(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;

        const string target = "vcvars64.bat";
        var current = root;
        while (!string.IsNullOrWhiteSpace(current))
        {
            try
            {
                var candidate = Directory.EnumerateFiles(current, target, SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (candidate is not null) return candidate;
            }
            catch { /* no permission or not a dir */ }

            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    private static bool SourceContainsDllMain(string[] sourceFiles)
    {
        foreach (var file in sourceFiles)
        {
            try
            {
                if (File.ReadAllText(file).Contains("DllMain", StringComparison.Ordinal))
                    return true;
            }
            catch { }
        }
        return false;
    }

    private static IReadOnlyList<string> DetectRequiredLibraries(string[] sourceFiles)
    {
        var libs = new List<string>();
        foreach (var file in sourceFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                if (content.Contains("#include <winhttp.h>", StringComparison.OrdinalIgnoreCase))
                {
                    if (!libs.Contains("-lwinhttp")) libs.Add("-lwinhttp");
                }
            }
            catch { }
        }
        return libs;
    }

    private static async Task<string> HashAndRenameAsync(string tempPath, IAppLogger logger)
    {
        var dir = Path.GetDirectoryName(tempPath) ?? Path.GetTempPath();
        var ext = Path.GetExtension(tempPath);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);

        string hash;
        var bytes = await File.ReadAllBytesAsync(tempPath).ConfigureAwait(false);
        hash = Convert.ToHexString(SHA256.HashData(bytes))[..5];

        var finalName = $"{timestamp}-{hash}{ext}";
        var finalPath = Path.Combine(dir, finalName);

        try { File.Move(tempPath, finalPath, overwrite: true); }
        catch (Exception ex)
        {
            logger.Warn($"LLVM: Could not rename output: {ex.Message}");
            return tempPath;
        }

        return finalPath;
    }

    private static string Q(string s) => $"\"{s}\"";

    private static string QuoteArgForLog(string s)
        => s.Contains(' ') ? $"\"{s}\"" : s;

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";

    /// <summary>
    /// Runs a shell command without hash-renaming the output. Used for intermediate steps
    /// (bitcode emit, pass-runner) where we need to control the output filename precisely.
    /// </summary>
    private static async Task<(bool Success, string Error)> RunIntermediateAsync(
        string cmd,
        string workingDir,
        string expectedOutput,
        string label,
        IAppLogger logger,
        CancellationToken ct)
    {
        logger.Debug($"LLVM {label}: {Truncate(cmd, 300)}");

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/S /C \"{cmd}\"",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            return (false, $"LLVM: Failed to start {label} process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(stdout)) logger.Debug($"LLVM {label} stdout: {Truncate(stdout, 400)}");
        if (!string.IsNullOrWhiteSpace(stderr)) logger.Debug($"LLVM {label} stderr: {Truncate(stderr, 400)}");

        if (process.ExitCode != 0)
        {
            var output = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
            var msg = $"LLVM {label} failed (exit {process.ExitCode}):\n{output}";
            logger.Warn(msg);
            return (false, msg);
        }

        if (!File.Exists(expectedOutput))
        {
            var msg = $"LLVM {label} exited cleanly but produced no output at '{expectedOutput}'.";
            logger.Warn(msg);
            return (false, msg);
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// Runs pass-runner.exe (a MinGW process, no vcvars needed) without hash-renaming.
    /// </summary>
    private static async Task<(bool Success, string Error)> RunPassRunnerAsync(
        ProcessStartInfo psi,
        string expectedOutput,
        string label,
        IAppLogger logger,
        CancellationToken ct)
    {
        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            return (false, $"LLVM: Failed to start {label} process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(stdout)) logger.Debug($"LLVM {label} stdout: {Truncate(stdout, 400)}");
        if (!string.IsNullOrWhiteSpace(stderr)) logger.Debug($"LLVM {label} stderr: {Truncate(stderr, 400)}");

        if (process.ExitCode != 0)
        {
            var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            var msg = $"LLVM {label} failed (exit {process.ExitCode}):\n{output}";
            logger.Warn(msg);
            return (false, msg);
        }

        if (!File.Exists(expectedOutput))
        {
            var msg = $"LLVM {label} exited cleanly but produced no output at '{expectedOutput}'.";
            logger.Warn(msg);
            return (false, msg);
        }

        return (true, string.Empty);
    }
}
