using System.Diagnostics;
using System.Text;
using ShieldGuard.Logging;

namespace ShieldGuard.Services;

/// <summary>
/// Inputs for converting a .NET assembly to position-independent licensestub using
/// <see href="https://github.com/TheWover/licenseassembly">licenseassembly</see>.
/// </summary>
public sealed class LicenseAssemblyOptions
{
    /// <summary>Path to the input .NET assembly (.exe or .dll).</summary>
    public string InputPath { get; init; } = "";

    /// <summary>Path where the output .bin licensestub will be written.</summary>
    public string OutputPath { get; init; } = "";

    /// <summary>
    /// Target architecture: 1 = x86, 2 = x64, 3 = x86+x64.
    /// Defaults to x86+x64 — the most-compatible choice — so the resulting licensestub
    /// runs in either a 32-bit or 64-bit host process.
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

/// <summary>Outcome of a single <see cref="LicenseAssemblyService.ConvertAsync"/> call.</summary>
public sealed class LicenseAssemblyResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? Error { get; set; }
    public string? StdOut { get; set; }
}

/// <summary>
/// Wraps the <c>licenseassembly.exe</c> CLI to convert .NET assemblies into PIC licensestub <c>.bin</c> files.
/// Stateless: a single instance can be reused across calls.
///
/// <para>
/// <b>Minimal-feature mode.</b> The service deliberately invokes licenseassembly with the smallest
/// flag set that still produces a working licensepayload. Optional features that embed
/// signature-rich code blobs — AMSI/WLDP bypass, compression, threading, staging URL,
/// wide-string encoding — are <i>not</i> enabled. The only non-essential flag we set is
/// <c>-b 1</c> to <i>disable</i> the AMSI/WLDP bypass, since licenseassembly's default
/// (<c>-b 3</c>) injects a sizable bypass stub that both adds detectable patterns and
/// can crash the loader on hardened/patched hosts.
/// </para>
/// </summary>
public sealed class LicenseAssemblyService
{
    private readonly string _licenseassemblyExePath;
    private readonly IAppLogger _logger;

    public LicenseAssemblyService(string licenseassemblyExePath, IAppLogger logger)
    {
        _licenseassemblyExePath = licenseassemblyExePath;
        _logger = logger;
    }

    /// <summary>True when <c>licenseassembly.exe</c> exists at the configured path.</summary>
    public bool IsAvailable => File.Exists(_licenseassemblyExePath);

    /// <summary>
    /// Converts the assembly described by <paramref name="options"/> to PIC licensestub.
    /// On success, the output file is written and <see cref="LicenseAssemblyResult.OutputPath"/> is populated.
    /// </summary>
    public async Task<LicenseAssemblyResult> ConvertAsync(LicenseAssemblyOptions options, CancellationToken cancellationToken = default)
    {
        var result = new LicenseAssemblyResult();

        if (!IsAvailable)
        {
            result.Error = $"licenseassembly.exe not found at: {_licenseassemblyExePath}\n" +
                           "Provision optional tools from the Settings page to download it.";
            return result;
        }

        if (!File.Exists(options.InputPath))
        {
            result.Error = $"Input file not found: {options.InputPath}";
            return result;
        }

        var argList = BuildArgList(options);
        _logger.Info($"Running licenseassembly: licenseassembly.exe {FormatArgsForLog(argList)}");

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _licenseassemblyExePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                // LicenseAssembly writes some files relative to its CWD; sit next to licenseassembly.exe so
                // intermediate artifacts land in a predictable place.
                WorkingDirectory = Path.GetDirectoryName(_licenseassemblyExePath) ?? Path.GetTempPath(),
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
                result.Error = $"licenseassembly exited with code {proc.ExitCode}.\n{errText}";
            }
        }
        catch (OperationCanceledException)
        {
            result.Error = "LicenseAssembly conversion was cancelled.";
        }
        catch (Exception ex)
        {
            result.Error = $"Failed to run licenseassembly: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Builds the licenseassembly argument vector with the minimum flags needed to produce a
    /// working PIC licensestub. Each token is passed separately via
    /// <see cref="ProcessStartInfo.ArgumentList"/> so paths with spaces don't need
    /// manual quoting on the call site.
    ///
    /// <para>Always-on flags:</para>
    /// <list type="bullet">
    ///   <item><description><c>-a &lt;arch&gt;</c> — target bitness (caller-provided).</description></item>
    ///   <item><description><c>-b 1</c> — disable AMSI/WLDP bypass blob (smaller, fewer signatures, more compatible).</description></item>
    ///   <item><description><c>-i</c> / <c>-o</c> — input assembly and output .bin path.</description></item>
    /// </list>
    ///
    /// <para>
    /// <c>-c</c>, <c>-m</c>, <c>-p</c> are forwarded only when the caller supplied
    /// an entry point override. Every other licenseassembly feature (compression, threading,
    /// entropy mode, staging URL, wide-string encoding, runtime pinning, exit option)
    /// is intentionally left at licenseassembly's default to keep the embedded loader minimal.
    /// </para>
    /// </summary>
    private static List<string> BuildArgList(LicenseAssemblyOptions options)
    {
        var args = new List<string>
        {
            "-a", options.Arch.ToString(),
            "-b", "1",                      // no AMSI/WLDP bypass — minimal signatures + better target compat
            "-o", options.OutputPath,
        };

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
