using Microsoft.UI.Xaml;

namespace Washmachine;

public partial class App : Application
{
    internal static Window? ActiveWindow { get; private set; }

    public App()
    {
        UnhandledException += (_, e) =>
        {
            e.Handled = true;
            ShowFatalError(e.Exception?.ToString() ?? e.Message);
        };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            ActiveWindow = new MainWindow();
            ActiveWindow.Activate();
        }
        catch (Exception ex)
        {
            ShowFatalError(ex.ToString());
        }
    }

    private static void ShowFatalError(string message)
    {
        try
        {
            var errWindow = new Microsoft.UI.Xaml.Window();
            var text = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = $"Startup error:\n\n{message}",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Margin = new Microsoft.UI.Xaml.Thickness(16)
            };
            errWindow.Content = text;
            errWindow.Title = "Washmachine – Startup Error";
            errWindow.Activate();
        }
        catch { /* last resort */ }
    }
}
