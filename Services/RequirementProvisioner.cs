using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using Washmachine.Logging;
using Washmachine.Views;

namespace Washmachine.Services;

public interface IRequirementProvisioner
{
    Task EnsureRequirementsAsync(IMainFormView view, CancellationToken cancellationToken = default);
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

    public async Task EnsureRequirementsAsync(IMainFormView view, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);

        var missing = GetMissingRequirements().ToList();
        if (missing.Count == 0)
        {
            _logger.Ok("All external requirements present.");
            return;
        }

        _logger.Warn($"Missing external requirements detected: {string.Join(", ", missing.Select(m => m.Name))}.");

        var progressForm = new RequirementsProgressWindow();
        progressForm.Show();
        progressForm.UpdateStatus("Preparing downloads...", 0);

        int totalStages = missing.Count * 2;
        int completedStages = 0;

        try
        {
            foreach (var requirement in missing)
            {
                completedStages = await InstallRequirementAsync(requirement, progressForm, totalStages, completedStages, cancellationToken);
            }

            progressForm.UpdateStatus("Requirements ready.", 100);
            await Task.Delay(400, cancellationToken);
            _logger.Ok("All external requirements downloaded successfully.");
        }
        finally
        {
            progressForm.Close();
        }
    }

    private IEnumerable<RequirementData> GetMissingRequirements()
    {
        string? bin2ShellDir = Path.GetDirectoryName(_paths.Bin2ShellScript);
        bool needsBin2Shell = string.IsNullOrWhiteSpace(bin2ShellDir)
                              || !Directory.Exists(bin2ShellDir)
                              || !File.Exists(_paths.Bin2ShellScript)
                              || !File.Exists(_paths.Bin2ShellAlgos);

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
    }

    private async Task<int> InstallRequirementAsync(
        RequirementData requirement,
        RequirementsProgressWindow progressForm,
        int totalStages,
        int completedStages,
        CancellationToken cancellationToken)
    {
        string tempZip = Path.Combine(Path.GetTempPath(), $"washmachine-{Guid.NewGuid():N}.zip");
        string tempExtractRoot = Path.Combine(Path.GetTempPath(), $"washmachine-{Guid.NewGuid():N}");

        try
        {
            progressForm.UpdateStatus($"Downloading {requirement.Name}...", CalculatePercent(completedStages, totalStages));
            await DownloadToFileAsync(requirement, tempZip, cancellationToken);
            completedStages++;

            progressForm.UpdateStatus($"Installing {requirement.Name}...", CalculatePercent(completedStages, totalStages));
            await ExtractAndMoveAsync(requirement, tempZip, tempExtractRoot, cancellationToken);
            completedStages++;

            progressForm.UpdateStatus($"{requirement.Name} ready.", CalculatePercent(completedStages, totalStages));
            return completedStages;
        }
        finally
        {
            TryDeleteFile(tempZip);
            TryDeleteDirectory(tempExtractRoot);
        }
    }

    private async Task DownloadToFileAsync(RequirementData requirement, string destinationFile, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? Path.GetTempPath());

        HttpRequestException? lastException = null;

        foreach (var uri in requirement.DownloadUris)
        {
            try
            {
                _logger.Info($"Downloading {requirement.Name} from {uri}...");
                using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    lastException = new HttpRequestException($"Request failed with status code {(int)response.StatusCode} ({response.StatusCode}).");
                    _logger.Warn($"Failed to download {requirement.Name} from {uri}: {(int)response.StatusCode} {response.StatusCode}.");
                    continue;
                }

                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = File.Create(destinationFile);
                await contentStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

                _logger.Ok($"Downloaded {requirement.Name}.");
                return;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.Warn($"Error downloading {requirement.Name} from {uri}: {ex.Message}");
            }
        }

        throw new InvalidOperationException($"Unable to download {requirement.Name} from the configured sources.", lastException);
    }

    private async Task ExtractAndMoveAsync(RequirementData requirement, string zipFile, string extractRoot, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, true);
            }

            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(zipFile, extractRoot, true);

            string sourceDirectory = Directory.EnumerateDirectories(extractRoot, "*", SearchOption.TopDirectoryOnly).FirstOrDefault()
                                   ?? extractRoot;

            string targetDirectory = requirement.TargetDirectory;
            string? targetParent = Path.GetDirectoryName(targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(targetParent))
            {
                Directory.CreateDirectory(targetParent);
            }

            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, true);
            }

            Directory.Move(sourceDirectory, targetDirectory);
        }, cancellationToken).ConfigureAwait(false);

        _logger.Ok($"Installed {requirement.Name} to {requirement.TargetDirectory}.");
    }

    private static int CalculatePercent(int completedStages, int totalStages)
    {
        if (totalStages <= 0)
            return 100;

        double percent = (double)completedStages / totalStages * 100d;
        return (int)Math.Clamp(Math.Round(percent, MidpointRounding.AwayFromZero), 0, 100);
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
