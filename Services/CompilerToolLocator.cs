using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Washmachine.Logging;
using Washmachine.Models;

namespace Washmachine.Services;

public interface ICompilerToolLocator
{
    Task<CompilerToolDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<CompilerToolDiscoveryResult> AddManualCandidateAsync(
        string path,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Lightweight compiler locator that prefers PATH and a few environment variables over heavy registry/vswhere scanning.
/// </summary>
public sealed class CompilerToolLocator : ICompilerToolLocator
{
    private static readonly string[] ExecutableNames = { "cl.exe", "clang++.exe", "g++.exe" };
    private static readonly string[] HintVariables = { "VCToolsInstallDir", "VCINSTALLDIR", "VSINSTALLDIR" };
    private static readonly string[] VsVersions = { "2022", "2019", "2017" };
    private static readonly string[] VsEditions = { "BuildTools", "Community", "Professional", "Enterprise" };
    private static readonly string[] LegacyVsVersions = { "14.0", "12.0", "11.0", "10.0" };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly IAppLogger _logger;
    private readonly HashSet<string> _manualCandidates = new(StringComparer.OrdinalIgnoreCase);
    private CompilerToolDiscoveryResult? _cached;

    public CompilerToolLocator(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<CompilerToolDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default)
        => Task.Run(DiscoverInternal, cancellationToken);

    public Task<CompilerToolDiscoveryResult> AddManualCandidateAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must be provided.", nameof(path));

        var normalized = Path.GetFullPath(path);
        if (!File.Exists(normalized))
            throw new FileNotFoundException("Specified compiler tool does not exist.", normalized);

        lock (_manualCandidates)
        {
            _manualCandidates.Add(normalized);
            _cached = null;
        }

        _logger.Info($"Manual compiler tool registered: {normalized}");
        return DiscoverAsync(cancellationToken);
    }

    private CompilerToolDiscoveryResult DiscoverInternal()
    {
        if (_cached != null)
        {
            _logger.Info("Using cached compiler discovery result.");
            return _cached;
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var manual in SnapshotManualPaths())
        {
            foreach (var path in ExpandCandidate(manual))
            {
                paths.Add(path);
            }
        }

        foreach (var envPath in FindFromHintVariables(errors))
        {
            paths.Add(envPath);
        }

        foreach (var vsPath in FindFromVisualStudioInstallations(errors))
        {
            paths.Add(vsPath);
        }

        foreach (var pathExe in FindOnPath(errors))
        {
            paths.Add(pathExe);
        }

        var candidates = paths
            .Select(CreateCandidate)
            .OrderBy(KindPriority)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var best = candidates.FirstOrDefault();

        var result = new CompilerToolDiscoveryResult
        {
            Best = best,
            Candidates = candidates,
            Errors = errors,
            Json = JsonSerializer.Serialize(new
            {
                best,
                candidates,
                errors
            }, JsonOptions)
        };

        _cached = result;
        LogSummary(result);
        return result;
    }

    private IEnumerable<string> SnapshotManualPaths()
    {
        lock (_manualCandidates)
        {
            return _manualCandidates.ToArray();
        }
    }

    private IEnumerable<string> ExpandCandidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            yield break;

        var ext = Path.GetExtension(path);
        if (ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            yield return path;

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                foreach (var exe in EnumerateCompilerExecutables(dir))
                    yield return exe;
            }

            yield break;
        }

        yield return path;
    }

    private IEnumerable<string> FindOnPath(ICollection<string> errors)
    {
        _ = errors;
        var raw = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var segments = raw.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            foreach (var name in ExecutableNames)
            {
                var candidate = Path.Combine(segment, name);
                if (File.Exists(candidate))
                    yield return candidate;
            }
        }
    }

    private IEnumerable<string> FindFromHintVariables(ICollection<string> errors)
    {
        _ = errors;
        foreach (var variable in HintVariables)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            foreach (var exe in EnumerateCompilerExecutables(value))
                yield return exe;
        }
    }

    private IEnumerable<string> FindFromVisualStudioInstallations(ICollection<string> errors)
    {
        foreach (var root in EnumerateVisualStudioRoots(errors))
        {
            foreach (var exe in EnumerateCompilerExecutables(root))
                yield return exe;
        }
    }

    private IEnumerable<string> EnumerateVisualStudioRoots(ICollection<string> errors)
    {
        foreach (var root in EnumerateFromVsWhere(errors))
            yield return root;

        foreach (var root in EnumerateKnownVsRoots())
            yield return root;
    }

    private IEnumerable<string> EnumerateFromVsWhere(ICollection<string> errors)
    {
        var vswhere = FindVsWhere();
        if (string.IsNullOrWhiteSpace(vswhere))
            yield break;

        string output;
        try
        {
            output = RunProcessCapture(
                vswhere,
                "-all -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath");
        }
        catch (Exception ex)
        {
            errors.Add($"vswhere failed: {ex.Message}");
            yield break;
        }

        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                yield return trimmed;
        }
    }

    private static string? FindVsWhere()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidate = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (File.Exists(candidate))
            return candidate;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        candidate = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (File.Exists(candidate))
            return candidate;

        return null;
    }

    private static string RunProcessCapture(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException($"Failed to start process: {fileName}");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException($"Process exited with code {process.ExitCode}: {stderr}");

        return stdout ?? string.Empty;
    }

    private static IEnumerable<string> EnumerateKnownVsRoots()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var baseDir = Path.Combine(programFilesX86, "Microsoft Visual Studio");

        foreach (var version in VsVersions)
        {
            foreach (var edition in VsEditions)
            {
                var candidate = Path.Combine(baseDir, version, edition);
                if (Directory.Exists(candidate))
                    yield return candidate;
            }
        }

        foreach (var legacyVersion in LegacyVsVersions)
        {
            var candidate = Path.Combine(programFilesX86, $"Microsoft Visual Studio {legacyVersion}");
            if (Directory.Exists(candidate))
                yield return candidate;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var altBase = Path.Combine(programFiles, "Microsoft Visual Studio");
        if (!string.Equals(altBase, baseDir, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var version in VsVersions)
            {
                foreach (var edition in VsEditions)
                {
                    var candidate = Path.Combine(altBase, version, edition);
                    if (Directory.Exists(candidate))
                        yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateCompilerExecutables(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            yield break;

        if (File.Exists(root))
        {
            yield return root;
            yield break;
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(root);
        }
        catch
        {
            yield break;
        }

        if (!Directory.Exists(normalized))
            yield break;

        foreach (var exe in EnumerateDirectExecutables(normalized))
            yield return exe;

        foreach (var exe in EnumerateMsvcExecutables(normalized))
            yield return exe;

        foreach (var exe in EnumerateLlvmExecutables(normalized))
            yield return exe;
    }

    private static IEnumerable<string> EnumerateDirectExecutables(string root)
    {
        foreach (var name in ExecutableNames)
        {
            var direct = Path.Combine(root, name);
            if (File.Exists(direct))
                yield return direct;
        }
    }

    private static IEnumerable<string> EnumerateMsvcExecutables(string root)
    {
        foreach (var exe in EnumerateMsvcBins(root))
            yield return exe;

        var vcToolsRoot = Path.Combine(root, "VC", "Tools", "MSVC");
        foreach (var versionDir in SafeEnumerateDirectories(vcToolsRoot).OrderByDescending(Path.GetFileName))
        {
            foreach (var exe in EnumerateMsvcBins(versionDir))
                yield return exe;
        }

        var vcInstallRoot = Path.Combine(root, "Tools", "MSVC");
        foreach (var versionDir in SafeEnumerateDirectories(vcInstallRoot).OrderByDescending(Path.GetFileName))
        {
            foreach (var exe in EnumerateMsvcBins(versionDir))
                yield return exe;
        }

        foreach (var exe in EnumerateLegacyMsvcBins(root))
            yield return exe;
    }

    private static IEnumerable<string> EnumerateMsvcBins(string toolsRoot)
    {
        var bins = new[]
        {
            Path.Combine(toolsRoot, "bin", "Hostx64", "x64", "cl.exe"),
            Path.Combine(toolsRoot, "bin", "Hostx64", "x86", "cl.exe"),
            Path.Combine(toolsRoot, "bin", "Hostx86", "x64", "cl.exe"),
            Path.Combine(toolsRoot, "bin", "Hostx86", "x86", "cl.exe")
        };

        foreach (var bin in bins)
        {
            if (File.Exists(bin))
                yield return bin;
        }
    }

    private static IEnumerable<string> EnumerateLegacyMsvcBins(string root)
    {
        var vcRoot = Path.Combine(root, "VC");
        foreach (var exe in EnumerateLegacyMsvcBinsAt(vcRoot))
            yield return exe;

        foreach (var exe in EnumerateLegacyMsvcBinsAt(root))
            yield return exe;
    }

    private static IEnumerable<string> EnumerateLegacyMsvcBinsAt(string root)
    {
        var bins = new[]
        {
            Path.Combine(root, "bin", "amd64", "cl.exe"),
            Path.Combine(root, "bin", "x86_amd64", "cl.exe"),
            Path.Combine(root, "bin", "cl.exe")
        };

        foreach (var bin in bins)
        {
            if (File.Exists(bin))
                yield return bin;
        }
    }

    private static IEnumerable<string> EnumerateLlvmExecutables(string root)
    {
        var llvmRoots = new[]
        {
            Path.Combine(root, "VC", "Tools", "Llvm"),
            Path.Combine(root, "VC", "Tools", "Llvm", "bin"),
            Path.Combine(root, "VC", "Tools", "Llvm", "x64", "bin"),
            Path.Combine(root, "Tools", "Llvm")
        };

        foreach (var llvmRoot in llvmRoots)
        {
            foreach (var exe in EnumerateDirectExecutables(llvmRoot))
                yield return exe;
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            yield break;

        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(root);
        }
        catch
        {
            yield break;
        }

        using var enumerator = dirs.GetEnumerator();
        while (true)
        {
            string current;
            try
            {
                if (!enumerator.MoveNext())
                    break;
                current = enumerator.Current;
            }
            catch
            {
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(current))
                yield return current;
        }
    }

    private CompilerToolCandidate CreateCandidate(string path)
    {
        var name = Path.GetFileName(path);
        var installation = Path.GetDirectoryName(path) ?? string.Empty;
        return new CompilerToolCandidate
        {
            Path = path,
            Kind = name,
            InstallationPath = installation,
            Edition = "Local",
            Validated = true,
            Notes = "Found via lightweight scan"
        };
    }

    private static int KindPriority(CompilerToolCandidate candidate)
    {
        var name = Path.GetFileName(candidate.Path);
        if (name.Equals("cl.exe", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.Equals("clang++.exe", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }

    private void LogSummary(CompilerToolDiscoveryResult discovery)
    {
        if (discovery.Errors != null && discovery.Errors.Count > 0)
        {
            foreach (var error in discovery.Errors)
            {
                if (!string.IsNullOrWhiteSpace(error))
                    _logger.Warn(error);
            }
        }

        if (discovery.Best == null)
        {
            _logger.Warn("No compiler toolchains found on this machine.");
            return;
        }

        _logger.Ok($"Compiler candidate selected: {discovery.Best.Path}.");
    }
}
