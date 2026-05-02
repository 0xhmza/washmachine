using Microsoft.UI.Xaml.Controls;

namespace Washmachine.Logging;

/// <summary>
/// Logs messages into a WinUI <see cref="RichEditBox"/> control.
/// Thread-safe: dispatches to the UI thread automatically.
/// </summary>
public sealed class RichEditBoxLogger : IAppLogger
{
    private readonly RichEditBox _box;

    public RichEditBoxLogger(RichEditBox box) => _box = box;

    private void Append(string line)
    {
        if (_box.DispatcherQueue.HasThreadAccess)
            AppendCore(line);
        else
            _box.DispatcherQueue.TryEnqueue(() => AppendCore(line));
    }

    private void AppendCore(string line)
    {
        // TypeText() is blocked on IsReadOnly boxes — temporarily allow writes.
        _box.IsReadOnly = false;
        try
        {
            var doc = _box.Document;
            doc.GetText(Microsoft.UI.Text.TextGetOptions.None, out var current);
            var len = current.TrimEnd('\0').Length;
            var range = doc.GetRange(len, len);
            range.SetText(Microsoft.UI.Text.TextSetOptions.None, line + "\r\n");
            // Scroll to bottom
            _box.Document.Selection.StartPosition = int.MaxValue;
            _box.Document.Selection.EndPosition   = int.MaxValue;
        }
        finally
        {
            _box.IsReadOnly = true;
        }
    }

    public void Info(string message)  => Append($"[INFO]  {message}");
    public void Ok(string message)    => Append($"  OK:  {message}");
    public void Warn(string message)  => Append($"  WARN: {message}");
    public void Error(string message) => Append($"  ERR:  {message}");
    public void Debug(string message) => Append($"  DBG:  {message}");
}
