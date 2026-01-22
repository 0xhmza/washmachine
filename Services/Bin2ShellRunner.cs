using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Washmachine.Services;

public interface IBin2ShellRunner
{
    Task<string> RunAsync(
        IEnumerable<string> arguments,
        string? pythonExecutable = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs the bundled Bin2Shell CLI and returns its stdout.
/// </summary>
public sealed class Bin2ShellRunner : IBin2ShellRunner
{
    private readonly IAppPaths _paths;

    public Bin2ShellRunner(IAppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<string> RunAsync(IEnumerable<string> arguments, string? pythonExecutable = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        pythonExecutable ??= Environment.OSVersion.Platform == PlatformID.Win32NT ? "python" : "python3";

        var workingDirectory = Path.GetDirectoryName(_paths.Bin2ShellScript);
        if (string.IsNullOrEmpty(workingDirectory))
            workingDirectory = _paths.ExecutableDirectory;

        var allArgs = new List<string> { _paths.Bin2ShellScript };
        allArgs.AddRange(arguments);

        var psi = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in allArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start Bin2Shell process.");

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Bin2Shell exited with code {process.ExitCode}.{Environment.NewLine}{stderr}");
        }

        return stdout;
    }
}
