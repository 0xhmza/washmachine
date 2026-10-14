using Windows.ApplicationModel.DataTransfer;

namespace Washmachine.Services;

public interface IClipboardService
{
    bool ContainsText();
    Task<string> GetTextAsync();
}

public sealed class ClipboardService : IClipboardService
{
    public bool ContainsText()
    {
        var content = Clipboard.GetContent();
        return content.Contains(StandardDataFormats.Text);
    }

    public async Task<string> GetTextAsync()
    {
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Text))
            throw new InvalidOperationException("Clipboard does not contain text.");

        return await content.GetTextAsync();
    }
}
