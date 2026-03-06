using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Washmachine.Logging;

public sealed class RichEditBoxLogger : IAppLogger
{
    private readonly RichEditBox _target;
    private readonly List<(string message, Color color)> _pending = [];
    private bool _ready;

    public RichEditBoxLogger(RichEditBox target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _target.Loaded += (_, _) =>
        {
            _ready = true;
            var toFlush = _pending.ToList();
            _pending.Clear();
            // Defer one dispatch cycle so the underlying Win32 RichEdit is fully initialized
            _target.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => { foreach (var (msg, col) in toFlush) Append(msg, col); });
        };
    }

    public void Info(string message)  => Write(message, Color.FromArgb(255, 220, 220, 220));
    public void Warn(string message)  => Write(message, Color.FromArgb(255, 218, 165,  32));
    public void Error(string message) => Write(message, Color.FromArgb(255, 255,  69,   0));
    public void Ok(string message)    => Write(message, Color.FromArgb(255,  34, 139,  34));

    private void Write(string message, Color color)
    {
        var dq = _target.DispatcherQueue;
        if (dq.HasThreadAccess)
            WriteOnUiThread(message, color);
        else
            dq.TryEnqueue(() => WriteOnUiThread(message, color));
    }

    private void WriteOnUiThread(string message, Color color)
    {
        if (!_ready)
        {
            _pending.Add((message, color));
            return;
        }
        Append(message, color);
    }

    private void Append(string message, Color color)
    {
        _target.IsReadOnly = false;
        try
        {
            var doc = _target.Document;
            doc.GetText(TextGetOptions.None, out string existing);
            int endPos = existing.Length;

            var range = doc.GetRange(endPos, endPos);
            range.CharacterFormat.ForegroundColor = color;
            string text = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
            range.SetText(TextSetOptions.None, text);

            _target.Document.Selection.SetRange(endPos + text.Length, endPos + text.Length);
        }
        finally
        {
            _target.IsReadOnly = true;
        }
    }
}
