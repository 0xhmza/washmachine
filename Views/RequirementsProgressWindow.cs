using System.Windows;
using System.Windows.Controls;

namespace Washmachine.Views;

public sealed class RequirementsProgressWindow : Window
{
    private readonly TextBlock _statusText;
    private readonly ProgressBar _progressBar;

    public RequirementsProgressWindow()
    {
        Title = "Downloading Requirements";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 420;
        Height = 160;

        var root = new Grid
        {
            Margin = new Thickness(16, 14, 16, 12)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _statusText = new TextBlock
        {
            Text = "Preparing downloads...",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(_statusText, 0);

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 20,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(_progressBar, 1);

        root.Children.Add(_statusText);
        root.Children.Add(_progressBar);
        Content = root;
    }

    public void UpdateStatus(string message, int progressPercent)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateStatus(message, progressPercent));
            return;
        }

        _statusText.Text = message;
        _progressBar.Value = Math.Clamp(progressPercent, (int)_progressBar.Minimum, (int)_progressBar.Maximum);
    }
}
