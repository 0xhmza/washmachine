using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using TitleBar = Wpf.Ui.Controls.TitleBar;
using UiButton = Wpf.Ui.Controls.Button;
using UiCard = Wpf.Ui.Controls.Card;
using UiTextBlock = Wpf.Ui.Controls.TextBlock;
using UiTextBox = Wpf.Ui.Controls.TextBox;

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
        var window = new FluentWindow
        {
            Title = title,
            Width = canCopy ? 760 : 960,
            Height = canCopy ? 520 : 680,
            MinWidth = 560,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            ExtendsContentIntoTitleBar = true,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            WindowCornerPreference = Wpf.Ui.Controls.WindowCornerPreference.Round,
            Background = Brushes.Transparent
        };
        window.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = new TitleBar { Title = title };
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        if (!string.IsNullOrWhiteSpace(header))
        {
            var headerCard = new UiCard { Margin = new Thickness(16, 12, 16, 0) };
            var headerPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
            headerPanel.Children.Add(new UiTextBlock
            {
                Text = header,
                FontWeight = FontWeights.SemiBold,
                FontTypography = Wpf.Ui.Controls.FontTypography.Subtitle
            });
            headerCard.Content = headerPanel;
            Grid.SetRow(headerCard, 1);
            root.Children.Add(headerCard);
        }

        var contentCard = new UiCard { Margin = new Thickness(16, 12, 16, 0) };
        var textBox = new UiTextBox
        {
            Text = content ?? string.Empty,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 240
        };
        contentCard.Content = textBox;
        Grid.SetRow(contentCard, 2);
        root.Children.Add(contentCard);

        var buttonCard = new UiCard { Margin = new Thickness(16, 12, 16, 12) };
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 8, 12, 8)
        };

        if (canCopy)
        {
            var copyButton = new UiButton
            {
                Content = "Copy",
                MinWidth = 90,
                Margin = new Thickness(0, 0, 8, 0),
                Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary
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

        var closeButton = new UiButton
        {
            Content = "Close",
            MinWidth = 90,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary
        };
        closeButton.Click += (_, _) => window.Close();
        buttonPanel.Children.Add(closeButton);

        buttonCard.Content = buttonPanel;
        Grid.SetRow(buttonCard, 3);
        root.Children.Add(buttonCard);

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
            ExtendsContentIntoTitleBar = true,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            WindowCornerPreference = Wpf.Ui.Controls.WindowCornerPreference.Round,
            Background = Brushes.Transparent
        };

        window.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = new TitleBar
        {
            Title = title
        };
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var contentCard = new UiCard { Margin = new Thickness(16, 12, 16, 0) };
        var contentPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        contentPanel.Children.Add(new UiTextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        });

        var textBox = new UiTextBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            Text = initialValue ?? string.Empty,
            PlaceholderText = placeholder ?? string.Empty
        };

        contentPanel.Children.Add(textBox);
        contentCard.Content = contentPanel;
        Grid.SetRow(contentCard, 1);
        root.Children.Add(contentCard);

        var buttonCard = new UiCard { Margin = new Thickness(16, 12, 16, 12) };
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 8, 12, 8)
        };

        var okButton = new UiButton
        {
            Content = "OK",
            MinWidth = 90,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary
        };
        okButton.Click += (_, _) =>
        {
            window.Tag = textBox.Text?.Trim();
            window.DialogResult = true;
        };

        var cancelButton = new UiButton
        {
            Content = "Cancel",
            MinWidth = 90,
            IsCancel = true,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary
        };
        cancelButton.Click += (_, _) => window.DialogResult = false;

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        buttonCard.Content = buttonPanel;
        Grid.SetRow(buttonCard, 2);
        root.Children.Add(buttonCard);

        window.Content = root;
        return window;
    }

    private static Window CreateInfoWindow(string title, string headerText, string bodyText, string exampleText)
    {
        var window = new FluentWindow
        {
            Title = title,
            Width = 640,
            Height = 440,
            MinWidth = 560,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            ExtendsContentIntoTitleBar = true,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            WindowCornerPreference = Wpf.Ui.Controls.WindowCornerPreference.Round,
            Background = Brushes.Transparent
        };
        window.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = new TitleBar { Title = title };
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(16, 12, 16, 0)
        };

        var contentPanel = new StackPanel();

        var infoCard = new UiCard { Margin = new Thickness(0, 0, 0, 12) };
        var infoPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        infoPanel.Children.Add(new UiTextBlock
        {
            Text = headerText,
            FontWeight = FontWeights.SemiBold,
            FontTypography = Wpf.Ui.Controls.FontTypography.Subtitle,
            Margin = new Thickness(0, 0, 0, 6)
        });
        infoPanel.Children.Add(new UiTextBlock
        {
            Text = bodyText,
            TextWrapping = TextWrapping.Wrap
        });
        infoCard.Content = infoPanel;
        contentPanel.Children.Add(infoCard);

        var exampleCard = new UiCard();
        var exampleBox = new UiTextBox
        {
            Text = exampleText,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 160
        };
        exampleCard.Content = exampleBox;
        contentPanel.Children.Add(exampleCard);

        scrollViewer.Content = contentPanel;
        Grid.SetRow(scrollViewer, 1);
        root.Children.Add(scrollViewer);

        var buttonCard = new UiCard { Margin = new Thickness(16, 12, 16, 12) };
        var okPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 8, 12, 8)
        };
        var okButton = new UiButton
        {
            Content = "OK",
            MinWidth = 90,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary
        };
        okButton.Click += (_, _) => window.Close();
        okPanel.Children.Add(okButton);
        buttonCard.Content = okPanel;
        Grid.SetRow(buttonCard, 2);
        root.Children.Add(buttonCard);

        window.Content = root;
        return window;
    }
}




