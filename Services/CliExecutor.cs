using System.Diagnostics;

namespace Washmachine.Services;

/// <summary>
/// Locates and invokes washmachine-cli.exe, streaming its output line-by-line.
/// </summary>
public sealed class CliExecutor
{
    public string CliPath { get; }
    public bool IsAvailable => File.Exists(CliPath);

    public CliExecutor()
    {
        CliPath = FindCliExecutable();
    }

    /// <summary>
    /// Runs the CLI with the given arguments, calling <paramref name="onOutput"/> for each output line.
    /// Returns a <see cref="CliResult"/> with exit code and all output lines.
    /// </summary>
    public async Task<CliResult> RunAsync(
        IEnumerable<string> args,
        Action<string>? onOutput = null,
        CancellationToken ct = default)
    {
        if (!IsAvailable)
            throw new FileNotFoundException($"CLI not found: {CliPath}. Build the Washmachine.Cli project first.");

        var psi = new ProcessStartInfo
        {
            FileName               = CliPath,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var lines = new List<string>();

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start washmachine-cli.exe");

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) { lines.Add(e.Data); onOutput?.Invoke(e.Data); }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) { lines.Add(e.Data); onOutput?.Invoke(e.Data); }
        };

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct);

        return new CliResult(proc.ExitCode, lines.AsReadOnly());
    }

    private static string FindCliExecutable()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        static string? FindRepoRoot(string dir)
        {
            var current = dir;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "washmachine.sln"))) return current;
                var parent = Path.GetDirectoryName(current);
                if (parent == current) break;
                current = parent;
            }
            return null;
        }

        var repoRoot = FindRepoRoot(appDir);

        string?[] candidates =
        [
            // Deployed: CLI bundled next to GUI
            Path.Combine(appDir, "washmachine-cli.exe"),
            // Development: Output\cli\Debug\net8.0\
            repoRoot is not null
                ? Path.Combine(repoRoot, "Output", "cli", "Debug", "net8.0", "washmachine-cli.exe")
                : null,
            // Development: Output\cli\Release\publish\
            repoRoot is not null
                ? Path.Combine(repoRoot, "Output", "cli", "Release", "publish", "washmachine-cli.exe")
                : null,
        ];

        return candidates
            .Where(p => p is not null)
            .Cast<string>()
            .FirstOrDefault(File.Exists)
               ?? Path.Combine(appDir, "washmachine-cli.exe");
    }
}

public sealed record CliResult(int ExitCode, IReadOnlyList<string> OutputLines)
{
    public bool   Success => ExitCode == 0;
    public string Output  => string.Join(Environment.NewLine, OutputLines);
}
