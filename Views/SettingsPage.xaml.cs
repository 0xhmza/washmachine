using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Washmachine.Services;

namespace Washmachine.Views;

public sealed partial class SettingsPage : Page
{
    private readonly AppPaths _paths = new();
    private AppSettings _settings = new();
    private bool _settingsLoaded;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = AppSettingsService.Load();
        _settingsLoaded = false;

        toggleSessionLogging.IsOn = _settings.SessionLoggingEnabled;
        toggleSaveBinary.IsOn = _settings.SaveBinaryArtifact;
        toggleSaveShellcode.IsOn = _settings.SaveShellcodeCopy;
        toggleVerboseLogging.IsOn = _settings.VerboseFileLogging;
        toggleKeepBuildArtifacts.IsOn = _settings.KeepBuildArtifacts;

        _settingsLoaded = true;
    }

    private void toggleKeepBuildArtifacts_Toggled(object sender, RoutedEventArgs e)
    {
        _settings.KeepBuildArtifacts = toggleKeepBuildArtifacts.IsOn;
        SaveIfReady();
    }

    private void SaveIfReady()
    {
        if (_settingsLoaded)
            AppSettingsService.Save(_settings);
    }

    private void toggleSessionLogging_Toggled(object sender, RoutedEventArgs e)
    {
        _settings.SessionLoggingEnabled = toggleSessionLogging.IsOn;
        SaveIfReady();
    }

    private void toggleSaveBinary_Toggled(object sender, RoutedEventArgs e)
    {
        _settings.SaveBinaryArtifact = toggleSaveBinary.IsOn;
        SaveIfReady();
    }

    private void toggleSaveShellcode_Toggled(object sender, RoutedEventArgs e)
    {
        _settings.SaveShellcodeCopy = toggleSaveShellcode.IsOn;
        SaveIfReady();
    }

    private void toggleVerboseLogging_Toggled(object sender, RoutedEventArgs e)
    {
        _settings.VerboseFileLogging = toggleVerboseLogging.IsOn;
        SaveIfReady();
    }

    private async void importTemplateUrlButton_Click(object sender, RoutedEventArgs e)
    {
        var (view, coordinator) = ResolveMainPage();
        if (view == null || coordinator == null) return;

        string url = templateCatalogUrl.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            await ShowMessage("Enter a template URL first.");
            return;
        }

        await coordinator.ImportTemplateCatalogFromUrlAsync(view, url);
    }

    private async void importTemplateFileButton_Click(object sender, RoutedEventArgs e)
    {
        var (view, coordinator) = ResolveMainPage();
        if (view == null || coordinator == null) return;

        await coordinator.ImportTemplateCatalogFromFileAsync(view);
    }

    private void openTemplateFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var catalogDir = _paths.AssetsDirectory;
        if (!string.IsNullOrEmpty(catalogDir) && System.IO.Directory.Exists(catalogDir))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = catalogDir,
                UseShellExecute = true
            });
        }
    }

    private static (IMainFormView? view, Controllers.MainFormCoordinator? coordinator) ResolveMainPage()
    {
        // MainPage is cached via NavigationCacheMode.Required — use the static instance.
        var mp = MainPage.Instance;
        if (mp != null)
            return (mp, mp.Coordinator);

        return (null, null);
    }

    private async Task ShowMessage(string text)
    {
        if (XamlRoot == null) return;
        var dialog = new ContentDialog
        {
            Content = text,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
