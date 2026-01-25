using System.Windows;
using System.Windows.Controls;

namespace Washmachine.Views;

public interface IMainFormView
{
    Window RootWindow { get; }

    ComboBox EncoderCombo { get; }
    ComboBox EnvelopeCombo { get; }
    ComboBox TemplateCombo { get; }
    ComboBox GenericShellcodeCombo { get; }

    TextBox ShellcodeFileTextBox { get; }
    TextBox ShellcodeRawTextBox { get; }
    TextBox ShellcodeUrlTextBox { get; }

    Button SubmitButton { get; }
}
