using System.Windows;

namespace Washmachine.Services;

public interface IClipboardService
{
    bool ContainsText();
    string GetText(TextDataFormat format = TextDataFormat.UnicodeText);
}

public sealed class ClipboardService : IClipboardService
{
    public bool ContainsText() => Clipboard.ContainsText();

    public string GetText(TextDataFormat format = TextDataFormat.UnicodeText)
    {
        if (!ContainsText())
            throw new InvalidOperationException("Clipboard does not contain text.");

        return Clipboard.GetText(format);
    }
}
