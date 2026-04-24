using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Washmachine.Models;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Washmachine.Services;

public interface IUserInteractionService
{
    MsgBoxResult ShowMessage(
        nint hwnd,
        string message,
        string title,
        MsgBoxButton buttons,
        MsgBoxIcon icon);

    Task<string?> SelectFileAsync(
        nint hwnd,
        string title,
        string filter,
        string initialDirectory);

    void ShowLargeText(
        nint hwnd,
        string title,
        string content,
        string? header = null);

    void ShowCopyableText(
        nint hwnd,
        string title,
        string content,
        string? header = null);

    Task<string?> PromptTextAsync(
        XamlRoot xamlRoot,
        string title,
        string message,
        string? placeholder = null,
        string? initialValue = null);

    void ShowShellcodeTip(nint hwnd);
    void ShowGuardRailInfo(nint hwnd);
}

public sealed class UserInteractionService : IUserInteractionService
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    private const uint MB_OK               = 0x00000000;
    private const uint MB_OKCANCEL         = 0x00000001;
    private const uint MB_YESNOCANCEL      = 0x00000003;
    private const uint MB_YESNO            = 0x00000004;
    private const uint MB_ICONERROR        = 0x00000010;
    private const uint MB_ICONQUESTION     = 0x00000020;
    private const uint MB_ICONWARNING      = 0x00000030;
    private const uint MB_ICONINFORMATION  = 0x00000040;
    private const uint MB_DEFBUTTON1       = 0x00000000;

    public MsgBoxResult ShowMessage(
        nint hwnd,
        string message,
        string title,
        MsgBoxButton buttons,
        MsgBoxIcon icon)
    {
        uint uType = buttons switch
        {
            MsgBoxButton.OKCancel    => MB_OKCANCEL,
            MsgBoxButton.YesNo       => MB_YESNO,
            MsgBoxButton.YesNoCancel => MB_YESNOCANCEL,
            _                        => MB_OK
        };

        uType |= icon switch
        {
            MsgBoxIcon.Error       => MB_ICONERROR,
            MsgBoxIcon.Warning     => MB_ICONWARNING,
            MsgBoxIcon.Information => MB_ICONINFORMATION,
            MsgBoxIcon.Question    => MB_ICONQUESTION,
            _                      => 0
        };

        uType |= MB_DEFBUTTON1;
        int result = MessageBoxW(hwnd, message ?? string.Empty, title ?? string.Empty, uType);
        return (MsgBoxResult)result;
    }

    public async Task<string?> SelectFileAsync(
        nint hwnd,
        string title,
        string filter,
        string initialDirectory)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };

        InitializeWithWindow.Initialize(picker, hwnd);

        // Parse WPF-style filter: "YAML files (*.yaml;*.yml)|*.yaml;*.yml|All files (*.*)|*.*"
        // Extract extensions from even-indexed segments after splitting by |
        var parts = (filter ?? string.Empty).Split('|');
        bool addedAny = false;
        for (int i = 1; i < parts.Length; i += 2)
        {
            foreach (var ext in parts[i].Split(';'))
            {
                string clean = ext.Trim().TrimStart('*');
                if (clean == ".*" || clean == ".*")
                {
                    picker.FileTypeFilter.Add("*");
                    addedAny = true;
                }
                else if (!string.IsNullOrWhiteSpace(clean))
                {
                    picker.FileTypeFilter.Add(clean);
                    addedAny = true;
                }
            }
        }

        if (!addedAny)
            picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public void ShowLargeText(nint hwnd, string title, string content, string? header = null)
    {
        var w = CreateTextWindow(title, content, header, canCopy: false, hwnd);
        w.Activate();
    }

    public void ShowCopyableText(nint hwnd, string title, string content, string? header = null)
    {
        var w = CreateTextWindow(title, content, header, canCopy: true, hwnd);
        w.Activate();
    }

    public async Task<string?> PromptTextAsync(
        XamlRoot xamlRoot,
        string title,
        string message,
        string? placeholder = null,
        string? initialValue = null)
    {
        var textBox = new TextBox
        {
            PlaceholderText = placeholder ?? string.Empty,
            Text = initialValue ?? string.Empty,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(textBox);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text?.Trim() : null;
    }

    public void ShowShellcodeTip(nint hwnd)
    {
        const string tip =
            "Please paste shellcode as hexadecimal byte escapes.\r\n\r\n" +
            "- Use \\x followed by exactly two hex digits per byte.\r\n" +
            "- No spaces, commas, or 0x prefixes.\r\n" +
            "- Line breaks are okay; format is validated before continuing.\r\n\r\n" +
            "Example:";

        const string example =
            "\\x48\\xB8\\x44\\x44\\x44\\x44\\x44\\x44\\x44\\x44\\x50\\x48\\xB8\\x55\\x55\\x55\\x55" +
            "\\x55\\x55\\x55\\x55\\x50\\x48\\x31\\xC9\\x48\\x89\\xE2\\x49\\x89\\xE0\\x49\\x83\\xC0" +
            "\\x08\\x4D\\x31\\xC9\\x48\\xB8\\x33\\x33\\x33\\x33\\x33\\x33\\x33\\x33\\x48\\x83\\xEC" +
            "\\x28\\xFF\\xD0\\x48\\x83\\xC4\\x38\\x48\\xB8\\xEF\\xBE\\xAD\\xDE\\x00\\x00\\x00\\x00\\xEB\\xFE";

        CreateInfoWindow("Shellcode Tip", "Shellcode Input Format", tip, example, hwnd).Activate();
    }

    public void ShowGuardRailInfo(nint hwnd)
    {
        const string info =
            "Enter environment conditions in the form NAME#operator#value.\r\n\r\n" +
            "Operators include equals, contains, biggerthan, and smallerthan.\r\n" +
            "Separate multiple conditions with commas.";

        const string example =
            "\"PROCESSOR_LEVEL#equals#6\", \"PATH#contains#System32\"\r\n" +
            "NUMBER_OF_PROCESSORS#biggerthan#4, USERDOMAIN#equals#ACME\r\n" +
            "PROCESSOR_LEVEL#smallerthan#10";

        CreateInfoWindow("Environment Condition Format", "Environment Condition Format", info, example, hwnd).Activate();
    }

    private static Window CreateTextWindow(string title, string content, string? header, bool canCopy, nint ownerHwnd)
    {
        int width  = canCopy ? 760 : 960;
        int height = canCopy ? 520 : 680;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        if (!string.IsNullOrWhiteSpace(header))
        {
            var headerBlock = new TextBlock
            {
                Text = header,
                FontSize = 14,
                Margin = new Thickness(16, 12, 16, 0),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(headerBlock, 0);
            root.Children.Add(headerBlock);
        }

        var textBox = new TextBox
        {
            Text = content ?? string.Empty,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 240,
            Margin = new Thickness(16, 12, 16, 0)
        };
        ScrollViewer.SetVerticalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);
        Grid.SetRow(textBox, 1);
        root.Children.Add(textBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 12, 16, 12),
            Spacing = 8
        };
        Grid.SetRow(buttonPanel, 2);

        var window = new Window { Title = title, Content = root };
        window.AppWindow.Resize(new SizeInt32(width, height));

        var display = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        window.AppWindow.Move(new PointInt32(
            work.X + (work.Width - width) / 2,
            work.Y + (work.Height - height) / 2));

        if (canCopy)
        {
            var copyBtn = new Button { Content = "Copy", MinWidth = 90 };
            copyBtn.Click += (_, _) =>
            {
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(textBox.Text ?? string.Empty);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            };
            buttonPanel.Children.Add(copyBtn);
        }

        var closeBtn = new Button
        {
            Content = "Close",
            MinWidth = 90,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        closeBtn.Click += (_, _) => window.Close();
        buttonPanel.Children.Add(closeBtn);

        root.Children.Add(buttonPanel);
        return window;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnableWindow(nint hWnd, bool bEnable);

    private static Window CreateInfoWindow(string title, string headerText, string bodyText, string exampleText, nint ownerHwnd)
    {
        const int width = 640;
        const int height = 440;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // title bar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // content
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // buttons

        // Custom title bar (matching wizard style)
        var titleBar = new Grid { Height = 48 };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var titleIcon = new FontIcon
        {
            Glyph = "\uE946",
            FontSize = 16,
            Margin = new Thickness(16, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var titleText = new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14
        };
        Grid.SetColumn(titleIcon, 0);
        Grid.SetColumn(titleText, 1);
        titleBar.Children.Add(titleIcon);
        titleBar.Children.Add(titleText);
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        // Content
        var contentPanel = new StackPanel { Margin = new Thickness(24, 8, 24, 0), Spacing = 12 };

        contentPanel.Children.Add(new TextBlock
        {
            Text = headerText,
            FontSize = 18,
            FontWeight = new Windows.UI.Text.FontWeight(600)
        });

        contentPanel.Children.Add(new TextBlock
        {
            Text = bodyText,
            TextWrapping = TextWrapping.Wrap,
            Foreground = App.ThemeBrush("TextFillColorSecondaryBrush")
        });

        contentPanel.Children.Add(new TextBox
        {
            Text = exampleText,
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 100
        });

        var sv = new ScrollViewer
        {
            Content = contentPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(sv, 1);
        root.Children.Add(sv);

        // Bottom button bar
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(24, 12, 24, 16),
            Spacing = 8
        };
        Grid.SetRow(buttonPanel, 2);

        var window = new Window
        {
            Title = title,
            Content = root,
            SystemBackdrop = new DesktopAcrylicBackdrop()
        };
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);

        window.AppWindow.Resize(new SizeInt32(width, height));
        var display = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        window.AppWindow.Move(new PointInt32(
            work.X + (work.Width - width) / 2,
            work.Y + (work.Height - height) / 2));

        // Make modal
        if (ownerHwnd != 0)
        {
            EnableWindow(ownerHwnd, false);
            window.Closed += (_, _) => EnableWindow(ownerHwnd, true);
        }

        var okBtn = new Button
        {
            Content = "OK",
            MinWidth = 90,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        okBtn.Click += (_, _) => window.Close();
        buttonPanel.Children.Add(okBtn);

        root.Children.Add(buttonPanel);
        return window;
    }
}
