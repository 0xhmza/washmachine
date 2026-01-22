using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Washmachine.Views;

public interface IMainFormView
{
    Window Window { get; }
    XamlRoot XamlRoot { get; }
    FrameworkElement RootElement { get; }

    ComboBox EncoderCombo { get; }
    ComboBox EnvelopeCombo { get; }
    ComboBox TemplateCombo { get; }
    ComboBox GenericShellcodeCombo { get; }

    TextBox ShellcodeFileTextBox { get; }
    TextBox ShellcodeRawTextBox { get; }
    TextBox ShellcodeUrlTextBox { get; }

    Button SubmitButton { get; }
}
