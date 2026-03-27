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
    ComboBox PlaybookCombo { get; }
    ComboBox GenericShellcodeCombo { get; }
    TextBlock EncoderDescriptionTextBlock { get; }
    TextBlock EnvelopeDescriptionTextBlock { get; }
    TextBlock PlaybookPathTextBlock { get; }

    TextBox ShellcodeFileTextBox { get; }
    TextBox ShellcodeRawTextBox { get; }
    TextBox ShellcodeUrlTextBox { get; }
    TextBox ShellcodeUrlFileTextBox { get; }

    Button SubmitButton { get; }

    /// <summary>
    /// Sets whether the Payload Encoding section is interactive.
    /// When <paramref name="enabled"/> is false the section should appear greyed out.
    /// </summary>
    void SetPayloadEncodingEnabled(bool enabled);

    /// <summary>The coordinator that drives this view.</summary>
    Controllers.MainFormCoordinator Coordinator { get; }
}
