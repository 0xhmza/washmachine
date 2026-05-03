using System.Diagnostics;
using System.Text;
using Washmachine.Logging;

namespace Washmachine.Services;

/// <summary>
/// Inputs for converting a .NET assembly to position-independent shellcode using
/// <see href="https://github.com/TheWover/donut">donut</see>.
/// </summary>
public sealed class DonutOptions
{
    /// <summary>Path to the input .NET assembly (.exe or .dll).</summary>
    public string InputPath { get; init; } = "";

    /// <summary>Path where the output .bin shellcode will be written.</summary>
    public string OutputPath { get; init; } = "";

    /// <summary>
    /// Target architecture: 1 = x86, 2 = x64, 3 = x86+x64.
    /// Defaults to x86+x64 so the resulting shellcode runs on either bitness.
    /// </summary>
    public int Arch { get; init; } = 3;

    /// <summary>
    /// Optional fully-qualified class name to invoke (e.g. <c>"MyNamespace.Program"</c>).
    /// Required when the assembly has multiple entry points or is a DLL.
    /// </summary>
    public string? Class { get; init; }

    /// <summary>
    /// Optional method to invoke on <see cref="Class"/>. Defaults to <c>Main</c> when omitted.
    /// </summary>
    public string? Method { get; init; }

    /// <summary>
    /// Optional command-line parameters to pass to the assembly entry point (comma-separated).
    /// </summary>
    public string? Params { get; init; }
}

/// <summary>Outcome of a single <see cref="DonutService.ConvertAsync"/> call.</summary>
public sealed class DonutResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? Error { get; set; }
    public string? StdOut { get; set; }
}

/// <summary>
/// Wraps the <c>donut.exe</c> CLI to convert .NET assemblies into PIC shellcode <c>.bin</c> files.
/// Stateless: a single instance can be reused across calls.
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

    /// <summary>True when <c>donut.exe</c> exists at the configured path.</summary>
    public bool IsAvailable => File.Exists(_donutExePath);

    /// <summary>
    /// Converts the assembly described by <paramref name="options"/> to PIC shellcode.
    /// On success, the output file is written and <see cref="DonutResult.OutputPath"/> is populated.
    /// </summary>
    public async Task<DonutResult> ConvertAsync(DonutOptions options, CancellationToken cancellationToken = default)
    {
        var result = new DonutResult();

        if (!IsAvailable)
        {
            result.Error = $"donut.exe not found at: {_donutExePath}\n" +
                           "Provision optional tools from the Settings page to download it.";
            return result;
        }

        if (!File.Exists(options.InputPath))
        {
            result.Error = $"Input file not found: {options.InputPath}";
            return result;
        }

        var argList = BuildArgList(options);
        _logger.Info($"Running donut: donut.exe {FormatArgsForLog(argList)}");

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
                // Donut writes some files relative to its CWD; sit next to donut.exe so
                // intermediate artifacts land in a predictable place.
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

    /// <summary>
    /// Builds the donut argument vector. We pass each token separately (via
    /// <see cref="ProcessStartInfo.ArgumentList"/>) so paths with spaces don't
    /// need manual quoting on the call site.
    /// </summary>
    private static List<string> BuildArgList(DonutOptions options)
    {
        var args = new List<string> { "-a", options.Arch.ToString(), "-o", options.OutputPath };

        if (!string.IsNullOrWhiteSpace(options.Class))  { args.Add("-c"); args.Add(options.Class); }
        if (!string.IsNullOrWhiteSpace(options.Method)) { args.Add("-m"); args.Add(options.Method); }
        if (!string.IsNullOrWhiteSpace(options.Params)) { args.Add("-p"); args.Add(options.Params); }

        args.Add("-i"); args.Add(options.InputPath);
        return args;
    }

    /// <summary>
    /// Best-effort reconstruction of how the args would look on a shell command line —
    /// purely for the log echo, not parsed by anyone.
    /// </summary>
    private static string FormatArgsForLog(IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        foreach (var a in args)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (a.Contains(' ')) sb.Append('"').Append(a).Append('"');
            else                 sb.Append(a);
        }
        return sb.ToString();
    }
}
