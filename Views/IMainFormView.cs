using System.Windows.Forms;

namespace Washmachine.Views;

public interface IMainFormView : IWin32Window
{
    Control RootControl { get; }

    ComboBox EncoderCombo { get; }
    ComboBox EnvelopeCombo { get; }
    ComboBox TemplateCombo { get; }
    FlowLayoutPanel SnippetPickerPanel { get; }
    ComboBox GenericShellcodeCombo { get; }

    TextBox ShellcodeFileTextBox { get; }
    TextBox ShellcodeRawTextBox { get; }
    TextBox ShellcodeUrlTextBox { get; }

    Button SubmitButton { get; }
}
