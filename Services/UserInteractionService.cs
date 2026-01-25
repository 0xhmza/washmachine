using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using TitleBar = Wpf.Ui.Controls.TitleBar;

namespace Washmachine.Services;

public interface IUserInteractionService
{
    MessageBoxResult ShowMessage(
        Window owner,
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage icon,
        MessageBoxResult defaultButton = MessageBoxResult.OK);

    string? SelectFile(
        Window owner,
        string title,
        string filter,
        string initialDirectory);

    void ShowLargeText(
        Window owner,
        string title,
        string content,
        string? header = null);

    void ShowCopyableText(
        Window owner,
        string title,
        string content,
        string? header = null);

    string? PromptText(
        Window owner,
        string title,
        string message,
        string? placeholder = null,
        string? initialValue = null);

    void ShowShellcodeTip(Window owner);
    void ShowGuardRailInfo(Window owner);
}

/// <summary>
/// Thin wrapper around WPF dialogs to keep UI interactions centralized.
/// </summary>
public sealed class UserInteractionService : IUserInteractionService
{
    public MessageBoxResult ShowMessage(
        Window owner,
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage icon,
        MessageBoxResult defaultButton = MessageBoxResult.OK)
        => MessageBox.Show(owner, message, title, buttons, icon, defaultButton);

    public string? SelectFile(
        Window owner,
        string title,
        string filter,
        string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : initialDirectory,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(owner) == true
            ? dialog.FileName
            : null;
    }

    public void ShowLargeText(
        Window owner,
        string title,
        string content,
        string? header = null)
    {
        var dialog = CreateTextWindow(title, content, header, canCopy: false);
        dialog.Owner = owner;
        dialog.ShowDialog();
    }

    public void ShowCopyableText(
        Window owner,
        string title,
        string content,
        string? header = null)
    {
        var dialog = CreateTextWindow(title, content, header, canCopy: true);
        dialog.Owner = owner;
        dialog.ShowDialog();
    }

    public string? PromptText(
        Window owner,
        string title,
        string message,
        string? placeholder = null,
        string? initialValue = null)
    {
        var dialog = CreateInputWindow(title, message, placeholder, initialValue);
        dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.Tag as string : null;
    }

    public void ShowShellcodeTip(Window owner)
    {
        const string tipText =
            "Please paste shellcode as hexadecimal byte escapes.\r\n\r\n" +
            "- Use \\x followed by exactly two hex digits per byte.\r\n" +
            "- No spaces, commas, or 0x prefixes.\r\n" +
            "- Line breaks are okay; format is validated before continuing.\r\n\r\n" +
            "Example:";

        const string example =
            "\\x48\\xB8\\x44\\x44\\x44\\x44\\x44\\x44\\x44\\x44\\x50\\x48\\xB8\\x55\\x55\\x55\\x55\\x55\\x55\\x55\\x55\\x50\\x48\\x31\\xC9\\x48\\x89\\xE2\\x49\\x89\\xE0\\x49\\x83\\xC0\\x08\\x4D\\x31\\xC9\\x48\\xB8\\x33\\x33\\x33\\x33\\x33\\x33\\x33\\x33\\x48\\x83\\xEC\\x28\\xFF\\xD0\\x48\\x83\\xC4\\x38\\x48\\xB8\\xEF\\xBE\\xAD\\xDE\\x00\\x00\\x00\\x00\\xEB\\xFE";

        var dialog = CreateInfoWindow("Shellcode Tip", "Shellcode Input Format", tipText, example);
        dialog.Owner = owner;
        dialog.ShowDialog();
    }

    public void ShowGuardRailInfo(Window owner)
    {
        const string infoText =
            "Enter environment conditions in the form NAME#operator#value.\r\n\r\n" +
            "Operators include equals, contains, biggerthan, and smallerthan.\r\n" +
            "Separate multiple conditions with commas.";

        const string example =
            "\"PROCESSOR_LEVEL#equals#6\", \"PATH#contains#System32\"\r\n" +
            "NUMBER_OF_PROCESSORS#biggerthan#4, USERDOMAIN#equals#ACME\r\n" +
            "PROCESSOR_LEVEL#smallerthan#10";

        var dialog = CreateInfoWindow("Environment Condition Format", "Environment Condition Format", infoText, example);
        dialog.Owner = owner;
        dialog.ShowDialog();
    }

    private static Window CreateTextWindow(string title, string content, string? header, bool canCopy)
    {
        var window = new Window
        {
            Title = title,
            Width = canCopy ? 760 : 960,
            Height = canCopy ? 480 : 640,
            MinWidth = 520,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        if (!string.IsNullOrWhiteSpace(header))
        {
            var headerText = new TextBlock
            {
                Text = header,
                Margin = new Thickness(16, 16, 16, 8),
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetRow(headerText, 0);
            root.Children.Add(headerText);
        }

        var textBox = new TextBox
        {
            Text = content ?? string.Empty,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Margin = new Thickness(16, 0, 16, 0)
        };
        Grid.SetRow(textBox, 1);
        root.Children.Add(textBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 10, 16, 12)
        };

        if (canCopy)
        {
            var copyButton = new Button
            {
                Content = "Copy",
                MinWidth = 90,
                Margin = new Thickness(0, 0, 8, 0)
            };
            copyButton.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(textBox.Text ?? string.Empty);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(window, $"Failed to copy payload: {ex.Message}", "Copy", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            buttonPanel.Children.Add(copyButton);
        }

        var closeButton = new Button
        {
            Content = "Close",
            MinWidth = 90
        };
        closeButton.Click += (_, _) => window.Close();
        buttonPanel.Children.Add(closeButton);

        Grid.SetRow(buttonPanel, 2);
        root.Children.Add(buttonPanel);

        window.Content = root;
        return window;
    }

    private static Window CreateInputWindow(string title, string message, string? placeholder, string? initialValue)
    {
        var window = new FluentWindow
        {
            Title = title,
            Width = 560,
            Height = 260,
            MinWidth = 420,
            MinHeight = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            ExtendsContentIntoTitleBar = true
        };

        window.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
        window.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = new TitleBar
        {
            Title = title
        };
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var promptText = new TextBlock
        {
            Text = message,
            Margin = new Thickness(16, 10, 16, 6),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(promptText, 1);
        root.Children.Add(promptText);

        var textBox = new TextBox
        {
            Margin = new Thickness(16, 0, 16, 6),
            Text = initialValue ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(placeholder))
        {
            textBox.ToolTip = placeholder;
        }

        Grid.SetRow(textBox, 2);
        root.Children.Add(textBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 6, 16, 12)
        };

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 90,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        okButton.Click += (_, _) =>
        {
            window.Tag = textBox.Text?.Trim();
            window.DialogResult = true;
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 90,
            IsCancel = true
        };
        cancelButton.Click += (_, _) => window.DialogResult = false;

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 3);
        root.Children.Add(buttonPanel);

        window.Content = root;
        return window;
    }

    private static Window CreateInfoWindow(string title, string headerText, string bodyText, string exampleText)
    {
        var window = new Window
        {
            Title = title,
            Width = 600,
            Height = 420,
            MinWidth = 520,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var root = new Grid { Margin = new Thickness(16, 16, 16, 12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = headerText,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var info = new TextBlock
        {
            Text = bodyText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(info, 1);
        root.Children.Add(info);

        var exampleBox = new TextBox
        {
            Text = exampleText,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        };
        Grid.SetRow(exampleBox, 2);
        root.Children.Add(exampleBox);

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        okButton.Click += (_, _) => window.Close();
        Grid.SetRow(okButton, 3);
        root.Children.Add(okButton);

        window.Content = root;
        return window;
    }
}




