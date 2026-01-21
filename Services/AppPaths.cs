using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Washmachine.Services;

public interface IAppPaths
{
    string ExecutableDirectory { get; }
    string AssetsDirectory { get; }
    string SnippetCatalogFile { get; }
    string Bin2ShellScript { get; }
    string Bin2ShellAlgos { get; }
    string EnsureTempShellcodeDirectory();
    string EnsureTempSourceDirectory();
    IReadOnlyList<string> Validate();
}

/// <summary>
/// Centralized resolver for application paths. Keeps path knowledge in one place and
/// defers expensive checks until required by callers.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    private const string EmbeddedCatalogResourceName = "Washmachine.Assets.vx_api_snippets.yaml";
    private readonly Lazy<string> _tempShellcodeDir;
    private readonly Lazy<string> _tempSourceDir;

    public AppPaths()
    {
        ExecutableDirectory = AppDomain.CurrentDomain.BaseDirectory;

        AssetsDirectory = Path.Combine(ExecutableDirectory, "Assets");
        SnippetCatalogFile = Path.Combine(AssetsDirectory, "vx_api_snippets.yaml");

        Bin2ShellScript = Path.Combine(ExecutableDirectory, "Tools", "Bin2Shell", "main.py");
        Bin2ShellAlgos = Path.Combine(ExecutableDirectory, "Tools", "Bin2Shell", "data", "yaml", "algos.yaml");

        _tempShellcodeDir = new Lazy<string>(CreateTempShellcodeDir, LazyThreadSafetyMode.ExecutionAndPublication);
        _tempSourceDir = new Lazy<string>(CreateTempSourceDirectory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string ExecutableDirectory { get; }
    public string AssetsDirectory { get; }
    public string SnippetCatalogFile { get; }
    public string Bin2ShellScript { get; }
    public string Bin2ShellAlgos { get; }

    public string EnsureTempShellcodeDirectory() => _tempShellcodeDir.Value;
    public string EnsureTempSourceDirectory() => _tempSourceDir.Value;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!File.Exists(SnippetCatalogFile) && !HasEmbeddedSnippetCatalog())
            errors.Add($"Snippet catalog missing: '{SnippetCatalogFile}'.");

        return errors;
    }

    private static bool HasEmbeddedSnippetCatalog()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceNames().Any(name =>
            name.Equals(EmbeddedCatalogResourceName, StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("vx_api_snippets.yaml", StringComparison.OrdinalIgnoreCase));
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
