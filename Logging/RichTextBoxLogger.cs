using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Washmachine.Logging;

public sealed class RichTextBoxLogger : IAppLogger
{
    private readonly RichTextBox _target;
    private readonly Brush _infoBrush;

    public RichTextBoxLogger(RichTextBox target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _target.IsReadOnly = true;
        if (_target.Foreground is SolidColorBrush solid)
        {
            _infoBrush = new SolidColorBrush(solid.Color);
            _infoBrush.Freeze();
        }
        else
        {
            _infoBrush = Brushes.Gainsboro;
        }
    }

    public void Info(string message) => Write(message, _infoBrush);
    public void Warn(string message) => Write(message, Brushes.Goldenrod);
    public void Error(string message) => Write(message, Brushes.OrangeRed);
    public void Ok(string message) => Write(message, Brushes.ForestGreen);

    private void Write(string message, Brush color)
    {
        if (_target.Dispatcher.CheckAccess())
        {
            Append(message, color);
        }
        else
        {
            _target.Dispatcher.Invoke(() => Append(message, color));
        }
    }

    private void Append(string message, Brush color)
    {
        var paragraph = _target.Document.Blocks.LastBlock as Paragraph;
        if (paragraph == null)
        {
            paragraph = new Paragraph { Margin = new System.Windows.Thickness(0) };
            _target.Document.Blocks.Add(paragraph);
        }

        var run = new Run($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}")
        {
            Foreground = color
        };

        paragraph.Inlines.Add(run);
        _target.ScrollToEnd();
    }
}
