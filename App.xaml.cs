using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Washmachine.Views;

namespace Washmachine;

public enum AppExperience
{
    Web,
    Cli,
    WinUi,
}

public partial class App : Application
{
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
    internal static Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment?>? WebView2EnvPrewarm { get; private set; }

    internal static SolidColorBrush ThemeBrush(string key)
    {
        if (DraculaAliases.TryGetValue(key, out var alias))
            key = alias;
        try
        {
            if (Current?.Resources is ResourceDictionary resources && resources[key] is SolidColorBrush brush)
                return brush;
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
            var launcher = new ModeSelectionWindow(StartExperience);
            ActiveWindow = launcher;
            launcher.Activate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FATAL] {ex}");
            ShowFatalError(ex.Message);
        }
    }

    private static void StartExperience(AppExperience experience, ModeSelectionWindow launcher)
    {
        if (experience == AppExperience.Cli)
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Unable to locate the Washmachine executable.");
            var startInfo = new System.Diagnostics.ProcessStartInfo(executable)
            {
                UseShellExecute = true,
            };
            startInfo.ArgumentList.Add("--cli-mode");
            System.Diagnostics.Process.Start(startInfo);
            launcher.Close();
            Current.Exit();
            return;
        }

        if (experience == AppExperience.Web)
            WebView2EnvPrewarm = PrewarmWebViewAsync();

        var startup = new StartupWindow();
        startup.Show();
        _ = RunStartupAsync(startup, launcher, experience);
    }

    private static async Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment?> PrewarmWebViewAsync()
    {
        try
        {
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Washmachine", "WebView2");
            Directory.CreateDirectory(userData);
            return await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(
                null,
                userData,
                new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebView2 prewarm] {ex}");
            return null;
        }
    }

    private static async Task RunStartupAsync(
        StartupWindow startup,
        ModeSelectionWindow launcher,
        AppExperience experience)
    {
        try
        {
            StartupOutcome = await startup.RunAsync();
            ActiveWindow = experience == AppExperience.Web
                ? new WebShellWindow()
                : new MainWindow();
            ActiveWindow.Activate();
            startup.Close();
            launcher.Close();
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
            var errWindow = new Window { Title = "Washmachine – Error" };
            var panel = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 12 };
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
            errWindow.Content = new Microsoft.UI.Xaml.Controls.Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20),
                Child = panel,
            };
            errWindow.Activate();
        }
        catch { }
    }
}
