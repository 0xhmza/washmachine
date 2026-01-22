using Windows.ApplicationModel.DataTransfer;

namespace Washmachine.Services;

public interface IClipboardService
{
    Task<bool> ContainsTextAsync();
    Task<string> GetTextAsync();
}

public sealed class ClipboardService : IClipboardService
{
    public Task<bool> ContainsTextAsync()
    {
        var content = Clipboard.GetContent();
        return Task.FromResult(content != null && content.Contains(StandardDataFormats.Text));
    }

    public async Task<string> GetTextAsync()
    {
        var content = Clipboard.GetContent();
        if (content == null || !content.Contains(StandardDataFormats.Text))
            throw new InvalidOperationException("Clipboard does not contain text.");

        return await content.GetTextAsync();
    }
}
