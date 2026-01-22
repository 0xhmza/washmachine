using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Washmachine.Views;

public sealed class RequirementsProgressDialog : IDisposable
{
    private readonly ContentDialog _dialog;
    private readonly TextBlock _statusText;
    private readonly ProgressBar _progressBar;
    private readonly DispatcherQueue _dispatcher;
    private bool _isShown;

    public RequirementsProgressDialog(XamlRoot xamlRoot)
    {
        _statusText = new TextBlock
        {
            Text = "Preparing downloads...",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 18
        };

        _dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Downloading Requirements",
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    _statusText,
                    _progressBar
                }
            },
            DefaultButton = ContentDialogButton.None
        };

        _dispatcher = _dialog.DispatcherQueue;
    }

    public void Show()
    {
        if (_isShown)
            return;

        _isShown = true;
        _ = _dialog.ShowAsync();
    }

    public void UpdateStatus(string message, int progressPercent)
    {
        if (_dispatcher.HasThreadAccess)
        {
            SetStatus(message, progressPercent);
            return;
        }

        _dispatcher.TryEnqueue(() => SetStatus(message, progressPercent));
    }

    public void Close()
    {
        if (!_isShown)
            return;

        _isShown = false;
        _dialog.Hide();
    }

    public void Dispose()
    {
        Close();
    }

    private void SetStatus(string message, int progressPercent)
    {
        _statusText.Text = message;
        _progressBar.Value = Math.Clamp(progressPercent, 0, 100);
    }
}
