using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Washmachine.Views;

public interface IMainFormView
{
    /// <summary>XamlRoot for ContentDialog and file pickers.</summary>
    XamlRoot ViewXamlRoot { get; }

    /// <summary>Win32 HWND for MessageBoxW and Win32 interop.</summary>
    nint WindowHandle { get; }

    /// <summary>Root DependencyObject for visual-tree walking.</summary>
    DependencyObject ContentRoot { get; }

    ComboBox EncoderCombo { get; }
    ComboBox EnvelopeCombo { get; }
    ComboBox TemplateCombo { get; }
    ComboBox GenericShellcodeCombo { get; }

    TextBox ShellcodeFileTextBox { get; }
    TextBox ShellcodeRawTextBox { get; }
    TextBox ShellcodeUrlTextBox { get; }

    Button SubmitButton { get; }
}
