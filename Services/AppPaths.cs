using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Washmachine.Services;

/// <summary>
/// Centralized resolver for the project paths that were previously scattered across <c>Constants</c>.
/// Defers expensive checks until needed and offers validation messages that can be surfaced to the user.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    private readonly Lazy<string> _tempShellcodeDir;

    public AppPaths()
    {
        ExecutableDirectory = AppDomain.CurrentDomain.BaseDirectory;

        AssetsDirectory = Path.Combine(ExecutableDirectory, "Assets");
        MainCppDirectory = Path.Combine(ExecutableDirectory, "VX-API-main", "VX-API");
        SnippetCatalogFile = Path.Combine(AssetsDirectory, "vx_api_snippets.yaml");
        TemplateCppFile = Path.Combine(AssetsDirectory, "template.cpp");
        MainCppFile = Path.Combine(MainCppDirectory, "main.cpp");

        Bin2ShellScript = Path.Combine(ExecutableDirectory, "Tools", "Bin2Shell", "main.py");
        Bin2ShellAlgos = Path.Combine(ExecutableDirectory, "Tools", "Bin2Shell", "algos.yaml");

        _tempShellcodeDir = new Lazy<string>(CreateTempShellcodeDir, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string ExecutableDirectory { get; }
    public string AssetsDirectory { get; }
    public string SnippetCatalogFile { get; }
    public string MainCppDirectory { get; }
    public string MainCppFile { get; }
    public string TemplateCppFile { get; }
    public string Bin2ShellScript { get; }
    public string Bin2ShellAlgos { get; }

    public string EnsureTempShellcodeDirectory() => _tempShellcodeDir.Value;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Directory.Exists(MainCppDirectory))
            errors.Add($"C++ project directory not found: '{MainCppDirectory}'.");

        var headerPath = Path.Combine(MainCppDirectory, "Win32Helper.h");
        if (!File.Exists(headerPath))
            errors.Add($"Header file missing: '{headerPath}'.");

        if (!Directory.Exists(AssetsDirectory))
            errors.Add($"Assets directory not found: '{AssetsDirectory}'.");

        if (!File.Exists(TemplateCppFile))
            errors.Add($"template.cpp missing: '{TemplateCppFile}'.");

        if (!File.Exists(SnippetCatalogFile))
            errors.Add($"Snippet catalog missing: '{SnippetCatalogFile}'.");

        if (!File.Exists(Bin2ShellScript))
            errors.Add($"Bin2Shell script missing: '{Bin2ShellScript}'.");

        if (!File.Exists(Bin2ShellAlgos))
            errors.Add($"Bin2Shell algos.yaml missing: '{Bin2ShellAlgos}'.");

        return errors;
    }

    private string CreateTempShellcodeDir()
    {
        var tempDir = Path.Combine(ExecutableDirectory, "temp", "shellcodes");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }
}
