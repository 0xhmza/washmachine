using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Washmachine.Views;

public sealed class RequirementsProgressWindow
{
    private readonly Window _window;
    private readonly TextBlock _statusText;
    private readonly ProgressBar _progressBar;

    public RequirementsProgressWindow()
    {
        _statusText = new TextBlock
        {
            Text = "Preparing downloads...",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var root = new Grid { Margin = new Thickness(16, 14, 16, 12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_statusText, 0);
        Grid.SetRow(_progressBar, 1);
        root.Children.Add(_statusText);
        root.Children.Add(_progressBar);

        _window = new Window
        {
            Title = "Downloading Requirements",
            Content = root
        };

        _window.AppWindow.Resize(new SizeInt32(420, 160));

        var display = DisplayArea.GetFromWindowId(_window.AppWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        _window.AppWindow.Move(new PointInt32(
            work.X + (work.Width - 420) / 2,
            work.Y + (work.Height - 160) / 2));
    }

    public void Show() => _window.Activate();

    public void Close() => _window.Close();

    public void UpdateStatus(string message, int progressPercent)
    {
        var dq = _window.DispatcherQueue;
        if (dq.HasThreadAccess)
            DoUpdate(message, progressPercent);
        else
            dq.TryEnqueue(() => DoUpdate(message, progressPercent));
    }

    private void DoUpdate(string message, int progressPercent)
    {
        _statusText.Text = message;
        if (progressPercent < 0)
        {
            _progressBar.IsIndeterminate = true;
        }
        else
        {
            _progressBar.IsIndeterminate = false;
            _progressBar.Value = Math.Clamp(progressPercent, (int)_progressBar.Minimum, (int)_progressBar.Maximum);
        }
    }
}
