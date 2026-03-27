using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Washmachine.Services;

namespace Washmachine.Views;

public sealed partial class SettingsPage : Page
{
    private readonly AppPaths _paths = new();

    public SettingsPage()
    {
        InitializeComponent();
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
        var catalogDir = System.IO.Path.GetDirectoryName(_paths.ActivePlaybookPath);
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
        if (App.ActiveWindow?.Content is not NavigationView navView ||
            navView.Content is not Frame frame)
            return (null, null);

        // MainPage is cached via NavigationCacheMode.Required — walk the back-stack
        // to find the cached instance and retrieve its coordinator.
        foreach (var entry in frame.BackStack)
        {
            if (entry.SourcePageType != typeof(MainPage))
                continue;

            // WinUI keeps the page alive. To reach it, temporarily navigate and
            // back — or scan the visual tree. However, the simplest reliable way
            // is to check if the frame's cached page matches.
            // Frame does not expose its page cache directly, so we have to do a
            // navigate-and-back dance:
            frame.Navigate(typeof(MainPage));
            if (frame.Content is MainPage mp)
            {
                frame.GoBack();
                return (mp, mp.Coordinator);
            }
        }

        // Already on MainPage
        if (frame.Content is MainPage current)
            return (current, current.Coordinator);

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
