using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using Washmachine.Logging;
using Washmachine.Models;
using Washmachine.Views;

namespace Washmachine.Services;

public sealed class MsvcToolchainLocator : IMsvcToolchainLocator
{
    private static readonly string[] CandidateYears = { "2030", "2029", "2028", "2027", "2026", "2025", "2024", "2023", "2022", "2021", "2020", "2019" };
    private static readonly string[] CandidateEditions = { "BuildTools", "Community", "Professional", "Enterprise", "Preview" };

    private readonly IAppLogger _logger;
    private readonly IUserInteractionService _interaction;
    private readonly IAppPaths _paths;
    private readonly string _cacheFile;

    private MsvcToolchain? _cached;

    public MsvcToolchainLocator(IAppLogger logger, IUserInteractionService interaction, IAppPaths paths)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _cacheFile = Path.Combine(_paths.ExecutableDirectory, "msvc_toolchain.json");
    }

    public Task<MsvcToolchain?> EnsureToolchainAsync(IMainFormView view, CancellationToken cancellationToken = default)
    {
        if (_cached != null && ValidateToolchain(_cached))
            return Task.FromResult<MsvcToolchain?>(_cached);

        var stored = LoadFromDisk();
        if (stored != null)
        {
            _cached = stored;
            _logger.Ok($"Using cached MSVC toolchain: {_cached.VersionLabel}");
            return Task.FromResult<MsvcToolchain?>(_cached);
        }

        var detected = AutoDetect();
        if (detected != null)
        {
            _cached = detected;
            SaveToDisk(_cached);
            _logger.Ok($"Auto-detected MSVC toolchain: {_cached.VersionLabel}");
            return Task.FromResult<MsvcToolchain?>(_cached);
        }

        _logger.Warn("Microsoft Visual C++ compiler not detected automatically.");
        _interaction.ShowMessage(view,
            "Microsoft Visual C++ (MSVC) compiler was not located automatically. Please browse to the 'vcvarsall.bat' file inside your Visual Studio installation.",
            "MSVC Compiler",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        string initialDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? selected = _interaction.SelectFile(
            view,
            "Select MSVC vcvarsall.bat",
            "Batch Files (*.bat)|*.bat|All files (*.*)|*.*",
            initialDir);

        if (string.IsNullOrWhiteSpace(selected))
        {
            _logger.Warn("MSVC compiler selection cancelled by user.");
            return Task.FromResult<MsvcToolchain?>(null);
        }

        var manual = BuildFromVcVars(selected!);
        if (manual == null)
        {
            _interaction.ShowMessage(view,
                "The selected file did not resolve to a valid MSVC toolchain. Please verify the selection and try again.",
                "MSVC Compiler",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _logger.Error("Manual MSVC selection failed validation.");
            return Task.FromResult<MsvcToolchain?>(null);
        }

        _cached = manual;
        SaveToDisk(_cached);
        _logger.Ok($"MSVC toolchain configured: {_cached.VersionLabel}");
        return Task.FromResult<MsvcToolchain?>(_cached);
    }

    private MsvcToolchain? AutoDetect()
    {
        foreach (var vcvars in EnumerateCandidateVcVars())
        {
            var toolchain = BuildFromVcVars(vcvars);
            if (toolchain != null)
                return toolchain;
        }

        foreach (var vcvars in EnumerateViaVsWhere())
        {
            var toolchain = BuildFromVcVars(vcvars);
            if (toolchain != null)
                return toolchain;
        }

        return null;
    }

    private IEnumerable<string> EnumerateCandidateVcVars()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio")
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var year in CandidateYears)
            {
                var yearPath = Path.Combine(root, year);
                if (!Directory.Exists(yearPath))
                    continue;

                foreach (var edition in CandidateEditions)
                {
                    var editionPath = Path.Combine(yearPath, edition);
                    if (!Directory.Exists(editionPath))
                        continue;

                    var vcvars = Path.Combine(editionPath, "VC", "Auxiliary", "Build", "vcvarsall.bat");
                    if (File.Exists(vcvars))
                        yield return vcvars;
                }
            }
        }
    }

    private IEnumerable<string> EnumerateViaVsWhere()
    {
        string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
            yield break;

        List<string> installationRoots;
        try
        {
            var args = "-products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath";
            var psi = new ProcessStartInfo(vswhere)
            {
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                yield break;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            installationRoots = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Warn($"vswhere enumeration failed: {ex.Message}");
            yield break;
        }

        foreach (var installPath in installationRoots)
        {
            var vcvars = Path.Combine(installPath, "VC", "Auxiliary", "Build", "vcvarsall.bat");
            if (File.Exists(vcvars))
                yield return vcvars;
        }
    }

    private MsvcToolchain? BuildFromVcVars(string vcvarsPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(vcvarsPath) || !File.Exists(vcvarsPath))
                return null;

            string? buildDir = Path.GetDirectoryName(vcvarsPath);
            string? auxiliaryDir = buildDir != null ? Path.GetDirectoryName(buildDir) : null;
            string? vcDir = auxiliaryDir != null ? Path.GetDirectoryName(auxiliaryDir) : null; // ...\VC
            if (vcDir == null)
                return null;

            string installationRoot = Path.GetDirectoryName(vcDir) ?? vcDir;
            string toolsDir = Path.Combine(vcDir, "Tools", "MSVC");
            if (!Directory.Exists(toolsDir))
                return null;

            string? latestVersionDir = Directory
                .GetDirectories(toolsDir)
                .OrderByDescending(Path.GetFileName)
                .FirstOrDefault();

            if (latestVersionDir == null)
                return null;

            var candidates = new[]
            {
                Path.Combine(latestVersionDir, "bin", "Hostx64", "x64", "cl.exe"),
                Path.Combine(latestVersionDir, "bin", "HostX64", "x64", "cl.exe"),
                Path.Combine(latestVersionDir, "bin", "Hostx64", "x86", "cl.exe"),
                Path.Combine(latestVersionDir, "bin", "Hostx86", "x86", "cl.exe"),
            };

            string? clPath = candidates.FirstOrDefault(File.Exists);
            if (clPath == null)
            {
                clPath = Directory
                    .EnumerateFiles(latestVersionDir, "cl.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (clPath == null)
                    return null;
            }

            string version = Path.GetFileName(latestVersionDir) ?? "unknown";
            string editionName = Path.GetFileName(installationRoot) ?? "Visual Studio";
            string label = $"{editionName} (MSVC {version})";

            return new MsvcToolchain(vcvarsPath, clPath, installationRoot, label);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to build MSVC toolchain from '{vcvarsPath}': {ex.Message}");
            return null;
        }
    }

    private bool ValidateToolchain(MsvcToolchain toolchain)
    {
        if (toolchain == null)
            return false;

        bool valid = File.Exists(toolchain.VcVarsPath) && File.Exists(toolchain.ClPath);
        if (!valid)
        {
            _logger.Warn("Cached MSVC toolchain no longer valid. Re-detection required.");
            DeleteCacheFile();
        }

        return valid;
    }

    private MsvcToolchain? LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_cacheFile))
                return null;

            var json = File.ReadAllText(_cacheFile);
            var dto = JsonSerializer.Deserialize<ToolchainCacheDto>(json);
            if (dto == null)
                return null;

            var toolchain = new MsvcToolchain(dto.VcVarsPath, dto.ClPath, dto.InstallationRoot, dto.VersionLabel ?? "MSVC");
            return ValidateToolchain(toolchain) ? toolchain : null;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to read MSVC cache: {ex.Message}");
            return null;
        }
    }

    private void SaveToDisk(MsvcToolchain toolchain)
    {
        try
        {
            var dto = new ToolchainCacheDto
            {
                VcVarsPath = toolchain.VcVarsPath,
                ClPath = toolchain.ClPath,
                InstallationRoot = toolchain.InstallationRoot,
                VersionLabel = toolchain.VersionLabel,
                StoredAtUtc = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_cacheFile, json);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to persist MSVC toolchain cache: {ex.Message}");
        }
    }

    private void DeleteCacheFile()
    {
        try
        {
            if (File.Exists(_cacheFile))
            {
                File.Delete(_cacheFile);
            }
        }
        catch
        {
            // ignore
        }
    }

    private sealed class ToolchainCacheDto
    {
        [JsonPropertyName("vcvars")]
        public string VcVarsPath { get; set; } = string.Empty;

        [JsonPropertyName("cl")]
        public string ClPath { get; set; } = string.Empty;

        [JsonPropertyName("root")]
        public string InstallationRoot { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string? VersionLabel { get; set; }

        [JsonPropertyName("storedAtUtc")]
        public DateTime StoredAtUtc { get; set; }
    }
}
