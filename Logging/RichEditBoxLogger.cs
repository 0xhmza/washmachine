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
        var doc = _box.Document;
        doc.GetText(Microsoft.UI.Text.TextGetOptions.None, out var existing);
        // Move caret to end and type text so existing RTF formatting is preserved.
        doc.Selection.StartPosition = existing.Length;
        doc.Selection.EndPosition   = existing.Length;
        doc.Selection.TypeText(line + "\r\n");
    }

    public void Info(string message)  => Append($"[INFO]  {message}");
    public void Ok(string message)    => Append($"  OK:  {message}");
    public void Warn(string message)  => Append($"  WARN: {message}");
    public void Error(string message) => Append($"  ERR:  {message}");
    public void Debug(string message) => Append($"  DBG:  {message}");
}
