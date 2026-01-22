using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.Text;

namespace Washmachine.Logging;

public sealed class RichEditBoxLogger : IAppLogger
{
    private readonly RichEditBox _target;
    private readonly DispatcherQueue _dispatcher;

    public RichEditBoxLogger(RichEditBox target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _dispatcher = _target.DispatcherQueue;
        _target.IsReadOnly = true;
        _target.IsSpellCheckEnabled = false;
        _target.IsTextPredictionEnabled = false;
    }

    public void Info(string message) => Write(message, Colors.Gainsboro);
    public void Warn(string message) => Write(message, Colors.Goldenrod);
    public void Error(string message) => Write(message, Colors.OrangeRed);
    public void Ok(string message) => Write(message, Colors.ForestGreen);

    private void Write(string message, Color color)
    {
        if (_dispatcher.HasThreadAccess)
        {
            Append(message, color);
            return;
        }

        _dispatcher.TryEnqueue(() => Append(message, color));
    }

    private void Append(string message, Color color)
    {
        var doc = _target.Document;
        doc.GetText(TextGetOptions.None, out var existing);
        int length = existing?.Length ?? 0;
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";

        var range = doc.GetRange(length, length);
        range.CharacterFormat.ForegroundColor = color;
        range.SetText(TextSetOptions.None, line);

        var selection = doc.Selection;
        int caret = length + line.Length;
        selection.SetRange(caret, caret);
        selection.ScrollIntoView(PointOptions.None);
    }
}
