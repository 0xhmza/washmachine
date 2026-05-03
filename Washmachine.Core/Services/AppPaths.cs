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
    string DonutExecutable { get; }
    bool SetActivePlaybook(string playbookPath);
    IReadOnlyList<string> GetAvailablePlaybookFiles();
    string CreateCompilationSessionDirectory(string? inputName = null, string? outputName = null);
    string CreateBackdoorSessionDirectory();

    /// <summary>
    /// Ensures a subdirectory exists within a session directory and returns its path.
    /// </summary>
    string EnsureSessionSubdirectory(string sessionDir, string subdirectory);

    IReadOnlyList<string> Validate();
}

/// <summary>
/// Centralized resolver for application paths. Keeps path knowledge in one place and
/// defers expensive checks until required by callers.
/// </summary>
public sealed class AppPaths : IAppPaths
{
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
        DonutExecutable = Path.Combine(ExecutableDirectory, "Tools", "Donut", "donut.exe");
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
    public string DonutExecutable { get; }

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

    public string CreateCompilationSessionDirectory(string? inputName = null, string? outputName = null)
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string slug = BuildSessionSlug(inputName, outputName);
        string folderName = string.IsNullOrEmpty(slug)
            ? $"session_{timestamp}_{Guid.NewGuid().ToString("N")[..8]}"
            : $"session_{timestamp}_{slug}";
        string sessionDir = Path.Combine(ExecutableDirectory, "logging", folderName);
        Directory.CreateDirectory(sessionDir);

        // Pre-create standard subdirectories
        Directory.CreateDirectory(Path.Combine(sessionDir, "input"));
        Directory.CreateDirectory(Path.Combine(sessionDir, "source"));
        Directory.CreateDirectory(Path.Combine(sessionDir, "build"));

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

        // Pre-create standard subdirectories
        Directory.CreateDirectory(Path.Combine(sessionDir, "input"));
        Directory.CreateDirectory(Path.Combine(sessionDir, "output"));

        return sessionDir;
    }

    public string EnsureSessionSubdirectory(string sessionDir, string subdirectory)
    {
        var path = Path.Combine(sessionDir, subdirectory);
        Directory.CreateDirectory(path);
        return path;
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!File.Exists(ActivePlaybookFullPath))
            errors.Add($"Snippet catalog missing: '{ActivePlaybookPath}'.");

        return errors;
    }

    /// <summary>
    /// Builds a short, filesystem-safe slug from input/output names for session folder naming.
    /// Falls back to a short GUID segment when no meaningful names are available.
    /// </summary>
    private static string BuildSessionSlug(string? inputName, string? outputName)
    {
        static string Sanitize(string? name, int maxLen = 40)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            // Strip to filename without extension
            var stem = Path.GetFileNameWithoutExtension(name.Trim());
            if (string.IsNullOrWhiteSpace(stem)) return string.Empty;
            // Replace unsafe chars
            var sb = new System.Text.StringBuilder(stem.Length);
            foreach (var ch in stem)
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
                    sb.Append(ch);
                else if (ch == ' ' || ch == '.')
                    sb.Append('_');
            }
            var result = sb.ToString().Trim('_');
            return result.Length > maxLen ? result[..maxLen] : result;
        }

        var parts = new List<string>();
        var inp = Sanitize(inputName);
        var outp = Sanitize(outputName);
        if (!string.IsNullOrEmpty(inp)) parts.Add(inp);
        if (!string.IsNullOrEmpty(outp)) parts.Add(outp);
        return parts.Count > 0
            ? string.Join("_", parts)
            : Guid.NewGuid().ToString("N")[..8];
    }
}
