using System.Diagnostics;
using System.Text.RegularExpressions;
using Washmachine.Logging;

namespace Washmachine.Services;

/// <summary>
/// One status row produced by <see cref="ToolPreflightService"/>.
/// </summary>
public sealed record ToolStatus(
    string Tool,
    bool Found,
    string? Location,
    string? Version,
    bool MeetsRequirements,
    string Detail);

/// <summary>
/// Aggregate result of a preflight check.
/// </summary>
public sealed record ToolPreflightReport(IReadOnlyList<ToolStatus> Statuses)
{
    public bool AllOk => Statuses.All(s => s.Found && s.MeetsRequirements);
    public IEnumerable<ToolStatus> Missing => Statuses.Where(s => !s.Found);
    public IEnumerable<ToolStatus> Incompatible => Statuses.Where(s => s.Found && !s.MeetsRequirements);
}

/// <summary>
/// Single source of truth for the "are my external tools installed?" check.
/// Validates that LLVM/clang, MSVC, Bin2Shell are reachable and that the LLVM
/// version is high enough to host the bundled obfuscation passes.
///
/// <para>The <see cref="RequiredLlvmMajor"/> minimum matches the new pass-manager
/// APIs used by Assets/llvm-passes/*: <c>registerOptimizerEarlyEPCallback</c>
/// with <c>ThinOrFullLTOPhase</c> (LLVM 16+) and <c>getFirstNonPHIIt()</c>
/// returning a BasicBlock::iterator (LLVM 20+).</para>
/// </summary>
public sealed class ToolPreflightService
{
    public const int RequiredLlvmMajor = 20;
    public const int RecommendedLlvmMajor = 22;

    private static readonly Regex ClangVersionRegex = new(
        @"(?:clang|LLVM)\s+version\s+(?<v>\d+(?:\.\d+){0,2})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IAppPaths _paths;
    private readonly ICompilerToolLocator _compilerLocator;
    private readonly IAppLogger _logger;

    public ToolPreflightService(IAppPaths paths, ICompilerToolLocator compilerLocator, IAppLogger logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _compilerLocator = compilerLocator ?? throw new ArgumentNullException(nameof(compilerLocator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ToolPreflightReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var statuses = new List<ToolStatus>
        {
            CheckClang(),
            CheckClangCl(),
            await CheckMsvcAsync(cancellationToken).ConfigureAwait(false),
            CheckBin2Shell(),
        };

        return new ToolPreflightReport(statuses);
    }

    private ToolStatus CheckClang()
    {
        var path = ResolveTool(_paths.LlvmClangPlusPlus, "clang++.exe");
        if (path is null)
            return new ToolStatus(
                "LLVM clang++",
                Found: false,
                Location: null,
                Version: null,
                MeetsRequirements: false,
                Detail: $"Not found at {_paths.LlvmClangPlusPlus} or on PATH. " +
                        "Install LLVM ≥ " + RequiredLlvmMajor + " and place clang++.exe in Tools\\LLVM\\bin\\, " +
                        "or add the LLVM bin directory to PATH.");

        var version = TryReadClangVersion(path);
        if (version is null)
            return new ToolStatus("LLVM clang++", true, path, null, false,
                "clang++ found but version could not be parsed from --version output.");

        bool ok = version.Major >= RequiredLlvmMajor;
        string detail = ok
            ? (version.Major >= RecommendedLlvmMajor
                ? "OK."
                : $"Compatible (≥ {RequiredLlvmMajor}). Recommended: {RecommendedLlvmMajor}+ for full obfuscation-pass API surface.")
            : $"Version {version} is below the required minimum {RequiredLlvmMajor}. " +
              $"Obfuscation passes will fail to load. Upgrade to LLVM {RecommendedLlvmMajor}+.";

        return new ToolStatus("LLVM clang++", true, path, version.ToString(), ok, detail);
    }

    private ToolStatus CheckClangCl()
    {
        var path = ResolveTool(_paths.LlvmClangCl, "clang-cl.exe");
        if (path is null)
            return new ToolStatus(
                "LLVM clang-cl",
                Found: false,
                Location: null,
                Version: null,
                MeetsRequirements: false,
                Detail: $"Not found at {_paths.LlvmClangCl} or on PATH. " +
                        "Bundled with the same LLVM install as clang++.");

        var version = TryReadClangVersion(path);
        if (version is null)
            return new ToolStatus("LLVM clang-cl", true, path, null, true,
                "clang-cl found; version not parsed but binary is present.");

        bool ok = version.Major >= RequiredLlvmMajor;
        return new ToolStatus("LLVM clang-cl", true, path, version.ToString(), ok,
            ok ? "OK." : $"Version {version} is below required minimum {RequiredLlvmMajor}.");
    }

    private async Task<ToolStatus> CheckMsvcAsync(CancellationToken cancellationToken)
    {
        var discovery = await _compilerLocator.DiscoverAsync(cancellationToken).ConfigureAwait(false);

        var cl = discovery.Candidates?
            .FirstOrDefault(c => Path.GetFileName(c.Path).Equals("cl.exe", StringComparison.OrdinalIgnoreCase));

        if (cl is null)
            return new ToolStatus(
                "MSVC cl.exe",
                Found: false,
                Location: null,
                Version: null,
                MeetsRequirements: false,
                Detail: "Visual Studio Build Tools (cl.exe) not detected. " +
                        "Install Visual Studio Build Tools 2019/2022 with the 'Desktop development with C++' workload " +
                        "from https://aka.ms/vs/17/release/vs_BuildTools.exe.");

        return new ToolStatus("MSVC cl.exe", true, cl.Path, null, true,
            "OK — required by clang-cl for the MSVC sysroot.");
    }

    private ToolStatus CheckBin2Shell()
    {
        bool found = File.Exists(_paths.Bin2ShellScript);
        if (!found)
            return new ToolStatus(
                "Bin2Shell",
                Found: false,
                Location: null,
                Version: null,
                MeetsRequirements: false,
                Detail: $"main.py not found at {_paths.Bin2ShellScript}. " +
                        "Run 'washmachine-cli provision' (or trigger first-run provisioning in the GUI) to download it.");

        return new ToolStatus("Bin2Shell", true, _paths.Bin2ShellScript, null, true,
            "OK — required for shellcode encoding/envelope features.");
    }

    private static string? ResolveTool(string bundledPath, string exeName)
    {
        if (File.Exists(bundledPath))
            return bundledPath;

        var raw = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var segment in raw.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(segment.Trim(), exeName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private Version? TryReadClangVersion(string path)
    {
        try
        {
            var psi = new ProcessStartInfo(path, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            string stdout = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(5000)) { try { proc.Kill(); } catch { } return null; }

            var match = ClangVersionRegex.Match(stdout);
            if (!match.Success) return null;
            return Version.TryParse(match.Groups["v"].Value, out var v) ? v : null;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to read version from {path}: {ex.Message}");
            return null;
        }
    }
}
