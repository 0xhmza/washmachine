using System.Windows.Forms;

namespace Washmachine.Views;

public interface IMainFormView : IWin32Window
{
    Control RootControl { get; }

    ComboBox EncoderCombo { get; }
    ComboBox CompressorCombo { get; }
    ComboBox EnvelopeCombo { get; }

    ComboBox ProcessInjectionCombo { get; }
    ComboBox ShellcodeExecutionCombo { get; }
    ComboBox UacBypassCombo { get; }
    ComboBox GenericShellcodeCombo { get; }
    ComboBox GuardrailCombo { get; }

    ListBox AntiDebugList { get; }

    TextBox ShellcodeFileTextBox { get; }
    TextBox ShellcodeRawTextBox { get; }
    TextBox ShellcodeUrlTextBox { get; }
    TextBox ProcessInjectionTargetTextBox { get; }
    TextBox GuardrailParameterTextBox { get; }

    Button SubmitButton { get; }
}
