using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Washmachine.Views;
using WinRT.Interop;

namespace Washmachine.Services;

public interface IUserInteractionService
{
    Task<DialogResult> ShowMessageAsync(
        IMainFormView view,
        string message,
        string title,
        DialogButtons buttons,
        DialogIcon icon,
        DialogDefaultButton defaultButton = DialogDefaultButton.Primary);

    Task<string?> SelectFileAsync(
        IMainFormView view,
        string title,
        string filter,
        string initialDirectory);

    Task ShowLargeTextAsync(
        IMainFormView view,
        string title,
        string content,
        string? header = null);

    Task ShowCopyableTextAsync(
        IMainFormView view,
        string title,
        string content,
        string? header = null);

    Task ShowShellcodeTipAsync(IMainFormView view);
    Task ShowGuardRailInfoAsync(IMainFormView view);
}

public sealed class UserInteractionService : IUserInteractionService
{
    public async Task<DialogResult> ShowMessageAsync(
        IMainFormView view,
        string message,
        string title,
        DialogButtons buttons,
        DialogIcon icon,
        DialogDefaultButton defaultButton = DialogDefaultButton.Primary)
    {
        ArgumentNullException.ThrowIfNull(view);

        var dialog = new ContentDialog
        {
            XamlRoot = view.XamlRoot,
            Title = title,
            Content = BuildMessageContent(message, icon)
        };

        ConfigureButtons(dialog, buttons, defaultButton);

        var result = await dialog.ShowAsync();
        return MapDialogResult(buttons, result);
    }

    public async Task<string?> SelectFileAsync(
        IMainFormView view,
        string title,
        string filter,
        string initialDirectory)
    {
        ArgumentNullException.ThrowIfNull(view);

        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };

        foreach (var extension in ParseFileTypes(filter))
        {
            picker.FileTypeFilter.Add(extension);
        }

        var hwnd = WindowNative.GetWindowHandle(view.Window);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task ShowLargeTextAsync(
        IMainFormView view,
        string title,
        string content,
        string? header = null)
    {
        ArgumentNullException.ThrowIfNull(view);

        var dialog = new ContentDialog
        {
            XamlRoot = view.XamlRoot,
            Title = title,
            CloseButtonText = "Close"
        };

        dialog.Content = BuildTextDialogContent(content, header, isCopyable: false);

        await dialog.ShowAsync();
    }

    public async Task ShowCopyableTextAsync(
        IMainFormView view,
        string title,
        string content,
        string? header = null)
    {
        ArgumentNullException.ThrowIfNull(view);

        var dialog = new ContentDialog
        {
            XamlRoot = view.XamlRoot,
            Title = title,
            PrimaryButtonText = "Copy",
            CloseButtonText = "Close"
        };

        dialog.Content = BuildTextDialogContent(content, header, isCopyable: true);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var package = new DataPackage();
                package.SetText(content ?? string.Empty);
                Clipboard.SetContent(package);
                Clipboard.Flush();
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync(
                    view,
                    $"Failed to copy payload: {ex.Message}",
                    "Copy",
                    DialogButtons.Ok,
                    DialogIcon.Error);
            }

            args.Cancel = true;
        };

        await dialog.ShowAsync();
    }

    public async Task ShowShellcodeTipAsync(IMainFormView view)
    {
        const string tipText = "Please paste shellcode as hexadecimal byte escapes.\r\n\r\n" +
                               "- Use \\x followed by exactly two hex digits per byte.\r\n" +
                               "- No spaces, commas, or 0x prefixes.\r\n" +
                               "- Line breaks are okay; format is validated before continuing.\r\n\r\n" +
                               "Example:";

        const string example = "\\x48\\xB8\\x44\\x44\\x44\\x44\\x44\\x44\\x44\\x44\\x50\\x48\\xB8\\x55\\x55\\x55\\x55\\x55\\x55\\x55\\x55\\x50\\x48\\x31\\xC9\\x48\\x89\\xE2\\x49\\x89\\xE0\\x49\\x83\\xC0\\x08\\x4D\\x31\\xC9\\x48\\xB8\\x33\\x33\\x33\\x33\\x33\\x33\\x33\\x33\\x48\\x83\\xEC\\x28\\xFF\\xD0\\x48\\x83\\xC4\\x38\\x48\\xB8\\xEF\\xBE\\xAD\\xDE\\x00\\x00\\x00\\x00\\xEB\\xFE";

        var dialog = new ContentDialog
        {
            XamlRoot = view.XamlRoot,
            Title = "Shellcode Tip",
            CloseButtonText = "OK",
            Content = BuildInfoDialogContent("Shellcode Input Format", tipText, example)
        };

        await dialog.ShowAsync();
    }

    public async Task ShowGuardRailInfoAsync(IMainFormView view)
    {
        const string infoText = "Enter environment conditions in the form NAME#operator#value.\r\n\r\n" +
                                "Operators include equals, contains, biggerthan, and smallerthan.\r\n" +
                                "Separate multiple conditions with commas.";

        const string example = "\"PROCESSOR_LEVEL#equals#6\", \"PATH#contains#System32\"\r\n" +
                               "NUMBER_OF_PROCESSORS#biggerthan#4, USERDOMAIN#equals#ACME\r\n" +
                               "PROCESSOR_LEVEL#smallerthan#10";

        var dialog = new ContentDialog
        {
            XamlRoot = view.XamlRoot,
            Title = "Environment Condition Format",
            CloseButtonText = "OK",
            Content = BuildInfoDialogContent("Environment Condition Format", infoText, example)
        };

        await dialog.ShowAsync();
    }

    private static UIElement BuildMessageContent(string message, DialogIcon icon)
    {
        var text = new TextBlock
        {
            Text = message ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420
        };

        if (icon == DialogIcon.None)
            return text;

        var iconElement = new SymbolIcon
        {
            Symbol = icon switch
            {
                DialogIcon.Warning => Symbol.Warning,
                DialogIcon.Error => Symbol.Clear,
                DialogIcon.Question => Symbol.Help,
                DialogIcon.Information => Symbol.Info,
                _ => Symbol.Info
            },
            Foreground = new SolidColorBrush(icon switch
            {
                DialogIcon.Warning => Colors.Goldenrod,
                DialogIcon.Error => Colors.OrangeRed,
                DialogIcon.Question => Colors.LightSkyBlue,
                DialogIcon.Information => Colors.DeepSkyBlue,
                _ => Colors.Gray
            })
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12
        };

        panel.Children.Add(iconElement);
        panel.Children.Add(text);
        return panel;
    }

    private static void ConfigureButtons(ContentDialog dialog, DialogButtons buttons, DialogDefaultButton defaultButton)
    {
        dialog.PrimaryButtonText = string.Empty;
        dialog.SecondaryButtonText = string.Empty;
        dialog.CloseButtonText = string.Empty;
        dialog.DefaultButton = ContentDialogButton.None;

        switch (buttons)
        {
            case DialogButtons.Ok:
                dialog.CloseButtonText = "OK";
                dialog.DefaultButton = ContentDialogButton.Close;
                break;
            case DialogButtons.YesNo:
                dialog.PrimaryButtonText = "Yes";
                dialog.SecondaryButtonText = "No";
                dialog.DefaultButton = defaultButton == DialogDefaultButton.Secondary
                    ? ContentDialogButton.Secondary
                    : ContentDialogButton.Primary;
                break;
        }
    }

    private static DialogResult MapDialogResult(DialogButtons buttons, ContentDialogResult result)
    {
        return buttons switch
        {
            DialogButtons.Ok => DialogResult.Ok,
            DialogButtons.YesNo => result switch
            {
                ContentDialogResult.Primary => DialogResult.Yes,
                ContentDialogResult.Secondary => DialogResult.No,
                _ => DialogResult.Cancel
            },
            _ => DialogResult.None
        };
    }

    private static UIElement BuildTextDialogContent(string content, string? header, bool isCopyable)
    {
        var container = new StackPanel
        {
            Spacing = 10
        };

        if (!string.IsNullOrWhiteSpace(header))
        {
            container.Children.Add(new TextBlock
            {
                Text = header,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold
            });
        }

        var textBox = new TextBox
        {
            Text = content ?? string.Empty,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            IsReadOnly = true,
            MinHeight = isCopyable ? 260 : 360,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        container.Children.Add(textBox);
        return container;
    }

    private static UIElement BuildInfoDialogContent(string header, string info, string example)
    {
        var container = new StackPanel
        {
            Spacing = 10
        };

        container.Children.Add(new TextBlock
        {
            Text = header,
            FontSize = 18,
            FontWeight = Windows.UI.Text.FontWeights.SemiBold
        });

        container.Children.Add(new TextBlock
        {
            Text = info,
            TextWrapping = TextWrapping.Wrap
        });

        container.Children.Add(new TextBlock
        {
            Text = "Examples:",
            FontWeight = Windows.UI.Text.FontWeights.SemiBold
        });

        container.Children.Add(new TextBox
        {
            Text = example,
            FontFamily = new FontFamily("Consolas"),
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 120,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        return container;
    }

    private static IReadOnlyList<string> ParseFileTypes(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return new[] { "*" };

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var segments = filter.Split('|');

        for (int i = 1; i < segments.Length; i += 2)
        {
            var patterns = segments[i].Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pattern in patterns)
            {
                var trimmed = pattern.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (trimmed == "*" || trimmed == "*.*")
                {
                    results.Add("*");
                    continue;
                }

                if (trimmed.StartsWith("*.", StringComparison.Ordinal))
                {
                    results.Add(trimmed[1..]);
                    continue;
                }

                if (trimmed.StartsWith(".", StringComparison.Ordinal))
                {
                    results.Add(trimmed);
                    continue;
                }
            }
        }

        if (results.Count == 0)
            results.Add("*");

        return results.ToList();
    }
}
