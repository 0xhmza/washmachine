using System.Diagnostics;
using System.Linq;
using System.Text;
using Washmachine.Logging;

namespace Washmachine.Services;

/// <summary>
/// Options for converting a .NET assembly to position-independent shellcode using donut.
/// </summary>
public sealed class DonutOptions
{
    /// <summary>Path to the input .NET assembly (.exe or .dll).</summary>
    public string InputPath { get; init; } = "";

    /// <summary>Path where the output .bin shellcode will be written.</summary>
    public string OutputPath { get; init; } = "";

    /// <summary>
    /// Target architecture: 1=x86, 2=x64, 3=x86+x64.
    /// Defaults to 3 (x86+x64) so the shellcode runs on either.
    /// </summary>
    public int Arch { get; init; } = 3;

    /// <summary>
    /// Optional fully-qualified class name to invoke (e.g. "MyNamespace.Program").
    /// Only required when the assembly has multiple entry points or is a DLL.
    /// </summary>
    public string? Class { get; init; }

    /// <summary>
    /// Optional method name to invoke on the class.
    /// Defaults to "Main" when not specified.
    /// </summary>
    public string? Method { get; init; }

    /// <summary>
    /// Optional command-line parameters to pass to the assembly entry point (comma-separated).
    /// </summary>
    public string? Params { get; init; }
}

/// <summary>Result of a donut conversion.</summary>
public sealed class DonutResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? Error { get; set; }
    public string? StdOut { get; set; }
}

/// <summary>
/// Wraps the donut.exe CLI to convert .NET assemblies into PIC shellcode .bin files.
/// </summary>
public sealed class DonutService
{
    private readonly string _donutExePath;
    private readonly IAppLogger _logger;

    public DonutService(string donutExePath, IAppLogger logger)
    {
        _donutExePath = donutExePath;
        _logger = logger;
    }

    public bool IsAvailable => File.Exists(_donutExePath);

    /// <summary>
    /// Converts a .NET assembly to PIC shellcode using donut.exe.
    /// </summary>
    public async Task<DonutResult> ConvertAsync(DonutOptions options, CancellationToken cancellationToken = default)
    {
        var result = new DonutResult();

        if (!IsAvailable)
        {
            result.Error = $"donut.exe not found at: {_donutExePath}\nProvision optional tools from the Settings page to download it.";
            return result;
        }

        if (!File.Exists(options.InputPath))
        {
            result.Error = $"Input file not found: {options.InputPath}";
            return result;
        }

        var argList = BuildArgList(options);
        _logger.Info($"Running donut: donut.exe {string.Join(" ", argList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}");

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _donutExePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_donutExePath) ?? Path.GetTempPath(),
            };
            foreach (var arg in argList)
                psi.ArgumentList.Add(arg);

            using var proc = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true,
            };

            proc.OutputDataReceived += (_, e) => { if (e.Data != null) { stdOut.AppendLine(e.Data); _logger.Info(e.Data); } };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) { stdErr.AppendLine(e.Data); _logger.Warn(e.Data); } };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            result.StdOut = stdOut.ToString();

            if (proc.ExitCode == 0 && File.Exists(options.OutputPath))
            {
                result.Success = true;
                result.OutputPath = options.OutputPath;
            }
            else
            {
                var errText = stdErr.Length > 0 ? stdErr.ToString().Trim() : stdOut.ToString().Trim();
                result.Error = $"donut exited with code {proc.ExitCode}.\n{errText}";
            }
        }
        catch (OperationCanceledException)
        {
            result.Error = "Donut conversion was cancelled.";
        }
        catch (Exception ex)
        {
            result.Error = $"Failed to run donut: {ex.Message}";
        }

        return result;
    }

    private static IReadOnlyList<string> BuildArgList(DonutOptions options)
    {
        var args = new List<string> { "-a", options.Arch.ToString(), "-o", options.OutputPath };

        if (!string.IsNullOrWhiteSpace(options.Class))  { args.Add("-c"); args.Add(options.Class); }
        if (!string.IsNullOrWhiteSpace(options.Method)) { args.Add("-m"); args.Add(options.Method); }
        if (!string.IsNullOrWhiteSpace(options.Params))  { args.Add("-p"); args.Add(options.Params); }

        args.Add(options.InputPath);

        return args;
    }
}
