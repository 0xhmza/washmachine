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
            System.Diagnostics.Debug.WriteLine($"[FATAL] {e.Exception}");
            ShowFatalError(e.Exception?.Message ?? e.Message);
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
            System.Diagnostics.Debug.WriteLine($"[FATAL] {ex}");
            ShowFatalError(ex.Message);
        }
    }

    private static void ShowFatalError(string message)
    {
        try
        {
            var errWindow = new Microsoft.UI.Xaml.Window();
            var panel = new Microsoft.UI.Xaml.Controls.StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12
            };
            panel.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "Something went wrong",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            panel.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.OrangeRed)
            });
            panel.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "Please report this issue on GitHub.",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Gray),
                FontSize = 12
            });
            errWindow.Content = panel;
            errWindow.Title = "Washmachine – Error";
            errWindow.Activate();
        }
        catch { /* last resort */ }
    }
}
