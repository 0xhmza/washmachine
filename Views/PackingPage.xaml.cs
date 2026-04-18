using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Net.Http;
using System.IO.Compression;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Washmachine.Views;

public sealed partial class PackingPage : Page
{
    public static PackingPage? Instance { get; private set; }

    private string? _upxPath;
    private static readonly string UpxDownloadUrl = "https://github.com/upx/upx/releases/download/v4.2.4/upx-4.2.4-win64.zip";

    public PackingPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        InitializeComponent();
        Instance = this;
        _ = DetectUpxAsync();
    }

    /// <summary>
    /// Gets whether packing is enabled.
    /// </summary>
    public bool IsPackingEnabled => EnablePackingToggle.IsOn;

    /// <summary>
    /// Gets the UPX path if available.
    /// </summary>
    public string? UpxPath => _upxPath;

    /// <summary>
    /// Gets the selected compression level display name.
    /// </summary>
    public string SelectedCompressionLevel => CompressionLevelCombo.SelectedIndex switch
    {
        0 => "Fast",
        1 => "Normal",
        2 => "Best",
        _ => "Normal"
    };

    /// <summary>
    /// Gets the UPX compression arguments based on settings.
    /// </summary>
    public string GetUpxArguments()
    {
        var args = new List<string>();

        // Compression level
        switch (CompressionLevelCombo.SelectedIndex)
        {
            case 0: args.Add("--best"); break;
            case 2: args.Add("--ultra-brute"); break;
        }

        // Options
        if (StripRelocCheck.IsChecked == true)
            args.Add("--strip-relocs");

        if (OverlayCheck.IsChecked == true)
            args.Add("--overlay=copy");

        if (BackupCheck.IsChecked == true)
            args.Add("-k");

        return string.Join(" ", args);
    }

    private async Task DetectUpxAsync()
    {
        // Check common locations
        var possiblePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "upx", "upx.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "upx", "upx.exe"),
            Path.Combine(AppContext.BaseDirectory, "Tools", "upx.exe"),
            // Check PATH
            await FindInPathAsync("upx.exe")
        };

        foreach (var path in possiblePaths.Where(p => !string.IsNullOrEmpty(p)))
        {
            if (File.Exists(path))
            {
                await SetUpxPath(path!);
                return;
            }
        }

        UpdateUpxStatus(false, "UPX not detected");
    }

    private static async Task<string?> FindInPathAsync(string fileName)
    {
        return await Task.Run(() =>
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;

            foreach (var dir in pathEnv.Split(';'))
            {
                var fullPath = Path.Combine(dir, fileName);
                if (File.Exists(fullPath))
                    return fullPath;
            }
            return null;
        });
    }

    private async Task SetUpxPath(string path)
    {
        _upxPath = path;
        UpxPathText.Text = path;

        // Get version
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = await proc.StandardOutput.ReadLineAsync();
                await proc.WaitForExitAsync();
                var version = output?.Split(' ').Skip(1).FirstOrDefault() ?? "unknown";
                UpdateUpxStatus(true, $"UPX {version} detected");
            }
        }
        catch
        {
            UpdateUpxStatus(true, "UPX detected");
        }
    }

    private void UpdateUpxStatus(bool found, string message)
    {
        UpxStatusText.Text = message;
        UpxStatusIcon.Glyph = found ? "\uE73E" : "\uE9CE";
        UpxStatusIcon.Foreground = new SolidColorBrush(
            found ? Microsoft.UI.Colors.Green : Microsoft.UI.Colors.Gray);
    }

    private void EnablePackingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var enabled = EnablePackingToggle.IsOn;
        PackingConfigPanel.Opacity = enabled ? 1.0 : 0.4;
        PackingConfigPanel.IsHitTestVisible = enabled;
    }

    private async void BrowseUpx_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add(".exe");

        var hwnd = WindowNative.GetWindowHandle(App.ActiveWindow!);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            await SetUpxPath(file.Path);
        }
    }

    private async void DownloadUpx_Click(object sender, RoutedEventArgs e)
    {
        DownloadUpxButton.IsEnabled = false;
        DownloadUpxButton.Content = "Downloading...";

        try
        {
            var toolsDir = Path.Combine(AppContext.BaseDirectory, "Tools");
            Directory.CreateDirectory(toolsDir);

            var zipPath = Path.Combine(toolsDir, "upx.zip");

            // Download
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            var response = await httpClient.GetAsync(UpxDownloadUrl);
            response.EnsureSuccessStatusCode();

            await using var fs = File.Create(zipPath);
            await response.Content.CopyToAsync(fs);
            fs.Close();

            // Extract
            var extractDir = Path.Combine(toolsDir, "upx-temp");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);

            ZipFile.ExtractToDirectory(zipPath, extractDir);

            // Find upx.exe in extracted folder
            var upxExe = Directory.GetFiles(extractDir, "upx.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (upxExe != null)
            {
                var destPath = Path.Combine(toolsDir, "upx.exe");
                File.Copy(upxExe, destPath, true);
                await SetUpxPath(destPath);
            }

            // Cleanup
            File.Delete(zipPath);
            Directory.Delete(extractDir, true);

            DownloadUpxButton.Content = "Download UPX";
        }
        catch (Exception ex)
        {
            DownloadUpxButton.Content = "Download Failed";
            UpdateUpxStatus(false, $"Download failed: {ex.Message}");
        }
        finally
        {
            DownloadUpxButton.IsEnabled = true;
        }
    }

    private void GoToCompilePage_Click(object sender, RoutedEventArgs e)
    {
        if (App.ActiveWindow is MainWindow mainWindow)
        {
            var navView = mainWindow.Content as NavigationView;
            if (navView != null)
            {
                var item = navView.MenuItems.OfType<NavigationViewItem>()
                    .FirstOrDefault(i => i.Tag?.ToString() == "FinalizePage");
                if (item != null)
                {
                    navView.SelectedItem = item;
                }
            }
        }
    }
}
