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

        var passPluginArgs = enabledPasses
            .Where(p => p.IsBuilt)
            .Select(p => p.PluginPath!)
            .ToList();

        if (enabledPasses.Count > 0 && passPluginArgs.Count == 0)
            logger.Warn("LLVM: All enabled passes are unbuilt — compiling without obfuscation. " +
                        "Build pass.dll stubs from Assets/llvm-passes/<id>/CMakeLists.txt.");
        else if (passPluginArgs.Count > 0)
            logger.Info($"LLVM: Loading {passPluginArgs.Count} obfuscation pass(es): " +
                        string.Join(", ", enabledPasses.Where(p => p.IsBuilt).Select(p => p.Name)));

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
            return await CompileWithClangClAsync(
                sources, tempExe, extraLibs, staticLibs, isDll,
                passPluginArgs, vcVarsScript!, llvmBinDirectory,
                buildDirectory, options, logger, cancellationToken);
        }

        logger.Info("LLVM: Using clang++ with MinGW target.");
        return await CompileWithClangPlusPlusAsync(
            sources, tempExe, extraLibs, staticLibs, isDll,
            passPluginArgs, llvmBinDirectory,
            buildDirectory, options, logger, cancellationToken);
    }

    // ─── MSVC path (clang-cl.exe) ────────────────────────────────────────────

    private static async Task<CppFileConversionResult> CompileWithClangClAsync(
        string[] sources,
        string outputExe,
        IReadOnlyList<string> extraLibs,
        string[] staticLibs,
        bool isDll,
        IReadOnlyList<string> passPlugins,
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

        // Translate the universal option model into clang-cl's MSVC-style flags.
        string optFlag = TranslateClFromOpt(opt.OptLevel);          // /O1, /O2, /Od, /Os
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

        // Plugin flags route through /clang: when going through clang-cl
        foreach (var dll in passPlugins)
            sb.Append($"/clang:-fpass-plugin={Q(dll)} ");

        if (opt.Lto)
            sb.Append("/clang:-flto ");

        foreach (var flag in opt.ExtraFlags)
            sb.Append($"/clang:{flag} ");

        sb.Append($"/Fe:{Q(outputExe)} ");
        sb.Append(string.Join(" ", sources.Select(Q)));
        sb.Append($" /link /OPT:REF /OPT:ICF /INCREMENTAL:NO {subsystem}");
        sb.Append(" kernel32.lib user32.lib gdi32.lib advapi32.lib shell32.lib ole32.lib");
        sb.Append(" comdlg32.lib ntdll.lib");

        // Extra libs detected from source (e.g. winhttp)
        foreach (var lib in extraLibs.Where(l => l.StartsWith("-l", StringComparison.Ordinal)))
            sb.Append($" {lib[2..]}.lib");

        foreach (var lib in staticLibs)
            sb.Append($" {Q(lib)}");

        var argsStr = sb.ToString().Trim();
        logger.Debug($"LLVM clang-cl args: {Truncate(argsStr, 600)}");

        // Bootstrap MSVC environment then invoke clang-cl
        var cmdLine = $"cmd.exe /c \"\"{vcVarsScript}\" && \"{clangCl}\" {argsStr}\"";
        return await RunCompilerAsync(cmdLine, buildDir, outputExe, "clang-cl", logger, ct);
    }

    // ─── MinGW path (clang++.exe) ────────────────────────────────────────────

    private static async Task<CppFileConversionResult> CompileWithClangPlusPlusAsync(
        string[] sources,
        string outputExe,
        IReadOnlyList<string> extraLibs,
        string[] staticLibs,
        bool isDll,
        IReadOnlyList<string> passPlugins,
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

        foreach (var dll in passPlugins)
            args.AddRange(["-fpass-plugin", dll]);

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
            return new CppFileConversionResult(false, errorMsg, stdout, stderr);
        }

        if (!File.Exists(expectedOutputPath))
        {
            var msg = $"LLVM {compilerLabel} exited cleanly but produced no output at '{expectedOutputPath}'.";
            logger.Warn(msg);
            return new CppFileConversionResult(false, msg, stdout, stderr);
        }

        // Hash and rename to final deterministic filename
        var finalPath = await HashAndRenameAsync(expectedOutputPath, logger).ConfigureAwait(false);
        logger.Ok($"LLVM build succeeded: {Path.GetFileName(finalPath)}");
        return new CppFileConversionResult(true, null, stdout, stderr, finalPath);
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
}
