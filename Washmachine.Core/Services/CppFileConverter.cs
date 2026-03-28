using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Washmachine.Logging; // <-- same logger as your other code
using Washmachine.Models;

namespace Washmachine.Services;

/// <summary>
/// Compiles .cpp sources into a single minimized executable.
/// </summary>
public static class CppFileConverter
{
    /// <summary>
    /// Compiles all .cpp files in <paramref name="directory"/> to a single minimized .exe using a compiler found in <paramref name="compilerDirectory"/>.
    /// The exe is written to "Compiled BInaries" and named "yyyyMMdd_HHmmss-xxxxx.exe" (xxxxx = first 5 chars of SHA-256 of the exe).
    /// </summary>
    public static async Task<CppFileConversionResult> ConvertAsync(
        string directory,
        string compilerDirectory,
        IAppLogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(compilerDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        CppFileConversionResult Fail(string message)
        {
            logger.Warn(message);
            return new CppFileConversionResult(false, message);
        }

        logger.Info($"Starting C++ conversion: dir='{directory}', compilerDir='{compilerDirectory}'");
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(directory))
        {
            string msg = $"Source directory not found: {directory}";
            return Fail(msg);
        }

        if (!Directory.Exists(compilerDirectory))
        {
            string msg = $"Compiler directory not found: {compilerDirectory}";
            return Fail(msg);
        }

        var sources = Directory.GetFiles(directory, "*.cpp", SearchOption.TopDirectoryOnly);
        if (sources.Length == 0)
        {
            string msg = "No .cpp files found to compile.";
            return Fail(msg);
        }

        logger.Debug($"Discovered {sources.Length} .cpp file(s) to compile. First few: {string.Join(", ", sources.Take(3).Select(Path.GetFileName))}{(sources.Length > 3 ? ", ..." : string.Empty)}");

        // Ensure output directory exists (exact casing/spaces requested)
        var outputDir = Path.Combine(directory, "Compiled BInaries");
        Directory.CreateDirectory(outputDir);
        logger.Debug($"Output directory: {outputDir}");

        // Pick compiler
        var (compilerPath, family) = DetectCompiler(compilerDirectory);
        if (compilerPath == null)
        {
            const string msg = "No supported compiler found. Expected cl.exe, g++.exe, or clang++.exe in compilerDirectory.";
            return Fail(msg);
        }

        logger.Debug($"Detected compiler: '{compilerPath}' ({family})");

        // Build to a temporary exe name first, then hash and rename after compilation succeeds.
        bool isDll = SourceContainsDllMain(sources);
        string outputExt = isDll ? ".dll" : ".exe";
        var tempExe = Path.Combine(outputDir, $"build-{Guid.NewGuid():N}{outputExt}");
        var extraLibs = DetectRequiredLibraries(sources);
        var staticLibs = Directory.GetFiles(directory, "*.lib", SearchOption.TopDirectoryOnly);
        var args = family == CompilerFamily.GCC_LIKE
            ? string.Empty
            : BuildCompilerArgs(family, sources, tempExe, extraLibs, staticLibs, isDll);
        var gccArgs = family == CompilerFamily.GCC_LIKE
            ? BuildGccCompilerArgList(sources, tempExe, extraLibs, staticLibs, isDll)
            : null;
        var vcVarsScript = family == CompilerFamily.MSVC
            ? FindVcVarsScript(compilerDirectory)
            : null;

        if (family == CompilerFamily.GCC_LIKE && gccArgs != null)
        {
            var argPreview = string.Join(" ", gccArgs.Select(QuoteArgForLog));
            logger.Debug($"Compiler args: {Truncate(argPreview, 600)}");
        }
        else
        {
            logger.Debug($"Compiler args: {Truncate(args, 600)}");
        }
        logger.Debug("Launching compiler process...");

        ProcessStartInfo startInfo;

        if (!string.IsNullOrWhiteSpace(vcVarsScript))
        {
            var cmdArgs = BuildCmdArguments(vcVarsScript!, compilerPath, args);
            startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArgs,
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            logger.Info($"Using MSVC environment bootstrap: {vcVarsScript}");
        }
        else
        {
            startInfo = new ProcessStartInfo
            {
                FileName = compilerPath,
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            if (family == CompilerFamily.GCC_LIKE && gccArgs != null)
            {
                foreach (var arg in gccArgs)
                    startInfo.ArgumentList.Add(arg);
            }
            else
            {
                startInfo.Arguments = args;
            }

            // Ensure GCC/Clang can resolve internal tools (cc1plus, as, collect2)
            // even when the parent process environment is minimal.
            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            startInfo.Environment["PATH"] = string.IsNullOrWhiteSpace(existingPath)
                ? compilerDirectory
                : $"{compilerDirectory}{Path.PathSeparator}{existingPath}";
        }

        using var proc = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        try
        {
            if (!proc.Start())
            {
                const string msg = "Failed to start compiler process.";
                return Fail(msg);
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

#if NET6_0_OR_GREATER
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            // Flush async output handlers before reading buffers.
            proc.WaitForExit();
#else
            while (!proc.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
#endif
        }
        catch (OperationCanceledException)
        {
            logger.Warn("Compilation cancelled by caller.");
            TryKill(proc);
            SafeDelete(tempExe);
            throw;
        }
        catch (Exception ex)
        {
            TryKill(proc);
            SafeDelete(tempExe);
            return Fail($"Compiler failed to launch: {ex.Message}");
        }

        logger.Debug($"Compiler exited with code {proc.ExitCode}.");
        if (proc.ExitCode != 0 || !File.Exists(tempExe))
        {
            SafeDelete(tempExe);

            var msg = new StringBuilder();
            msg.AppendLine("Compilation failed.");
            msg.AppendLine($"Compiler exit code: {proc.ExitCode}");
            if (stdOut.Length > 0) msg.AppendLine("-- stdout --").AppendLine(Truncate(stdOut.ToString(), 4000));
            if (stdErr.Length > 0) msg.AppendLine("-- stderr --").AppendLine(Truncate(stdErr.ToString(), 4000));

            var finalMsg = msg.ToString().TrimEnd();
            logger.Warn(Truncate(finalMsg, 4000));
            return new CppFileConversionResult(false, finalMsg);
        }

        // Compute SHA-256 of the produced exe, take first 5 hex chars
        string hashFirst5;
        try
        {
            using var fs = File.OpenRead(tempExe);
#if NET6_0_OR_GREATER
            var hashBytes = SHA256.HashData(fs);
#else
            using var sha = SHA256.Create();
            var hashBytes = sha.ComputeHash(fs);
#endif
            var hex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            hashFirst5 = hex.Substring(0, 5);
            logger.Debug($"SHA-256 (first 5): {hashFirst5}");
        }
        catch (Exception ex)
        {
            // Security software may block file reads — use a random suffix instead
            hashFirst5 = Guid.NewGuid().ToString("N")[..5];
            logger.Warn($"Hash skipped (security block?): {ex.Message}");
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var finalExe = Path.Combine(outputDir, $"{timestamp}-{hashFirst5}{outputExt}");

        try
        {
            if (File.Exists(finalExe))
            {
                logger.Warn($"Output file already existed, replacing: {finalExe}");
                File.Delete(finalExe);
            }

            File.Move(tempExe, finalExe);
            logger.Ok($"Build succeeded: {Path.GetFileName(finalExe)}");
        }
        catch (Exception ex)
        {
            // Move failed (security block?) — try to use temp exe directly
            if (File.Exists(tempExe))
            {
                logger.Warn($"Could not rename output ({ex.Message}), using temp path.");
                finalExe = tempExe;
                logger.Ok($"Build succeeded: {Path.GetFileName(finalExe)}");
            }
            else
            {
                return Fail($"Failed to finalize output exe: {ex.Message}");
            }
        }

        return new CppFileConversionResult(true, null, finalExe);
    }

    // ----- helpers -----

    private enum CompilerFamily { MSVC, GCC_LIKE }

    private static (string? path, CompilerFamily family) DetectCompiler(string compilerDirectory)
    {
        string Try(string file) => Path.Combine(compilerDirectory, file);

        var cl = Try("cl.exe");
        if (File.Exists(cl)) return (cl, CompilerFamily.MSVC);

        var gpp = Try("g++.exe");
        if (File.Exists(gpp)) return (gpp, CompilerFamily.GCC_LIKE);

        var clangpp = Try("clang++.exe");
        if (File.Exists(clangpp)) return (clangpp, CompilerFamily.GCC_LIKE);

        return (null, default);
    }

    private static string BuildCompilerArgs(CompilerFamily family, string[] sources, string outputExe, IReadOnlyList<string> extraLibs, string[] staticLibs, bool isDll)
    {
        static string Q(string s) => $"\"{s}\"";

        // When the source list is large, write a response file to avoid exceeding
        // the OS command-line length limit (~8192 chars on Windows).
        string src;
        string? responseFile = null;
        string rawSrc = string.Join(" ", sources.Select(Q));
        if (rawSrc.Length > 4000)
        {
            responseFile = Path.Combine(Path.GetDirectoryName(outputExe) ?? Path.GetTempPath(),
                $"sources_{Guid.NewGuid():N}.rsp");
            File.WriteAllText(responseFile, string.Join(Environment.NewLine, sources.Select(Q)));
            src = $"@\"{responseFile}\"";
        }
        else
        {
            src = rawSrc;
        }

        // Append static libraries (.lib files found in the source directory)
        var libArgs = staticLibs.Length > 0
            ? " " + string.Join(" ", staticLibs.Select(Q))
            : string.Empty;

        if (family == CompilerFamily.MSVC)
        {
            // MSVC: extra libs use #pragma comment(lib, ...) in the source; no args needed.
            // /std:c++17 required for std::wstring::data() non-const overload,
            // std::string_view, and [[maybe_unused]] used by bin2shell web helpers.
            // Standard Win32 libraries are listed explicitly to support linking against
            // pre-built static libs (.lib) where pragma-driven auto-linking doesn't propagate.
            return $"/nologo /O1 /Gy /DNDEBUG /EHsc /std:c++17 /Fe:{Q(outputExe)} {src}" +
                   $" /link /OPT:REF /OPT:ICF /INCREMENTAL:NO{(isDll ? " /DLL" : "")}{libArgs}" +
                   " kernel32.lib user32.lib gdi32.lib advapi32.lib shell32.lib ole32.lib" +
                   " comdlg32.lib ntdll.lib";
        }

        // GCC/Clang size-focused build: append -l flags for required libraries.
        // Avoid -static-libstdc++ — most loader templates are pure Win32 API and
        // the static C++ stdlib adds ~80-100 KB of unnecessary bloat.
        var libs = extraLibs.Count > 0 ? " " + string.Join(" ", extraLibs) : string.Empty;
        return $"-Os -s -std=c++17 -ffunction-sections -fdata-sections -Wl,--gc-sections -Wl,--subsystem,windows -DNDEBUG{(isDll ? " -shared" : " -static-libgcc")} -o {Q(outputExe)} {src}{libs}{libArgs}";
    }

    private static IReadOnlyList<string> BuildGccCompilerArgList(
        string[] sources,
        string outputExe,
        IReadOnlyList<string> extraLibs,
        string[] staticLibs,
        bool isDll)
    {
        var args = new List<string>
        {
            "-Os",
            "-s",
            "-std=c++17",
            "-ffunction-sections",
            "-fdata-sections",
            "-Wl,--gc-sections",
            "-DNDEBUG",
        };

        if (isDll)
        {
            args.Add("-shared");
        }
        else
        {
            args.Add("-static-libgcc");
        }

        args.Add("-Wl,--subsystem,windows");

        args.Add("-o");
        args.Add(outputExe);

        args.AddRange(sources);
        args.AddRange(extraLibs);
        args.AddRange(staticLibs);
        return args;
    }

    /// <summary>
    /// Checks if any source file contains a DllMain entry point, indicating a DLL build.
    /// </summary>
    private static bool SourceContainsDllMain(string[] sourceFiles)
    {
        foreach (var file in sourceFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                if (content.Contains("DllMain", StringComparison.Ordinal))
                    return true;
            }
            catch { /* ignore read errors */ }
        }
        return false;
    }

    /// <summary>
    /// Scans source files for known #include directives and returns the additional
    /// linker libraries required (e.g. -lwinhttp when winhttp.h is included).
    /// </summary>
    private static IReadOnlyList<string> DetectRequiredLibraries(string[] sourceFiles)
    {
        var libs = new List<string>();
        bool needsWinHttp = false;

        foreach (var file in sourceFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                if (content.Contains("#include <winhttp.h>", StringComparison.OrdinalIgnoreCase))
                    needsWinHttp = true;
            }
            catch { /* ignore read errors */ }
        }

        if (needsWinHttp)
        {
            libs.Add("-lwinhttp");
        }

        return libs;
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* ignore */ }
    }

    private static void SafeDelete(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }

    private static string Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value.Substring(0, max) + "...");

    private static string QuoteArgForLog(string arg)
        => arg.Contains(' ') ? $"\"{arg}\"" : arg;

    private static string? FindVcVarsScript(string compilerDirectory)
    {
        try
        {
            var dir = new DirectoryInfo(compilerDirectory);
            while (dir != null)
            {
                if (string.Equals(dir.Name, "VC", StringComparison.OrdinalIgnoreCase))
                {
                    var baseDir = dir.FullName;
                    var candidates = new[]
                    {
                        Path.Combine(baseDir, "Auxiliary", "Build", "vcvars64.bat"),
                        Path.Combine(baseDir, "Auxiliary", "Build", "vcvarsall.bat"),
                        Path.Combine(baseDir, "Auxiliary", "Build", "VsDevCmd.bat")
                    };

                    foreach (var candidate in candidates)
                    {
                        if (File.Exists(candidate))
                            return candidate;
                    }

                    break;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // ignore and fall back
        }

        return null;
    }

    private static string BuildCmdArguments(string scriptPath, string compilerPath, string compilerArgs)
    {
        var scriptName = Path.GetFileName(scriptPath);
        var scriptCall = $"\"{scriptPath}\"";

        if (string.Equals(scriptName, "vcvarsall.bat", StringComparison.OrdinalIgnoreCase))
        {
            scriptCall += " x64";
        }

        return $"/c \"{scriptCall} && \"{compilerPath}\" {compilerArgs}\"";
    }
}

