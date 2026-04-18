using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using Washmachine.Logging;

namespace Washmachine.Services;

/// <summary>
/// Shared downloader for the bundled MinGW-w64 toolchain. Used by both the
/// CLI startup flow and the GUI compile page when no C/C++ compiler is
/// detected and the user opts in to downloading one.
/// </summary>
public sealed class MingwDownloader
{
    public const string DownloadUrl =
        "https://github.com/niXman/mingw-builds-binaries/releases/download/14.2.0-rt_v12-rev0/x86_64-14.2.0-release-win32-seh-ucrt-rt_v12-rev0.7z";

    private readonly IAppLogger _logger;
    private readonly string _toolsDir;

    public MingwDownloader(IAppLogger logger, IAppPaths paths)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _toolsDir = Path.Combine((paths ?? throw new ArgumentNullException(nameof(paths))).ExecutableDirectory, "Tools");
    }

    public string ToolsDirectory => _toolsDir;
    public string ArchivePath => Path.Combine(_toolsDir, "mingw64.7z");

    /// <summary>
    /// Download and extract MinGW-w64 into the Tools directory. Progress
    /// callback reports (message, percent). Returns true when the archive
    /// was successfully extracted to disk.
    /// </summary>
    public async Task<bool> DownloadAndExtractAsync(
        Action<string, int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_toolsDir);

        try
        {
            progress?.Invoke("Downloading MinGW-w64…", -1);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Washmachine", "1.0"));

            using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            long bytes = 0;
            long lastReported = -1;

            await using var src = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var dst = File.Create(ArchivePath);
            var buf = new byte[81920];
            int read;
            while ((read = await src.ReadAsync(buf, cancellationToken)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read), cancellationToken);
                bytes += read;

                if (bytes - lastReported > 262144) // 256 KB throttle
                {
                    lastReported = bytes;
                    int pct = total.HasValue && total.Value > 0
                        ? (int)(bytes * 100L / total.Value)
                        : -1;
                    progress?.Invoke($"Downloading MinGW-w64… {FormatBytes(bytes)}{(total.HasValue ? " / " + FormatBytes(total.Value) : "")}", pct);
                }
            }

            _logger.Ok($"Downloaded MinGW-w64 archive ({FormatBytes(bytes)}).");

            progress?.Invoke("Extracting MinGW-w64…", -1);
            var sevenZip = Find7Zip();
            if (string.IsNullOrEmpty(sevenZip))
            {
                _logger.Warn("7-Zip not found. Archive downloaded but extraction is manual.");
                progress?.Invoke("7-Zip not found — extract manually from Tools/.", 100);
                return false;
            }

            if (!await Extract7zAsync(sevenZip, ArchivePath, _toolsDir, cancellationToken))
            {
                _logger.Warn("7-Zip extraction failed.");
                return false;
            }

            TryDelete(ArchivePath);
            _logger.Ok("MinGW-w64 installed.");
            progress?.Invoke("MinGW-w64 installed.", 100);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"MinGW download failed: {ex.Message}");
            return false;
        }
    }

    public static string? Find7Zip()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "7z.exe"),
        };

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return null;
    }

    private static async Task<bool> Extract7zAsync(string sevenZip, string archive, string outDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = sevenZip,
            Arguments = $"x \"{archive}\" -o\"{outDir}\" -y",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi);
        if (proc == null) return false;

        _ = proc.StandardOutput.ReadToEndAsync();
        _ = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync(ct);
        return proc.ExitCode == 0;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private static string FormatBytes(long b) => b switch
    {
        >= 1024 * 1024 => $"{b / (1024.0 * 1024.0):F1} MB",
        >= 1024 => $"{b / 1024.0:F1} KB",
        _ => $"{b} B"
    };
}
