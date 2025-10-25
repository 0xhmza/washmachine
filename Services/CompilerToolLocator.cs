using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Washmachine.Logging;
using Washmachine.Models;

namespace Washmachine.Services;

public sealed class CompilerToolLocator : ICompilerToolLocator
{
    private static readonly string[] KnownYears = { "2026", "2025", "2022", "2019", "2017" };
    private static readonly string[] KnownEditions = { "BuildTools", "Enterprise", "Professional", "Community", "Insiders" };
    private static readonly Dictionary<string, int> VersionToYear = new(StringComparer.OrdinalIgnoreCase)
    {
        ["18"] = 2026,
        ["17"] = 2022,
        ["16"] = 2019,
        ["15"] = 2017
    };

    private static readonly string[] RegistryKeys2015 =
    {
        @"SOFTWARE\\Microsoft\\VisualStudio\\14.0\\Setup\\VC",
        @"SOFTWARE\\WOW6432Node\\Microsoft\\VisualStudio\\14.0\\Setup\\VC"
    };

    private static readonly Regex YearRegex = new(@"Microsoft Visual Studio\\(?<year>\d{4})\\", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VersionRegex = new(@"Microsoft Visual Studio\\(?<version>\d{2})\\", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EditionRegex = new(@"Microsoft Visual Studio\\\d{1,4}\\(?<edition>[^\\]+)\\", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly IAppLogger _logger;
    private readonly object _syncRoot = new();
    private readonly HashSet<string> _manualCandidates = new(StringComparer.OrdinalIgnoreCase);

    private CompilerToolDiscoveryResult? _cached;
    private int _sequenceCounter;

    public CompilerToolLocator(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<CompilerToolDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default)
        => DiscoverInternalAsync(forceRefresh: false, cancellationToken);

    public async Task<CompilerToolDiscoveryResult> AddManualCandidateAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must be provided.", nameof(path));

        string normalizedPath = Path.GetFullPath(path);
        if (!File.Exists(normalizedPath))
            throw new FileNotFoundException("Specified compiler script does not exist.", normalizedPath);

        lock (_syncRoot)
        {
            if (_manualCandidates.Add(normalizedPath))
            {
                _cached = null;
            }
        }

        _logger.Info($"Manual compiler script registered: {normalizedPath}");
        return await DiscoverInternalAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CompilerToolDiscoveryResult> DiscoverInternalAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        CompilerToolDiscoveryResult? cached = null;
        lock (_syncRoot)
        {
            if (!forceRefresh)
            {
                cached = _cached;
            }
        }

        if (cached != null)
        {
            _logger.Info("Using cached Visual Studio build tools discovery result.");
            return cached;
        }

        var map = new Dictionary<string, CandidateBuilder>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        _logger.Info("Checking environment variables for Visual Studio build tools scripts...");
        CollectFromEnvironment(map);

        cancellationToken.ThrowIfCancellationRequested();

        await CollectFromVsWhereAsync(map, errors, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        _logger.Info("Scanning well-known Visual Studio install locations...");
        CollectFromWellKnownPaths(map);

        cancellationToken.ThrowIfCancellationRequested();

        _logger.Info("Checking legacy Visual Studio 2015 locations...");
        CollectLegacyCandidates(map);

        cancellationToken.ThrowIfCancellationRequested();

        IncludeManualCandidates(map);

        var builders = map.Values.OrderBy(b => b.Sequence).ToList();

        foreach (var builder in builders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ValidateCandidateAsync(builder, errors, cancellationToken).ConfigureAwait(false);
        }

        var candidates = builders.Select(b => b.ToCandidate()).ToList();
        var best = SelectBestCandidate(candidates);

        var result = CreateResult(best, candidates, errors);

        lock (_syncRoot)
        {
            _cached = result;
        }

        WriteJson(result);
        LogSummary(result);

        return result;
    }

    private void CollectFromEnvironment(IDictionary<string, CandidateBuilder> map)
    {
        TryAddEnvCandidate(map, "VSINSTALLDIR", Path.Combine("VC", "Auxiliary", "Build", "vcvars64.bat"), "vcvars64");
        TryAddEnvCandidate(map, "VSINSTALLDIR", Path.Combine("VC", "Auxiliary", "Build", "vcvarsall.bat"), "vcvarsall");
        TryAddEnvCandidate(map, "VCINSTALLDIR", Path.Combine("Auxiliary", "Build", "vcvars64.bat"), "vcvars64");
        TryAddEnvCandidate(map, "VCINSTALLDIR", Path.Combine("Auxiliary", "Build", "vcvarsall.bat"), "vcvarsall");
        TryAddEnvCandidate(map, "VCToolsInstallDir", Path.Combine("..", "..", "Auxiliary", "Build", "vcvars64.bat"), "vcvars64");
        TryAddEnvCandidate(map, "VCToolsInstallDir", Path.Combine("..", "..", "Auxiliary", "Build", "vcvarsall.bat"), "vcvarsall");
    }

    private void TryAddEnvCandidate(IDictionary<string, CandidateBuilder> map, string envVariable, string relativePath, string kind)
    {
        string? value = Environment.GetEnvironmentVariable(envVariable);
        if (string.IsNullOrWhiteSpace(value))
            return;

        string combined;
        try
        {
            combined = Path.GetFullPath(Path.Combine(value, relativePath));
        }
        catch
        {
            return;
        }

        AddCandidate(map, combined, kind, $"env:{envVariable}");
    }

    private async Task CollectFromVsWhereAsync(IDictionary<string, CandidateBuilder> map, ICollection<string> errors, CancellationToken cancellationToken)
    {
        string? programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (string.IsNullOrWhiteSpace(programFilesX86))
        {
            AppendError(errors, "ProgramFiles(x86) environment variable not defined.");
            _logger.Warn("ProgramFiles(x86) environment variable not defined; skipping vswhere discovery.");
            return;
        }

        string vswherePath = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswherePath))
        {
            AppendError(errors, "vswhere.exe not found.");
            _logger.Warn($"vswhere.exe not found at {vswherePath}; skipping vswhere discovery.");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = vswherePath,
            Arguments = "-all -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        _logger.Info($"Running vswhere discovery via {vswherePath}...");

        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                const string message = "Unable to start vswhere.exe.";
                AppendError(errors, message);
                _logger.Warn(message);
                return;
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                string message = $"vswhere exited with code {process.ExitCode}. {stderr}".Trim();
                AppendError(errors, message);
                _logger.Warn(message);
                return;
            }

            if (string.IsNullOrWhiteSpace(stdout))
            {
                _logger.Warn("vswhere returned no instances.");
                return;
            }

            using var document = JsonDocument.Parse(stdout);
            foreach (var instance in document.RootElement.EnumerateArray())
            {
                if (!instance.TryGetProperty("installationPath", out var installationPathProp))
                    continue;

                string? installationPath = installationPathProp.GetString();
                if (string.IsNullOrWhiteSpace(installationPath))
                    continue;

                string normalized = Path.GetFullPath(installationPath);
                string? instanceVersion = instance.TryGetProperty("installationVersion", out var versionProp)
                    ? versionProp.GetString()
                    : null;

                AddVsInstanceCandidates(map, normalized, instanceVersion, $"vswhere:{normalized}");
            }
        }
        catch (JsonException ex)
        {
            string message = $"Failed to parse vswhere output: {ex.Message}";
            AppendError(errors, message);
            _logger.Warn(message);
        }
        catch (Exception ex)
        {
            string message = $"vswhere discovery failed: {ex.Message}";
            AppendError(errors, message);
            _logger.Warn(message);
        }
    }

    private void CollectFromWellKnownPaths(IDictionary<string, CandidateBuilder> map)
    {
        var bases = new[]
        {
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)")
        };

        foreach (var baseDir in bases)
        {
            if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
                continue;

            foreach (var year in KnownYears)
            {
                foreach (var edition in KnownEditions)
                {
                    string installationPath = Path.Combine(baseDir, "Microsoft Visual Studio", year, edition);
                    if (Directory.Exists(installationPath))
                    {
                        AddVsInstanceCandidates(map, installationPath, null, $"well-known:{installationPath}");
                    }
                }
            }

            foreach (var kvp in VersionToYear)
            {
                foreach (var edition in KnownEditions)
                {
                    string installationPath = Path.Combine(baseDir, "Microsoft Visual Studio", kvp.Key, edition);
                    if (Directory.Exists(installationPath))
                    {
                        AddVsInstanceCandidates(map, installationPath, null, $"well-known-version:{installationPath}");
                    }
                }
            }
        }
    }

    private void CollectLegacyCandidates(IDictionary<string, CandidateBuilder> map)
    {
        string? programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            string installationPath = Path.Combine(programFilesX86, "Microsoft Visual Studio 14.0");
            string script = Path.Combine(installationPath, "VC", "vcvarsall.bat");
            AddCandidate(map, script, "vcvarsall", "vs2015-default", installationPath, "14.0");
        }

        foreach (var registryPath in RegistryKeys2015)
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(registryPath);
            if (key == null)
                continue;

            if (key.GetValue("ProductDir") is string dir && !string.IsNullOrWhiteSpace(dir))
            {
                string script = Path.Combine(dir, "vcvarsall.bat");
                AddCandidate(map, script, "vcvarsall", $"registry:{registryPath}", dir, "14.0");
            }
        }
    }

    private void IncludeManualCandidates(IDictionary<string, CandidateBuilder> map)
    {
        string[] manual;
        lock (_syncRoot)
        {
            manual = _manualCandidates.ToArray();
        }

        foreach (var path in manual)
        {
            if (!File.Exists(path))
            {
                _logger.Warn($"Manual compiler script missing: {path}. Removing from list.");
                lock (_syncRoot)
                {
                    _manualCandidates.Remove(path);
                    _cached = null;
                }

                continue;
            }

            if (map.ContainsKey(path))
                continue;

            string? kind = DetermineKindFromFile(path);
            if (kind == null)
            {
                _logger.Warn($"Manual compiler script ignored (unsupported type): {path}");
                continue;
            }

            AddCandidate(map, path, kind, "manual");
        }
    }

    private void AddVsInstanceCandidates(IDictionary<string, CandidateBuilder> map, string installationPath, string? instanceVersion, string source)
    {
        string normalized = Path.GetFullPath(installationPath);
        string vcAux = Path.Combine(normalized, "VC", "Auxiliary", "Build");
        AddCandidate(map, Path.Combine(vcAux, "vcvars64.bat"), "vcvars64", source, normalized, instanceVersion);
        AddCandidate(map, Path.Combine(vcAux, "vcvarsall.bat"), "vcvarsall", source, normalized, instanceVersion);
        AddCandidate(map, Path.Combine(normalized, "Common7", "Tools", "VsDevCmd.bat"), "VsDevCmd", source, normalized, instanceVersion);
    }

    private void AddCandidate(IDictionary<string, CandidateBuilder> map, string path, string kind, string source, string? installationPath = null, string? instanceVersion = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        if (!File.Exists(normalizedPath))
            return;

        if (map.ContainsKey(normalizedPath))
            return;

        var builder = new CandidateBuilder(normalizedPath, kind, source, Interlocked.Increment(ref _sequenceCounter))
        {
            InstallationPath = installationPath ?? DetermineInstallationPath(normalizedPath),
            InstanceVersion = instanceVersion,
            Year = InferYear(normalizedPath),
            Edition = InferEdition(normalizedPath)
        };

        map.Add(normalizedPath, builder);
        _logger.Info($"Candidate located ({source}): {normalizedPath} [{kind}].");
    }

    private static string DetermineInstallationPath(string scriptPath)
    {
        try
        {
            var fileInfo = new FileInfo(scriptPath);
            var directory = fileInfo.Directory;
            while (directory != null)
            {
                if (directory.Name.Equals("VC", StringComparison.OrdinalIgnoreCase) ||
                    directory.Name.Equals("Common7", StringComparison.OrdinalIgnoreCase))
                {
                    return directory.Parent?.FullName ?? directory.FullName;
                }

                directory = directory.Parent;
            }
        }
        catch
        {
            // ignored
        }

        return Path.GetDirectoryName(scriptPath) ?? scriptPath;
    }

    private static string? DetermineKindFromFile(string path)
    {
        string fileName = Path.GetFileName(path);
        if (fileName.Equals("vcvars64.bat", StringComparison.OrdinalIgnoreCase))
            return "vcvars64";
        if (fileName.Equals("vcvarsall.bat", StringComparison.OrdinalIgnoreCase))
            return "vcvarsall";
        if (fileName.Equals("VsDevCmd.bat", StringComparison.OrdinalIgnoreCase))
            return "VsDevCmd";
        return null;
    }

    private static int? InferYear(string path)
    {
        var match = YearRegex.Match(path);
        if (match.Success && int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int year))
        {
            return year;
        }

        var versionMatch = VersionRegex.Match(path);
        if (versionMatch.Success && VersionToYear.TryGetValue(versionMatch.Groups["version"].Value, out var mappedYear))
        {
            return mappedYear;
        }

        if (path.Contains("14.0", StringComparison.OrdinalIgnoreCase))
            return 2015;

        return null;
    }

    private static string InferEdition(string path)
    {
        var match = EditionRegex.Match(path);
        if (match.Success)
        {
            string edition = match.Groups["edition"].Value;
            foreach (var known in KnownEditions)
            {
                if (edition.Equals(known, StringComparison.OrdinalIgnoreCase))
                    return known;
            }
        }

        foreach (var known in KnownEditions)
        {
            if (path.IndexOf($"\\{known}\\", StringComparison.OrdinalIgnoreCase) >= 0)
                return known;
        }

        return "Unknown";
    }

    private async Task ValidateCandidateAsync(CandidateBuilder builder, ICollection<string> errors, CancellationToken cancellationToken)
    {
        string? command = builder.Kind switch
        {
            "vcvars64" => $"call \"{builder.Path}\" && cl.exe /Bv",
            "vcvarsall" => $"call \"{builder.Path}\" x64 && cl.exe /Bv",
            "VsDevCmd" => $"call \"{builder.Path}\" && cl.exe /Bv",
            _ => null
        };

        if (command == null)
        {
            builder.Notes = "Unsupported script type.";
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        _logger.Info($"Validating {builder.Kind} candidate: {builder.Path}");

        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                const string message = "Failed to start validation process.";
                builder.Notes = message;
                AppendError(errors, message + $" ({builder.Path})");
                _logger.Warn(message + $" ({builder.Path})");
                return;
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            string combined = ($"{stdout}{stderr}").Trim();

            bool hasCompilerSignature = ContainsCompilerSignature(combined);
            bool succeeded = process.ExitCode == 0 && !string.IsNullOrWhiteSpace(combined);

            if (!succeeded && hasCompilerSignature)
            {
                succeeded = true;
            }

            if (succeeded)
            {
                builder.Validated = true;
                builder.Notes = process.ExitCode == 0
                    ? "Validated successfully."
                    : $"Validated with exit code {process.ExitCode}.";
                _logger.Ok($"Validation succeeded for {builder.Path}.");
            }
            else
            {
                builder.Validated = false;
                builder.Notes = BuildValidationFailureNote(process.ExitCode, combined);
                AppendError(errors, builder.Notes + $" ({builder.Path})");
                _logger.Warn($"Validation failed for {builder.Path}: {builder.Notes}");
            }
        }
        catch (Exception ex)
        {
            builder.Validated = false;
            builder.Notes = ex.Message;
            AppendError(errors, $"Validation failed for {builder.Path}: {ex.Message}");
            _logger.Warn($"Validation error for {builder.Path}: {ex.Message}");
        }
    }

    private static string BuildValidationFailureNote(int exitCode, string output)
    {
        var sb = new StringBuilder();
        sb.Append("Validation failed");
        if (exitCode != 0)
        {
            sb.Append($" (exit code {exitCode})");
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
            string firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? output;
            if (firstLine.Length > 240)
            {
                firstLine = firstLine.Substring(0, 240) + "...";
            }

            sb.Append($". Output: {firstLine}");
        }

        return sb.ToString();
    }

    private CompilerToolDiscoveryResult CreateResult(CompilerToolCandidate? best, List<CompilerToolCandidate> candidates, List<string> errors)
    {
        var candidateArray = candidates
            .OrderByDescending(c => c.Year ?? int.MinValue)
            .ThenBy(EditionPriority)
            .ThenBy(c => c.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var errorArray = errors.Count == 0 ? Array.Empty<string>() : errors.Distinct(StringComparer.Ordinal).ToArray();

        var result = new CompilerToolDiscoveryResult
        {
            Best = best,
            Candidates = candidateArray,
            Errors = errorArray
        };

        string json = JsonSerializer.Serialize(result, JsonOptions);

        return new CompilerToolDiscoveryResult
        {
            Best = result.Best,
            Candidates = result.Candidates,
            Errors = result.Errors,
            Json = json
        };
    }

    private void WriteJson(CompilerToolDiscoveryResult discovery)
    {
        if (string.IsNullOrWhiteSpace(discovery.Json))
            return;

        try
        {
            Console.Out.WriteLine(discovery.Json);
            Console.Out.Flush();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Unable to write compiler discovery JSON to stdout: {ex.Message}");
        }
    }

    private void LogSummary(CompilerToolDiscoveryResult discovery)
    {
        if (discovery.Best == null)
        {
            _logger.Warn("No Visual Studio build tools candidate selected.");
            return;
        }

        string validationState = discovery.Best.Validated ? "validated" : "not validated";
        _logger.Ok($"Best compiler candidate: {discovery.Best.Kind} -> {discovery.Best.Path} ({validationState}).");
    }

    private static bool ContainsCompilerSignature(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.IndexOf("Microsoft (R) C/C++", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("Compiler Version", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AppendError(ICollection<string> errors, string message)
    {
        if (errors == null || string.IsNullOrWhiteSpace(message))
            return;

        if (!errors.Contains(message))
        {
            errors.Add(message);
        }
    }

    private CompilerToolCandidate? SelectBestCandidate(IEnumerable<CompilerToolCandidate> candidates)
    {
        var list = candidates.ToList();
        if (list.Count == 0)
            return null;

        var validated = list.Where(c => c.Validated).ToList();

        var best = validated
            .Where(c => c.Kind.Equals("vcvars64", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Year ?? int.MinValue)
            .ThenBy(EditionPriority)
            .ThenByDescending(ParseVersion)
            .FirstOrDefault();

        if (best != null)
            return best;

        best = validated
            .Where(c => c.Kind.Equals("vcvarsall", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Year ?? int.MinValue)
            .ThenBy(EditionPriority)
            .ThenByDescending(ParseVersion)
            .FirstOrDefault();

        if (best != null)
            return best;

        best = validated
            .Where(c => c.Kind.Equals("VsDevCmd", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Year ?? int.MinValue)
            .ThenBy(EditionPriority)
            .ThenByDescending(ParseVersion)
            .FirstOrDefault();

        if (best != null)
            return best;

        return list
            .OrderByDescending(c => c.Year ?? int.MinValue)
            .ThenBy(EditionPriority)
            .ThenByDescending(ParseVersion)
            .ThenBy(c => c.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int EditionPriority(CompilerToolCandidate candidate)
        => EditionPriority(candidate.Edition);

    private static int EditionPriority(string edition)
    {
        return edition switch
        {
            "BuildTools" => 0,
            "Enterprise" => 1,
            "Professional" => 2,
            "Community" => 3,
            "Insiders" => 4,
            _ => 5
        };
    }

    private static Version ParseVersion(CompilerToolCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.InstanceVersion) && Version.TryParse(candidate.InstanceVersion, out var version))
        {
            return version;
        }

        return new Version(0, 0);
    }

    private sealed class CandidateBuilder
    {
        public CandidateBuilder(string path, string kind, string source, int sequence)
        {
            Path = path;
            Kind = kind;
            Source = source;
            Sequence = sequence;
        }

        public string Path { get; }
        public string Kind { get; }
        public string Source { get; }
        public int Sequence { get; }
        public int? Year { get; set; }
        public string Edition { get; set; } = "Unknown";
        public string? InstanceVersion { get; set; }
        public string InstallationPath { get; set; } = string.Empty;
        public bool Validated { get; set; }
        public string Notes { get; set; } = string.Empty;

        public CompilerToolCandidate ToCandidate()
            => new()
            {
                Path = Path,
                Kind = Kind,
                Year = Year,
                Edition = Edition,
                InstanceVersion = InstanceVersion,
                InstallationPath = InstallationPath,
                Validated = Validated,
                Notes = Notes ?? string.Empty
            };
    }
}


