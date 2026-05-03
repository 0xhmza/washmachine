using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Washmachine.Logging;

namespace Washmachine.Services;

public interface IRequirementProvisioner
{
    Task EnsureRequirementsAsync(IProgressReporter? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ensures external tools (like Bin2Shell) are present, downloading when missing.
/// </summary>
public sealed class RequirementProvisioner : IRequirementProvisioner
{
    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;
    private readonly HttpClient _httpClient;

    public RequirementProvisioner(IAppPaths paths, IAppLogger logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Washmachine", "1.0"));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Requirements", "1.0"));
    }

    public Task EnsureRequirementsAsync(IProgressReporter? progress = null, CancellationToken cancellationToken = default) =>
        EnsureRequirementsAsync(progress, includeOptionalTools: true, cancellationToken);

    public async Task EnsureRequirementsAsync(
        IProgressReporter? progress,
        bool includeOptionalTools,
        CancellationToken cancellationToken = default)
    {
        CleanupStaleDirectories();

        var missing = GetMissingRequirements(includeOptionalTools).ToList();
        if (missing.Count == 0)
        {
            EnsureBin2ShellAlgorithmDescriptions();
            _logger.Debug("All external requirements present.");
            return;
        }

        _logger.Warn($"Missing external requirements detected: {string.Join(", ", missing.Select(m => m.Name))}.");

        progress?.UpdateStatus("Preparing downloads...", -1);

        try
        {
            for (int i = 0; i < missing.Count; i++)
            {
                var requirement = missing[i];
                string tempZip = Path.Combine(Path.GetTempPath(), $"washmachine-{Guid.NewGuid():N}.zip");
                string tempExtractRoot = Path.Combine(Path.GetTempPath(), $"washmachine-{Guid.NewGuid():N}");

                try
                {
                    await DownloadToFileAsync(requirement, tempZip, progress, cancellationToken);
                    await ExtractAndMoveAsync(requirement, tempZip, tempExtractRoot, progress, cancellationToken);
                }
                finally
                {
                    TryDeleteFile(tempZip);
                    TryDeleteDirectory(tempExtractRoot);
                }
            }

            EnsureBin2ShellAlgorithmDescriptions();

            progress?.UpdateStatus("Requirements ready.", 100);
            await Task.Delay(400, cancellationToken);
            _logger.Ok("All external requirements downloaded successfully.");
        }
        finally
        {
            progress?.Close();
        }
    }

    private IEnumerable<RequirementData> GetMissingRequirements(bool includeOptionalTools)
    {
        string? bin2ShellDir = Path.GetDirectoryName(_paths.Bin2ShellScript);
        bool needsBin2Shell = string.IsNullOrWhiteSpace(bin2ShellDir)
                              || !Directory.Exists(bin2ShellDir)
                              || !File.Exists(_paths.Bin2ShellScript)
                              || !File.Exists(_paths.Bin2ShellAlgos);
        string? sgnDir = Path.GetDirectoryName(_paths.SgnExecutable);
        bool needsSgn = string.IsNullOrWhiteSpace(sgnDir)
                        || !Directory.Exists(sgnDir)
                        || !File.Exists(_paths.SgnExecutable);

        if (needsBin2Shell)
        {
            yield return new RequirementData(
                "Bin2Shell",
                Path.Combine(_paths.ExecutableDirectory, "Tools", "Bin2Shell"),
                new[]
                {
                    new Uri("https://github.com/0xhmza/bin2shell/archive/refs/heads/main.zip"),
                    new Uri("https://github.com/0xhmza/bin2shell/archive/refs/heads/master.zip")
                });
        }

        if (includeOptionalTools && needsSgn)
        {
            yield return new RequirementData(
                "SGN",
                Path.Combine(_paths.ExecutableDirectory, "Tools", "SGN"),
                new[]
                {
                    new Uri("https://github.com/EgeBalci/sgn/releases/download/v2.0.1/sgn_windows_amd64_2.0.1.zip"),
                    new Uri("https://github.com/EgeBalci/sgn/releases/download/v2.0.1/sgn_windows_386_2.0.1.zip")
                });
        }

        bool needsDonut = !File.Exists(_paths.DonutExecutable);
        if (includeOptionalTools && needsDonut)
        {
            yield return new RequirementData(
                "Donut",
                Path.Combine(_paths.ExecutableDirectory, "Tools", "Donut"),
                new[]
                {
                    new Uri("https://github.com/TheWover/donut/releases/download/v1.1/donut_v1.1.zip")
                });
        }
    }

    private async Task DownloadToFileAsync(RequirementData requirement, string destinationFile, IProgressReporter? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? Path.GetTempPath());

        Exception? lastException = null;

        foreach (var uri in requirement.DownloadUris)
        {
            try
            {
                _logger.Info($"Downloading {requirement.Name} from {uri}...");
                progress?.UpdateStatus($"Connecting to download server...", -1);

                using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    lastException = new HttpRequestException($"Request failed with status code {(int)response.StatusCode} ({response.StatusCode}).");
                    _logger.Warn($"Failed to download {requirement.Name} from {uri}: {(int)response.StatusCode} {response.StatusCode}.");
                    continue;
                }

                long? totalBytes = response.Content.Headers.ContentLength;
                long bytesDownloaded = 0;
                long lastReportedBytes = -1;
                const long reportThreshold = 128 * 1024; // report every 128 KB

                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = File.Create(destinationFile);

                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    bytesDownloaded += bytesRead;

                    if (bytesDownloaded - lastReportedBytes >= reportThreshold)
                    {
                        lastReportedBytes = bytesDownloaded;
                        string statusMsg;
                        int percent;
                        if (totalBytes > 0)
                        {
                            percent = (int)(bytesDownloaded * 100L / totalBytes.Value);
                            statusMsg = $"Downloading {requirement.Name}... {FormatBytes(bytesDownloaded)} / {FormatBytes(totalBytes.Value)}";
                        }
                        else
                        {
                            percent = -1;
                            statusMsg = $"Downloading {requirement.Name}... {FormatBytes(bytesDownloaded)}";
                        }
                        progress?.UpdateStatus(statusMsg, percent);
                    }
                }

                _logger.Ok($"Downloaded {requirement.Name} ({FormatBytes(bytesDownloaded)}).");
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
            {
                lastException = ex;
                _logger.Warn($"Error downloading {requirement.Name} from {uri}: {ex.Message}");
            }
        }

        throw new InvalidOperationException($"Unable to download {requirement.Name} from the configured sources. Last error: {lastException?.Message}", lastException);
    }

    private async Task ExtractAndMoveAsync(RequirementData requirement, string zipFile, string extractRoot, IProgressReporter? progress, CancellationToken cancellationToken)
    {
        progress?.UpdateStatus($"Installing {requirement.Name}...", -1);

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(extractRoot))
                Directory.Delete(extractRoot, true);

            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(zipFile, extractRoot, true);

            // Only unwrap one level when the zip has a single root directory and
            // no files at the top level (e.g. GitHub archive zips like "tool-main/").
            // For flat releases (files + subdirs at root, like donut_v1.1.zip),
            // use extractRoot directly so that the binary lands at the right path.
            var topLevelDirs  = Directory.EnumerateDirectories(extractRoot, "*", SearchOption.TopDirectoryOnly).ToList();
            var topLevelFiles = Directory.EnumerateFiles(extractRoot,      "*", SearchOption.TopDirectoryOnly).ToList();

            string sourceDirectory = topLevelDirs.Count == 1 && topLevelFiles.Count == 0
                ? topLevelDirs[0]
                : extractRoot;

            string targetDirectory = requirement.TargetDirectory;
            string? targetParent = Path.GetDirectoryName(targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(targetParent))
                Directory.CreateDirectory(targetParent);

            MoveDirectoryRobust(sourceDirectory, targetDirectory);
        }, cancellationToken).ConfigureAwait(false);

        _logger.Ok($"Installed {requirement.Name} to {requirement.TargetDirectory}.");
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
            >= 1024        => $"{bytes / 1024.0:F1} KB",
            _              => $"{bytes} B"
        };
    }

    private void EnsureBin2ShellAlgorithmDescriptions()
    {
        try
        {
            if (!File.Exists(_paths.Bin2ShellAlgos))
                return;

            var yaml = File.ReadAllText(_paths.Bin2ShellAlgos);
            var updated = yaml;

            updated = EnsureEnvelopeDescription(updated, 1, "base91", "Base91 text envelope");
            updated = EnsureEnvelopeDescription(updated, 2, "base64", "Base64 text envelope");
            updated = EnsureEnvelopeDescription(updated, 3, "base32", "Base32 text envelope");

            if (!string.Equals(yaml, updated, StringComparison.Ordinal))
            {
                File.WriteAllText(_paths.Bin2ShellAlgos, updated);
                _logger.Info("Updated Bin2Shell algorithm descriptions in algos.yaml.");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to update Bin2Shell algorithm descriptions: {ex.Message}");
        }
    }

    private static string EnsureEnvelopeDescription(string yaml, int index, string name, string description)
    {
        var pattern = $@"(?ms)(\r?\n- index:\s*{index}\s*\r?\n\s*name:\s*{Regex.Escape(name)}\s*\r?\n)(?!\s*desc:)";
        var replacement = $"$1  desc: {description}{Environment.NewLine}";
        return Regex.Replace(yaml, pattern, replacement);
    }

    /// <summary>
    /// Moves source to target using multiple strategies to cope with
    /// Windows file-lock scenarios (antivirus, search indexer, IDE, etc.).
    /// </summary>
    private void MoveDirectoryRobust(string source, string target)
    {
        if (!Directory.Exists(target))
        {
            Directory.Move(source, target);
            return;
        }

        // Strategy 1: rename the old directory aside, then move the new one in.
        string aside = target + $".old-{Guid.NewGuid():N}";
        try
        {
            Directory.Move(target, aside);
            try
            {
                Directory.Move(source, target);
            }
            catch
            {
                try { Directory.Move(aside, target); } catch { }
                throw;
            }
            try { DeleteDirectoryWithRetry(aside, maxRetries: 2); } catch { }
            return;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        // Strategy 2: delete with retry, then move.
        try
        {
            DeleteDirectoryWithRetry(target);
            if (!Directory.Exists(target))
            {
                Directory.Move(source, target);
                return;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        // Strategy 3: clear existing contents and copy new files into the
        // (locked) target directory. Individual file operations succeed even
        // when the directory handle itself is held by another process.
        _logger.Warn("Target directory is locked — falling back to in-place copy.");
        ClearDirectoryContents(target);
        CopyDirectoryContents(source, target);
    }

    /// <summary>
    /// Removes stale <c>.old-*</c> directories left by previous rename-aside
    /// install attempts.
    /// </summary>
    private void CleanupStaleDirectories()
    {
        string toolsDir = Path.Combine(_paths.ExecutableDirectory, "Tools");
        if (!Directory.Exists(toolsDir))
            return;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(toolsDir, "*.old-*", SearchOption.TopDirectoryOnly))
            {
                try { Directory.Delete(dir, true); }
                catch { /* will be retried next time */ }
            }
        }
        catch { /* ignore enumeration errors */ }
    }

    /// <summary>
    /// Removes all files and subdirectories inside a directory without
    /// deleting the directory itself (which may be locked).
    /// </summary>
    private static void ClearDirectoryContents(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var attrs = File.GetAttributes(file);
                if (attrs.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                File.Delete(file);
            }
            catch { /* best effort — file may be locked */ }
        }

        foreach (var subDir in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
        {
            try { Directory.Delete(subDir, true); }
            catch { ClearDirectoryContents(subDir); }
        }
    }

    /// <summary>
    /// Recursively copies all files and directories from source into target.
    /// Throws on failure so that callers never see a partial install as success.
    /// </summary>
    private static void CopyDirectoryContents(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            string destFile = Path.Combine(target, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
        {
            string destDir = Path.Combine(target, Path.GetFileName(dir));
            CopyDirectoryContents(dir, destDir);
        }
    }

    /// <summary>
    /// Deletes a directory with retries to handle transient locks from
    /// Windows Search Indexer, antivirus, or other background scanners.
    /// </summary>
    private static void DeleteDirectoryWithRetry(string path, int maxRetries = 5)
    {
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;

                // Clear read-only attributes that can block deletion
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    var attrs = File.GetAttributes(file);
                    if (attrs.HasFlag(FileAttributes.ReadOnly))
                        File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                }

                Directory.Delete(path, true);
                return;
            }
            catch (Exception) when (attempt < maxRetries)
            {
                // Brief pause to let Windows release the handle
                Thread.Sleep(500 * (attempt + 1));
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore cleanup errors
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // ignore cleanup errors
        }
    }

    private sealed record RequirementData(string Name, string TargetDirectory, IReadOnlyList<Uri> DownloadUris);
}
