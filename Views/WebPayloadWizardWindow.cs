using System.Net.Http;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Washmachine.Models;
using Washmachine.Services;
using Windows.Graphics;

namespace Washmachine.Views;

/// <summary>
/// Result returned by the web payload wizard when the user completes the flow.
/// </summary>
public sealed class WebPayloadWizardResult
{
    public int EncoderIndex { get; set; }
    public int EnvelopeIndex { get; set; }
    public int WebHelperIndex { get; set; }
    public string PayloadUrl { get; set; } = string.Empty;
    public Bin2ShellWebOutput WebOutput { get; set; } = new();
}

/// <summary>
/// Three-step wizard for configuring a web-mode payload via Bin2Shell.
/// Step 1: Choose encoder, envelope, web helper.
/// Step 2: Shows payload text, asks user to upload and provide URL.
/// Step 3: Verifies the URL returns the correct payload.
/// </summary>
public sealed class WebPayloadWizardWindow
{
    private readonly Window _window;
    private readonly TaskCompletionSource<WebPayloadWizardResult?> _tcs;
    private readonly ShellcodeEncodingCatalog _catalog;
    private readonly Func<int, int, int, Task<Bin2ShellWebOutput>> _runBin2Shell;

    // Step 1 controls
    private readonly ComboBox _encoderCombo;
    private readonly ComboBox _envelopeCombo;
    private readonly ComboBox _webHelperCombo;
    private StackPanel _webHelperPanel = null!;

    // Step 2 controls
    private TextBox _payloadTextBox = null!;
    private TextBox _urlTextBox = null!;
    private TextBlock _payloadLenLabel = null!;

    // Step 3 controls
    private TextBlock _verifyStatusText = null!;
    private FontIcon _verifyIcon = null!;
    private Button _verifyButton = null!;

    // Navigation
    private readonly StackPanel[] _pages;
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private readonly Button _finishButton;
    private readonly TextBlock _stepLabel;
    private int _currentPage;

    // State
    private Bin2ShellWebOutput? _webOutput;
    private bool _urlVerified;

    private WebPayloadWizardWindow(
        ShellcodeEncodingCatalog catalog,
        Func<int, int, int, Task<Bin2ShellWebOutput>> runBin2Shell,
        TaskCompletionSource<WebPayloadWizardResult?> tcs)
    {
        _catalog = catalog;
        _runBin2Shell = runBin2Shell;
        _tcs = tcs;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // title bar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // content
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // nav buttons

        // Title bar
        var titleBar = new Grid { Height = 48 };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var titleIcon = new FontIcon { Glyph = "\uE71B", FontSize = 16, Margin = new Thickness(16, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleText = new TextBlock { Text = "Web Payload Wizard", VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
        Grid.SetColumn(titleIcon, 0);
        Grid.SetColumn(titleText, 1);
        titleBar.Children.Add(titleIcon);
        titleBar.Children.Add(titleText);
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        // Build pages
        _encoderCombo = new ComboBox { Width = 300 };
        _envelopeCombo = new ComboBox { Width = 300 };
        _webHelperCombo = new ComboBox { Width = 300 };

        var page1 = BuildPage1();
        var page2 = BuildPage2();
        var page3 = BuildPage3();
        _pages = new[] { page1, page2, page3 };

        var contentHost = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var page in _pages)
        {
            page.Visibility = Visibility.Collapsed;
            contentHost.Children.Add(page);
        }
        page1.Visibility = Visibility.Visible;

        var scrollViewer = new ScrollViewer
        {
            Content = contentHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scrollViewer, 1);
        root.Children.Add(scrollViewer);

        // Navigation bar
        _stepLabel = new TextBlock { Text = "Step 1 of 3", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        _backButton = new Button { Content = "Back", MinWidth = 90 };
        _backButton.Click += (_, _) => Navigate(-1);
        _backButton.IsEnabled = false;

        _nextButton = new Button { Content = "Next", MinWidth = 90, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        _nextButton.Click += OnNextClick;

        _finishButton = new Button { Content = "Finish", MinWidth = 90, Style = (Style)Application.Current.Resources["AccentButtonStyle"], Visibility = Visibility.Collapsed };
        _finishButton.Click += (_, _) => Finish();

        var cancelButton = new Button { Content = "Cancel", MinWidth = 90 };
        cancelButton.Click += (_, _) => Cancel();

        var navPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 8, 16, 16),
            Spacing = 8
        };
        navPanel.Children.Add(_stepLabel);
        navPanel.Children.Add(_backButton);
        navPanel.Children.Add(_nextButton);
        navPanel.Children.Add(_finishButton);
        navPanel.Children.Add(cancelButton);
        Grid.SetRow(navPanel, 2);
        root.Children.Add(navPanel);

        _window = new Window
        {
            Title = "Web Payload Wizard",
            Content = root,
            SystemBackdrop = new DesktopAcrylicBackdrop()
        };
        _window.ExtendsContentIntoTitleBar = true;
        _window.SetTitleBar(titleBar);
        _window.Closed += OnWindowClosedCancel;

        PopulateCombos();
    }

    public static Task<WebPayloadWizardResult?> ShowAsync(
        nint ownerHandle,
        ShellcodeEncodingCatalog catalog,
        Func<int, int, int, Task<Bin2ShellWebOutput>> runBin2Shell)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(runBin2Shell);

        var tcs = new TaskCompletionSource<WebPayloadWizardResult?>();
        var wiz = new WebPayloadWizardWindow(catalog, runBin2Shell, tcs);

        wiz._window.AppWindow.Resize(new SizeInt32(820, 680));
        var display = DisplayArea.GetFromWindowId(wiz._window.AppWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        wiz._window.AppWindow.Move(new PointInt32(
            work.X + (work.Width - 820) / 2,
            work.Y + (work.Height - 680) / 2));

        // Make the wizard modal: disable the owner window until wizard closes.
        if (ownerHandle != 0)
        {
            EnableWindow(ownerHandle, false);
            wiz._window.Closed += (_, _) => EnableWindow(ownerHandle, true);
        }

        wiz._window.Activate();
        return tcs.Task;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnableWindow(nint hWnd, bool bEnable);

    private StackPanel BuildPage1()
    {
        var panel = new StackPanel { Margin = new Thickness(24, 16, 24, 16), Spacing = 16 };

        panel.Children.Add(new TextBlock
        {
            Text = "Configure Bin2Shell Web Mode",
            FontSize = 18,
            FontWeight = new Windows.UI.Text.FontWeight(600)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Choose the encoder, envelope, and web helper that Bin2Shell will use to prepare your payload for web delivery.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Colors.Gray)
        });

        panel.Children.Add(CreateLabeledControl("Encoder:", _encoderCombo));
        panel.Children.Add(CreateLabeledControl("Envelope:", _envelopeCombo));

        _webHelperPanel = CreateLabeledControl("Web Helper:", _webHelperCombo);
        panel.Children.Add(_webHelperPanel);

        return panel;
    }

    private StackPanel BuildPage2()
    {
        var panel = new StackPanel { Margin = new Thickness(24, 16, 24, 16), Spacing = 12 };

        panel.Children.Add(new TextBlock
        {
            Text = "Upload Payload",
            FontSize = 18,
            FontWeight = new Windows.UI.Text.FontWeight(600)
        });

        _payloadLenLabel = new TextBlock
        {
            Text = "Payload length: --",
            Foreground = new SolidColorBrush(Colors.Gray)
        };
        panel.Children.Add(_payloadLenLabel);

        panel.Children.Add(new TextBlock
        {
            Text = "Copy the payload text below and upload it to an HTTP(S) endpoint. Then enter the URL where the payload is hosted.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Colors.Gray)
        });

        _payloadTextBox = new TextBox
        {
            FontFamily = new FontFamily("Consolas"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            IsReadOnly = true,
            Height = 160,
            Margin = new Thickness(0, 4, 0, 0)
        };
        panel.Children.Add(_payloadTextBox);

        var copyButton = new Button { Content = "Copy to clipboard", MinWidth = 140 };
        copyButton.Click += (_, _) =>
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(_payloadTextBox.Text ?? string.Empty);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        };
        panel.Children.Add(copyButton);

        panel.Children.Add(new TextBlock { Text = "Payload URL:", Margin = new Thickness(0, 8, 0, 0) });
        _urlTextBox = new TextBox { PlaceholderText = "https://example.com/payload.bin", Margin = new Thickness(0, 4, 0, 0) };
        _urlTextBox.TextChanged += (_, _) => _urlVerified = false;
        panel.Children.Add(_urlTextBox);

        return panel;
    }

    private StackPanel BuildPage3()
    {
        var panel = new StackPanel { Margin = new Thickness(24, 16, 24, 16), Spacing = 12 };

        panel.Children.Add(new TextBlock
        {
            Text = "Verify Payload URL",
            FontSize = 18,
            FontWeight = new Windows.UI.Text.FontWeight(600)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Click Verify to confirm the URL returns the correct payload content.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Colors.Gray)
        });

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        _verifyIcon = new FontIcon { Glyph = "\uE9CE", FontSize = 20 };
        _verifyStatusText = new TextBlock { Text = "Not verified yet.", VerticalAlignment = VerticalAlignment.Center };
        statusRow.Children.Add(_verifyIcon);
        statusRow.Children.Add(_verifyStatusText);
        panel.Children.Add(statusRow);

        _verifyButton = new Button { Content = "Verify", MinWidth = 120, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        _verifyButton.Click += OnVerifyClick;
        panel.Children.Add(_verifyButton);

        return panel;
    }

    private static StackPanel CreateLabeledControl(string label, FrameworkElement control)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(control);
        return panel;
    }

    private void PopulateCombos()
    {
        PopulateCombo(_encoderCombo, _catalog.Encoders);
        PopulateCombo(_envelopeCombo, _catalog.Envelopes);
        PopulateCombo(_webHelperCombo, _catalog.WebHelpers);

        _encoderCombo.SelectedIndex = _encoderCombo.Items.Count > 0 ? 0 : -1;
        _envelopeCombo.SelectedIndex = _envelopeCombo.Items.Count > 0 ? 0 : -1;
        _webHelperCombo.SelectedIndex = _webHelperCombo.Items.Count > 0 ? 0 : -1;

        _webHelperPanel.Visibility = _webHelperCombo.Items.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static void PopulateCombo(ComboBox combo, IReadOnlyList<ShellcodeEncodingItem> items)
    {
        combo.Items.Clear();
        foreach (var item in items.OrderBy(i => i.Index))
            combo.Items.Add(item);
        combo.DisplayMemberPath = nameof(ShellcodeEncodingItem.DisplayText);
    }

    private void Navigate(int direction)
    {
        int target = _currentPage + direction;
        if (target < 0 || target >= _pages.Length) return;

        _pages[_currentPage].Visibility = Visibility.Collapsed;
        _currentPage = target;
        _pages[_currentPage].Visibility = Visibility.Visible;

        _backButton.IsEnabled = _currentPage > 0;
        _nextButton.Visibility = _currentPage < _pages.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
        _finishButton.Visibility = _currentPage == _pages.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
        _stepLabel.Text = $"Step {_currentPage + 1} of {_pages.Length}";
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_currentPage == 0)
        {
            // Run bin2shell before going to page 2
            _nextButton.IsEnabled = false;
            try
            {
                int encoderIdx = GetSelectedIndex(_encoderCombo);
                int envelopeIdx = GetSelectedIndex(_envelopeCombo);
                int webHelperIdx = GetSelectedIndex(_webHelperCombo);

                _webOutput = await _runBin2Shell(encoderIdx, envelopeIdx, webHelperIdx);

                _payloadTextBox.Text = _webOutput.Payload;
                int displayLen = _webOutput.PayloadLen > 0
                    ? _webOutput.PayloadLen
                    : EstimatePayloadByteCount(_webOutput.Payload);
                _payloadLenLabel.Text = $"Payload length: {displayLen} bytes";
                _urlVerified = false;
                _verifyStatusText.Text = "Not verified yet.";
                _verifyIcon.Glyph = "\uE9CE";
                _verifyIcon.Foreground = null;
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "Bin2Shell Error",
                    Content = $"Failed to generate web payload:\n\n{ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = _window.Content.XamlRoot
                };
                await dialog.ShowAsync();
                _nextButton.IsEnabled = true;
                return;
            }
            _nextButton.IsEnabled = true;
        }

        Navigate(1);
    }

    private async void OnVerifyClick(object sender, RoutedEventArgs e)
    {
        string url = _urlTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            _verifyStatusText.Text = "Enter a URL first.";
            _verifyIcon.Glyph = "\uEA39";
            _verifyIcon.Foreground = new SolidColorBrush(Colors.Orange);
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _verifyStatusText.Text = "URL must start with http:// or https://";
            _verifyIcon.Glyph = "\uEA39";
            _verifyIcon.Foreground = new SolidColorBrush(Colors.Orange);
            return;
        }

        _verifyButton.IsEnabled = false;
        _verifyStatusText.Text = "Verifying...";
        _verifyIcon.Glyph = "\uE895";
        _verifyIcon.Foreground = null;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var response = await client.GetAsync(uri);
            int statusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                _verifyStatusText.Text = $"Warning: HTTP {statusCode}. The server returned an error, but you can still proceed.";
                _verifyIcon.Glyph = "\uEA39";
                _verifyIcon.Foreground = new SolidColorBrush(Colors.Orange);
                _urlVerified = false;
                _verifyButton.IsEnabled = true;
                return;
            }

            string content = await response.Content.ReadAsStringAsync();
            string expectedPayload = NormalizePayloadText(_webOutput?.Payload ?? string.Empty);
            string actualContent = NormalizePayloadText(content);

            if (string.Equals(expectedPayload, actualContent, StringComparison.Ordinal))
            {
                _verifyStatusText.Text = $"Verified! HTTP {statusCode}. Payload matches.";
                _verifyIcon.Glyph = "\uE73E";
                _verifyIcon.Foreground = new SolidColorBrush(Colors.Green);
                _urlVerified = true;
            }
            else
            {
                _verifyStatusText.Text = $"Warning: HTTP {statusCode} but payload content does not match. You can still proceed.";
                _verifyIcon.Glyph = "\uEA39";
                _verifyIcon.Foreground = new SolidColorBrush(Colors.Orange);
                _urlVerified = false;
            }
        }
        catch (Exception ex)
        {
            _verifyStatusText.Text = $"Verification failed: {ex.Message}";
            _verifyIcon.Glyph = "\uEA39";
            _verifyIcon.Foreground = new SolidColorBrush(Colors.Red);
            _urlVerified = false;
        }

        _verifyButton.IsEnabled = true;
    }

    private async void Finish()
    {
        string url = _urlTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            _verifyStatusText.Text = "Enter a payload URL before finishing.";
            _verifyIcon.Glyph = "\uEA39";
            _verifyIcon.Foreground = new SolidColorBrush(Colors.Orange);
            return;
        }

        if (_webOutput == null)
        {
            _verifyStatusText.Text = "No payload generated. Go back and try again.";
            return;
        }

        if (!_urlVerified)
        {
            var dialog = new ContentDialog
            {
                Title = "URL not verified",
                Content = "The payload URL has not been verified. The compiled binary may fail at runtime if the URL is unreachable or returns unexpected content.\n\nContinue anyway?",
                PrimaryButtonText = "Continue",
                CloseButtonText = "Go back",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = _window.Content.XamlRoot
            };
            var choice = await dialog.ShowAsync();
            if (choice != ContentDialogResult.Primary)
                return;
        }

        _webOutput.ReplacePayloadUrl(url);

        var result = new WebPayloadWizardResult
        {
            EncoderIndex = GetSelectedIndex(_encoderCombo),
            EnvelopeIndex = GetSelectedIndex(_envelopeCombo),
            WebHelperIndex = GetSelectedIndex(_webHelperCombo),
            PayloadUrl = url,
            WebOutput = _webOutput
        };

        _tcs.TrySetResult(result);
        _window.Closed -= OnWindowClosedCancel;
        _window.Close();
    }

    private void Cancel()
    {
        _tcs.TrySetResult(null);
        _window.Closed -= OnWindowClosedCancel;
        _window.Close();
    }

    private void OnWindowClosedCancel(object sender, WindowEventArgs e)
        => _tcs.TrySetResult(null);

    private static int GetSelectedIndex(ComboBox combo)
    {
        if (combo.SelectedItem is ShellcodeEncodingItem item)
            return item.Index;
        return 0;
    }

    /// <summary>
    /// Estimates the byte count from a hex payload string like "0x48 0x83 0xEC ...".
    /// Falls back to character count if no hex tokens are found.
    /// </summary>
    private static int EstimatePayloadByteCount(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return 0;

        // Count 0xNN tokens
        int count = 0;
        int idx = 0;
        while ((idx = payload.IndexOf("0x", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += 2;
        }

        return count > 0 ? count : payload.Length;
    }

    /// <summary>
    /// Normalizes a hex payload string for comparison by collapsing all whitespace
    /// (newlines, tabs, multiple spaces) into single spaces and trimming.
    /// This allows matching even when line breaks differ (e.g. pastebin vs original).
    /// </summary>
    private static string NormalizePayloadText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Replace all whitespace runs with a single space.
        var sb = new System.Text.StringBuilder(text.Length);
        bool prevWasSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevWasSpace)
                {
                    sb.Append(' ');
                    prevWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                prevWasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }
}
