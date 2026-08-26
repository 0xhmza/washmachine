using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Washmachine.Views;

namespace Washmachine;

public partial class App : Application
{
    // Maps legacy Dracula-named keys to their native WinUI system resource equivalents.
    // This keeps view code working if any callers still reference Dracula names.
    private static readonly IReadOnlyDictionary<string, string> DraculaAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DraculaBackgroundBrush",    "ApplicationPageBackgroundThemeBrush" },
            { "DraculaCurrentLineBrush",   "DividerStrokeColorDefaultBrush" },
            { "DraculaSelectionBrush",     "SubtleFillColorSecondaryBrush" },
            { "DraculaForegroundBrush",    "TextFillColorPrimaryBrush" },
            { "DraculaCommentBrush",       "TextFillColorSecondaryBrush" },
            { "DraculaRedBrush",           "SystemFillColorCriticalBrush" },
            { "DraculaOrangeBrush",        "SystemFillColorCautionBrush" },
            { "DraculaYellowBrush",        "TextFillColorPrimaryBrush" },
            { "DraculaGreenBrush",         "SystemFillColorSuccessBrush" },
            { "DraculaCyanBrush",          "AccentTextFillColorPrimaryBrush" },
            { "DraculaPurpleBrush",        "AccentFillColorDefaultBrush" },
            { "DraculaPinkBrush",          "AccentFillColorDefaultBrush" },
            { "DraculaRedSoftBrush",       "SystemFillColorCriticalBackgroundBrush" },
            { "DraculaGreenSoftBrush",     "SystemFillColorSuccessBackgroundBrush" },
            { "DraculaOrangeSoftBrush",    "SystemFillColorCautionBackgroundBrush" },
        };

    internal static Window? ActiveWindow { get; private set; }
    internal static StartupOutcome? StartupOutcome { get; private set; }

    /// <summary>
    /// Pre-warming task for the WebView2 environment. Started in
    /// <see cref="OnLaunched"/> so the Chromium browser process spawns in
    /// parallel with the (visible) startup provisioning. By the time the user
    /// finishes downloading requirements, the environment is hot and the
    /// web shell paints almost immediately. Result is awaited by
    /// <see cref="Views.WebShellWindow"/>.
    /// </summary>
    internal static Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment?>? WebView2EnvPrewarm { get; private set; }

    internal static SolidColorBrush ThemeBrush(string key)
    {
        if (DraculaAliases.TryGetValue(key, out var alias))
            key = alias;
        try
        {
            if (Current?.Resources is ResourceDictionary resources)
            {
                var val = resources[key];
                if (val is SolidColorBrush brush)
                    return brush;
            }
        }
        catch { }
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public App()
    {
        UnhandledException += (_, e) =>
        {
            e.Handled = true;
            System.Diagnostics.Debug.WriteLine($"[FATAL] {e.Exception}");
            ShowFatalError(e.Exception?.Message ?? e.Message);
        };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Kick off WebView2 environment creation in parallel with provisioning.
            // CoreWebView2 cold-start takes ~1-2 s on first run; doing it now means
            // the web shell can navigate immediately once StartupWindow closes.
            bool useWebShell = Environment.GetEnvironmentVariable("USE_WEB_SHELL") != "0";
            if (useWebShell)
                WebView2EnvPrewarm = PrewarmWebViewAsync();

            var startup = new StartupWindow();
            startup.Show();
            _ = RunStartupAsync(startup);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FATAL] {ex}");
            ShowFatalError(ex.Message);
        }
    }

    private static async Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment?> PrewarmWebViewAsync()
    {
        try
        {
            var userData = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Washmachine", "WebView2");
            System.IO.Directory.CreateDirectory(userData);

            var envOptions = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--disable-features=msSmartScreenProtection",
            };

            return await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                .CreateWithOptionsAsync(null, userData, envOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebView2 prewarm] {ex}");
            return null;
        }
    }

    private static async Task RunStartupAsync(StartupWindow startup)
    {
        try
        {
            StartupOutcome = await startup.RunAsync();

            // Open the main window BEFORE closing the startup window — otherwise
            // the last-window-closed heuristic begins app shutdown and the new
            // window gets activated into a dying process.
            //
            // Primary shell is now the WebView2-hosted web app (VS Code-style).
            // The legacy XAML MainWindow is kept reachable via Settings ▸ "Use
            // classic UI" for now — set USE_WEB_SHELL=0 to fall back.
            bool useWebShell = Environment.GetEnvironmentVariable("USE_WEB_SHELL") != "0";
            ActiveWindow = useWebShell
                ? (Window)new WebShellWindow()
                : new MainWindow();
            ActiveWindow.Activate();
            startup.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FATAL] startup: {ex}");
            try { startup.Close(); } catch { }
            ShowFatalError($"Startup failed: {ex.Message}");
        }
    }

    private static void ShowFatalError(string message)
    {
        try
        {
            var errWindow = new Microsoft.UI.Xaml.Window();
            var panel = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 12 };
            var content = new Microsoft.UI.Xaml.Controls.Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20),
                Child = panel
            };
            panel.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "Something went wrong",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            panel.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            });
            panel.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "Please report this issue on GitHub.",
                FontSize = 12
            });
            errWindow.Content = content;
            errWindow.Title = "Washmachine – Error";
            errWindow.Activate();
        }
        catch { /* last resort */ }
    }
}
