using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Washmachine.Views;

/// <summary>
/// Progress window for compiler detection that shows a loading bar and logging output.
/// </summary>
public sealed class CompilerDetectionWindow
{
    private readonly Window _window;
    private readonly TextBlock _statusText;
    private readonly ProgressBar _progressBar;
    private readonly TextBox _logBox;
    private readonly Button _closeButton;

    public CompilerDetectionWindow()
    {
        _statusText = new TextBlock
        {
            Text = "Detecting compilers...",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = true,
            Margin = new Thickness(0, 0, 0, 12)
        };

        _logBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            MinHeight = 200,
            MaxHeight = 300
        };

        // Wrap in ScrollViewer for scrolling
        var logScrollViewer = new ScrollViewer
        {
            Content = _logBox,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 300
        };

        _closeButton = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            IsEnabled = false
        };

        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 12) };
        root.Children.Add(_statusText);
        root.Children.Add(_progressBar);
        root.Children.Add(new TextBlock 
        { 
            Text = "Detection Log:", 
            Margin = new Thickness(0, 0, 0, 4),
            FontSize = 12,
            Opacity = 0.7
        });
        root.Children.Add(logScrollViewer);
        root.Children.Add(_closeButton);

        _window = new Window
        {
            Title = "Compiler Detection",
            Content = root
        };

        // Hook up close button after window is created
        _closeButton.Click += (_, _) => _window.Close();

        _window.AppWindow.Resize(new SizeInt32(520, 420));

        var display = DisplayArea.GetFromWindowId(_window.AppWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        _window.AppWindow.Move(new PointInt32(
            work.X + (work.Width - 520) / 2,
            work.Y + (work.Height - 420) / 2));
    }

    public void Show() => _window.Activate();

    public void Close() => _window.Close();

    public void UpdateStatus(string message, int progressPercent)
    {
        var dq = _window.DispatcherQueue;
        if (dq.HasThreadAccess)
            DoUpdateStatus(message, progressPercent);
        else
            dq.TryEnqueue(() => DoUpdateStatus(message, progressPercent));
    }

    public void AppendLog(string message)
    {
        var dq = _window.DispatcherQueue;
        if (dq.HasThreadAccess)
            DoAppendLog(message);
        else
            dq.TryEnqueue(() => DoAppendLog(message));
    }

    public void SetComplete(bool success, string summary)
    {
        var dq = _window.DispatcherQueue;
        if (dq.HasThreadAccess)
            DoSetComplete(success, summary);
        else
            dq.TryEnqueue(() => DoSetComplete(success, summary));
    }

    private void DoUpdateStatus(string message, int progressPercent)
    {
        _statusText.Text = message;
        if (progressPercent >= 0)
        {
            _progressBar.IsIndeterminate = false;
            _progressBar.Value = Math.Clamp(progressPercent, (int)_progressBar.Minimum, (int)_progressBar.Maximum);
        }
    }

    private void DoAppendLog(string message)
    {
        if (!string.IsNullOrEmpty(_logBox.Text))
            _logBox.Text += Environment.NewLine;
        _logBox.Text += message;
        
        // Scroll to end
        _logBox.SelectionStart = _logBox.Text.Length;
    }

    private void DoSetComplete(bool success, string summary)
    {
        _progressBar.IsIndeterminate = false;
        _progressBar.Value = 100;
        _statusText.Text = summary;
        _closeButton.IsEnabled = true;

        if (success)
            _statusText.Foreground = App.ThemeBrush("DraculaGreenBrush");
        else
            _statusText.Foreground = App.ThemeBrush("DraculaOrangeBrush");
    }
}
