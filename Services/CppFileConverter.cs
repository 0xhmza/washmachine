using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Washmachine.Logging; // <-- same logger as your other code
using Washmachine.Models;

namespace Washmachine.Services;

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
        if (directory == null)
            throw new ArgumentNullException(nameof(directory));
        if (compilerDirectory == null)
            throw new ArgumentNullException(nameof(compilerDirectory));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));

        logger.Info($"Starting C++ conversion: dir='{directory}', compilerDir='{compilerDirectory}'");
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(directory))
        {
            string msg = $"Source directory not found: {directory}";
            logger.Warn(msg);
            return new CppFileConversionResult(false, msg);
        }

        if (!Directory.Exists(compilerDirectory))
        {
            string msg = $"Compiler directory not found: {compilerDirectory}";
            logger.Warn(msg);
            return new CppFileConversionResult(false, msg);
        }

        var sources = Directory.GetFiles(directory, "*.cpp", SearchOption.TopDirectoryOnly);
        if (sources.Length == 0)
        {
            string msg = "No .cpp files found to compile.";
            logger.Warn(msg);
            return new CppFileConversionResult(false, msg);
        }

        logger.Info($"Discovered {sources.Length} .cpp file(s) to compile. First few: {string.Join(", ", sources.Take(3).Select(Path.GetFileName))}{(sources.Length > 3 ? ", ..." : string.Empty)}");

        // Ensure output directory exists (exact casing/spaces requested)
        var outputDir = Path.Combine(directory, "Compiled BInaries");
        Directory.CreateDirectory(outputDir);
        logger.Info($"Output directory: {outputDir}");

        // Pick compiler
        var (compilerPath, family) = DetectCompiler(compilerDirectory);
        if (compilerPath == null)
        {
            const string msg = "No supported compiler found. Expected cl.exe, g++.exe, or clang++.exe in compilerDirectory.";
            logger.Warn(msg);
            return new CppFileConversionResult(false, msg);
        }

        logger.Info($"Detected compiler: '{compilerPath}' ({family})");

        // Build to a temporary exe name first, then hash and rename
        var tempExe = Path.Combine(outputDir, $"build-{Guid.NewGuid():N}.exe");
        var args = BuildCompilerArgs(family, sources, tempExe);
        var vcVarsScript = family == CompilerFamily.MSVC
            ? FindVcVarsScript(compilerDirectory)
            : null;

        logger.Info($"Compiler args: {Truncate(args, 600)}");
        logger.Info("Launching compiler process...");

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
                Arguments = args,
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
        }

        using var proc = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        try
        {
            if (!proc.Start())
            {
                const string msg = "Failed to start compiler process.";
                logger.Warn(msg);
                return new CppFileConversionResult(false, msg);
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

#if NET6_0_OR_GREATER
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
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
            if (File.Exists(tempExe)) SafeDelete(tempExe);
            throw;
        }
        catch (Exception ex)
        {
            TryKill(proc);
            if (File.Exists(tempExe)) SafeDelete(tempExe);
            string msg = $"Compiler failed to launch: {ex.Message}";
            logger.Warn(msg);
            return new CppFileConversionResult(false, msg);
        }

        logger.Info($"Compiler exited with code {proc.ExitCode}.");
        if (proc.ExitCode != 0 || !File.Exists(tempExe))
        {
            if (File.Exists(tempExe)) SafeDelete(tempExe);

            var msg = new StringBuilder();
            msg.AppendLine("Compilation failed.");
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
            logger.Info($"SHA-256 (first 5): {hashFirst5}");
        }
        catch (Exception ex)
        {
            SafeDelete(tempExe);
            string msg = $"Failed to hash output exe: {ex.Message}";
            logger.Warn(msg);
            return new CppFileConversionResult(false, msg);
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var finalExe = Path.Combine(outputDir, $"{timestamp}-{hashFirst5}.exe");

        try
        {
            if (File.Exists(finalExe))
            {
                logger.Warn($"Output file already existed, replacing: {finalExe}");
                File.Delete(finalExe); // unlikely collision; just replace
            }

            File.Move(tempExe, finalExe);
            logger.Ok($"Compilation succeeded. Output: {finalExe}");
        }
        catch (Exception ex)
        {
            SafeDelete(tempExe);
            string msg = $"Failed to finalize output exe: {ex.Message}";
            logger.Warn(msg);
            return new CppFileConversionResult(false, msg);
        }

        TryStripBinary(compilerPath, finalExe, logger);

        return new CppFileConversionResult(true, null);
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

    private static string BuildCompilerArgs(CompilerFamily family, string[] sources, string outputExe)
    {
        static string Q(string s) => $"\"{s}\"";

        if (family == CompilerFamily.MSVC)
        {
            var args = new List<string>
            {
                "/nologo",
                "/O1",
                "/Os",
                "/GS-",
                "/Gw",
                "/Gy",
                "/GL",
                "/Zc:inline",
                "/Zc:threadSafeInit-",
                "/DNDEBUG",
                "/DWIN32_LEAN_AND_MEAN",
                "/D_CRT_SECURE_NO_WARNINGS",
                "/GR-",
                "/EHsc",
                $"/Fe:{Q(outputExe)}"
            };

            args.AddRange(sources.Select(Q));
            args.Add("/link");
            args.Add("/NODEFAULTLIB");
            args.Add("/LTCG");
            args.Add("/OPT:REF");
            args.Add("/OPT:ICF");
            args.Add("/INCREMENTAL:NO");
            args.Add("/ENTRY:WinMainCRTStartup");
            args.Add("/SUBSYSTEM:WINDOWS");
            args.Add("kernel32.lib");
            args.Add("user32.lib");

            return string.Join(" ", args);
        }
        else
        {
            var args = new List<string>
            {
                "-Os",
                "-s",
                "-ffunction-sections",
                "-fdata-sections",
                "-fno-ident",
                "-fno-asynchronous-unwind-tables",
                "-fmerge-all-constants",
                "-fno-stack-protector",
                "-fvisibility=hidden",
                "-DNDEBUG",
                "-Wl,--gc-sections",
                "-Wl,--strip-all",
                "-o",
                Q(outputExe)
            };

            args.AddRange(sources.Select(Q));

            return string.Join(" ", args);
        }
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* ignore */ }
    }

    private static void SafeDelete(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }

    private static void TryStripBinary(string? compilerPath, string exePath, IAppLogger logger)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(compilerPath))
        {
            var dir = Path.GetDirectoryName(compilerPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                candidates.Add(Path.Combine(dir, "llvm-strip.exe"));
                candidates.Add(Path.Combine(dir, "strip.exe"));
                candidates.Add(Path.Combine(dir, "objcopy.exe"));
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var segment in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0)
                continue;

            candidates.Add(Path.Combine(trimmed, "llvm-strip.exe"));
            candidates.Add(Path.Combine(trimmed, "strip.exe"));
            candidates.Add(Path.Combine(trimmed, "objcopy.exe"));
        }

        string? tool = candidates.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(tool))
        {
            logger.Info("No strip utility found; skipping post-link trimming.");
            return;
        }

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = tool,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            if (tool.EndsWith("objcopy.exe", StringComparison.OrdinalIgnoreCase))
            {
                proc.StartInfo.ArgumentList.Add("--strip-unneeded");
            }
            else
            {
                proc.StartInfo.ArgumentList.Add("--strip-all");
            }

            proc.StartInfo.ArgumentList.Add(exePath);

            if (!proc.Start())
                return;

            proc.WaitForExit();
            if (proc.ExitCode == 0)
            {
                logger.Ok("Post-link stripping succeeded (binary trimmed).");
            }
            else
            {
                var err = proc.StandardError.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(err))
                    logger.Warn($"Strip utility exited with code {proc.ExitCode}: {Truncate(err, 300)}");
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"Unable to strip binary: {ex.Message}");
        }
    }

    private static string Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value.Substring(0, max) + "...");

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

