using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Washmachine.Services;

namespace Washmachine.Views;

public sealed partial class PayloadHistoryPage : Page
{
    private List<PayloadHistoryEntry> _entries = new();

    public PayloadHistoryPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadHistory();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadHistory();

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = HistoryList.SelectedItem as PayloadHistoryEntry;
        DeleteButton.IsEnabled = selected != null;
        ShowSelectedEntry(selected);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not PayloadHistoryEntry entry)
            return;

        var dialog = new ContentDialog
        {
            Title = "Delete entry",
            Content = $"Delete the history entry from {entry.DisplayTimestamp}?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };
        var choice = await dialog.ShowAsync();
        if (choice != ContentDialogResult.Primary)
            return;

        PayloadHistoryStore.Delete(entry.Id);
        LoadHistory();
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Clear all history",
            Content = "This will permanently delete all payload history entries. Continue?",
            PrimaryButtonText = "Clear All",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };
        var choice = await dialog.ShowAsync();
        if (choice != ContentDialogResult.Primary)
            return;

        PayloadHistoryStore.Clear();
        LoadHistory();
    }

    private void LoadHistory()
    {
        HistoryPathValue.Text = PayloadHistoryStore.FilePath;

        try
        {
            _entries = PayloadHistoryStore.Load().ToList();
            HistoryList.ItemsSource = _entries;

            EmptyStateText.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = $"Entries: {_entries.Count}";

            if (_entries.Count > 0)
            {
                HistoryList.SelectedIndex = 0;
            }
            else
            {
                ShowSelectedEntry(null);
            }
        }
        catch (JsonException ex)
        {
            StatusText.Text = $"History file is malformed: {ex.Message}";
            EmptyStateText.Visibility = Visibility.Visible;
            HistoryList.ItemsSource = null;
            ShowSelectedEntry(null);
        }
        catch (IOException ex)
        {
            StatusText.Text = $"Failed to load history file: {ex.Message}";
            EmptyStateText.Visibility = Visibility.Visible;
            HistoryList.ItemsSource = null;
            ShowSelectedEntry(null);
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusText.Text = $"Access denied while reading history: {ex.Message}";
            EmptyStateText.Visibility = Visibility.Visible;
            HistoryList.ItemsSource = null;
            ShowSelectedEntry(null);
        }
    }

    private void ShowSelectedEntry(PayloadHistoryEntry? entry)
    {
        if (entry == null)
        {
            NoSelectionText.Visibility = Visibility.Visible;
            DetailsHost.Visibility = Visibility.Collapsed;
            SetAllDetailText("—");
            return;
        }

        NoSelectionText.Visibility = Visibility.Collapsed;
        DetailsHost.Visibility = Visibility.Visible;

        GeneratedAtValue.Text = entry.GeneratedAtUtc == default
            ? "—"
            : entry.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        PayloadUrlValue.Text = TextOrDash(entry.PayloadUrl);
        SourcePathValue.Text = TextOrDash(entry.SourceFilePath);
        SourceSizeValue.Text = entry.SourceFileSizeBytes > 0
            ? $"{entry.SourceFileSizeBytes:N0} bytes ({FormatBytes(entry.SourceFileSizeBytes)})"
            : "—";
        SourceHashValue.Text = TextOrDash(entry.SourceFileSha256);
        EncoderValue.Text = FormatCodecValue(entry.EncoderIndex, entry.EncoderName, entry.EncoderDescription);
        EnvelopeValue.Text = FormatCodecValue(entry.EnvelopeIndex, entry.EnvelopeName, entry.EnvelopeDescription);
        WebHelperValue.Text = FormatCodecValue(entry.WebHelperIndex, entry.WebHelperName, entry.WebHelperDescription);
        PayloadBytesValue.Text = entry.PayloadLengthBytes > 0 ? $"{entry.PayloadLengthBytes:N0} bytes" : "—";
        PayloadChecksumValue.Text = TextOrDash(entry.PayloadChecksum);
        HistoryPathValue.Text = PayloadHistoryStore.FilePath;

        CommandLineTextBox.Text = TextOrDash(entry.Bin2ShellCommandLine);
        PayloadTextBox.Text = TextOrDash(entry.Payload);
        CppIncludesTextBox.Text = TextOrDash(entry.CppIncludes);
        CppDeclarationsTextBox.Text = TextOrDash(entry.CppDeclarations);
        CppWebFetchTextBox.Text = TextOrDash(entry.CppWebFetch);
        CppPayloadInitTextBox.Text = TextOrDash(entry.CppPayloadInit);
        CppDecodeTextBox.Text = TextOrDash(entry.CppDecode);
        CppPreambleTextBox.Text = TextOrDash(entry.CppPreamble);
        CppBodyTextBox.Text = TextOrDash(entry.CppBody);
    }

    private void SetAllDetailText(string value)
    {
        GeneratedAtValue.Text = value;
        PayloadUrlValue.Text = value;
        SourcePathValue.Text = value;
        SourceSizeValue.Text = value;
        SourceHashValue.Text = value;
        EncoderValue.Text = value;
        EnvelopeValue.Text = value;
        WebHelperValue.Text = value;
        PayloadBytesValue.Text = value;
        PayloadChecksumValue.Text = value;
        HistoryPathValue.Text = PayloadHistoryStore.FilePath;

        CommandLineTextBox.Text = value;
        PayloadTextBox.Text = value;
        CppIncludesTextBox.Text = value;
        CppDeclarationsTextBox.Text = value;
        CppWebFetchTextBox.Text = value;
        CppPayloadInitTextBox.Text = value;
        CppDecodeTextBox.Text = value;
        CppPreambleTextBox.Text = value;
        CppBodyTextBox.Text = value;
    }

    private static string TextOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string FormatCodecValue(int index, string name, string description)
    {
        var baseText = $"{index} - {TextOrDash(name)}";
        if (string.IsNullOrWhiteSpace(description))
            return baseText;

        return $"{baseText} ({description})";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes:N0} B";

        double value = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:N2} {units[unitIndex]}";
    }
}
