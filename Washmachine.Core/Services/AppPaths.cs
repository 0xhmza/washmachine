using System.Reflection;

namespace Washmachine.Services;

public interface IAppPaths
{
    string ExecutableDirectory { get; }
    string AssetsDirectory { get; }
    string SnippetCatalogFile { get; }
    string ActivePlaybookPath { get; }
    string ActivePlaybookFullPath { get; }
    string Bin2ShellScript { get; }
    string Bin2ShellAlgos { get; }
    string SgnExecutable { get; }
    bool SetActivePlaybook(string playbookPath);
    IReadOnlyList<string> GetAvailablePlaybookFiles();
    string EnsureTempShellcodeDirectory();
    string EnsureTempSourceDirectory();
    string CreateCompilationSessionDirectory();
    IReadOnlyList<string> Validate();
}

/// <summary>
/// Centralized resolver for application paths. Keeps path knowledge in one place and
/// defers expensive checks until required by callers.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    private readonly Lazy<string> _tempShellcodeDir;
    private readonly Lazy<string> _tempSourceDir;
    private readonly string _activePlaybookStateFile;

    public AppPaths()
    {
        ExecutableDirectory = AppDomain.CurrentDomain.BaseDirectory;

        AssetsDirectory = Path.Combine(ExecutableDirectory, "Assets");
        SnippetCatalogFile = Path.Combine(AssetsDirectory, "default.yaml");
        _activePlaybookStateFile = Path.Combine(AssetsDirectory, ".active-playbook");

        Bin2ShellScript = Path.Combine(ExecutableDirectory, "Tools", "Bin2Shell", "main.py");
        Bin2ShellAlgos = Path.Combine(ExecutableDirectory, "Tools", "Bin2Shell", "data", "yaml", "algos.yaml");
        SgnExecutable = Path.Combine(ExecutableDirectory, "Tools", "SGN", "sgn.exe");

        _tempShellcodeDir = new Lazy<string>(CreateTempShellcodeDir, LazyThreadSafetyMode.ExecutionAndPublication);
        _tempSourceDir = new Lazy<string>(CreateTempSourceDirectory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string ExecutableDirectory { get; }
    public string AssetsDirectory { get; }
    public string SnippetCatalogFile { get; }
    public string ActivePlaybookPath
    {
        get
        {
            var fullPath = ActivePlaybookFullPath;
            return Path.GetRelativePath(ExecutableDirectory, fullPath);
        }
    }

    public string ActivePlaybookFullPath
    {
        get
        {
            var available = GetAvailablePlaybookFiles();
            if (available.Count == 0)
                return SnippetCatalogFile;

            try
            {
                if (File.Exists(_activePlaybookStateFile))
                {
                    var stored = File.ReadAllText(_activePlaybookStateFile).Trim();
                    if (!string.IsNullOrWhiteSpace(stored))
                    {
                        var candidate = Path.IsPathRooted(stored)
                            ? Path.GetFullPath(stored)
                            : Path.GetFullPath(Path.Combine(AssetsDirectory, stored));

                        if (File.Exists(candidate) &&
                            candidate.StartsWith(AssetsDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch
            {
                // Fall back to default selection.
            }

            if (File.Exists(SnippetCatalogFile))
                return SnippetCatalogFile;

            return available
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .First();
        }
    }
    public string Bin2ShellScript { get; }
    public string Bin2ShellAlgos { get; }
    public string SgnExecutable { get; }

    public bool SetActivePlaybook(string playbookPath)
    {
        if (string.IsNullOrWhiteSpace(playbookPath))
            return false;

        string normalized;
        try
        {
            normalized = Path.GetFullPath(playbookPath);
        }
        catch
        {
            return false;
        }

        if (!File.Exists(normalized))
            return false;

        if (!normalized.StartsWith(AssetsDirectory, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            Directory.CreateDirectory(AssetsDirectory);
            File.WriteAllText(_activePlaybookStateFile, Path.GetFileName(normalized));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<string> GetAvailablePlaybookFiles()
    {
        if (!Directory.Exists(AssetsDirectory))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(AssetsDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var ext = Path.GetExtension(path);
                return ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".yml", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string EnsureTempShellcodeDirectory() => _tempShellcodeDir.Value;
    public string EnsureTempSourceDirectory() => _tempSourceDir.Value;

    public string CreateCompilationSessionDirectory()
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string sessionDir = Path.Combine(ExecutableDirectory, "logging", $"session_{timestamp}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionDir);
        return sessionDir;
    }

    /// <summary>
    /// Creates a timestamped session directory for backdoor operations.
    /// Pattern: <c>logging/backdoor_YYYYMMDD_HHMMSS_&lt;guid&gt;/</c>
    /// </summary>
    public string CreateBackdoorSessionDirectory()
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string sessionDir = Path.Combine(ExecutableDirectory, "logging", $"backdoor_{timestamp}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionDir);
        return sessionDir;
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!File.Exists(ActivePlaybookFullPath))
            errors.Add($"Snippet catalog missing: '{ActivePlaybookPath}'.");

        return errors;
    }

    private string CreateTempShellcodeDir()
    {
        var tempDir = Path.Combine(ExecutableDirectory, "temp", "shellcodes");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private string CreateTempSourceDirectory()
    {
        var tempDir = Path.Combine(ExecutableDirectory, "temp", "cpp");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }
}
