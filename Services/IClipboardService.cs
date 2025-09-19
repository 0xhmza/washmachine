using System.Windows.Forms;

namespace Washmachine.Services;

public interface IClipboardService
{
    bool ContainsText();
    string GetText(TextDataFormat format = TextDataFormat.UnicodeText);
}
