using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Washmachine.Services;
using Windows.ApplicationModel.DataTransfer;

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
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
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
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        PayloadHistoryStore.Clear();
        LoadHistory();
    }

    private void LoadHistory()
    {
        try
        {
            _entries = PayloadHistoryStore.Load().ToList();

            bool hasEntries = _entries.Count > 0;
            EmptyStateBorder.Visibility = hasEntries ? Visibility.Collapsed : Visibility.Visible;
            HistoryList.Visibility = hasEntries ? Visibility.Visible : Visibility.Collapsed;

            HistoryList.ItemsSource = _entries;
            StatusText.Text = _entries.Count == 1 ? "1 entry" : $"{_entries.Count} entries";

            if (hasEntries)
                HistoryList.SelectedIndex = 0;
            else
                ShowSelectedEntry(null);
        }
        catch (JsonException ex)
        {
            StatusText.Text = $"History file is malformed: {ex.Message}";
            ShowEmptyState();
        }
        catch (IOException ex)
        {
            StatusText.Text = $"Failed to load history file: {ex.Message}";
            ShowEmptyState();
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusText.Text = $"Access denied while reading history: {ex.Message}";
            ShowEmptyState();
        }
    }

    private void ShowEmptyState()
    {
        EmptyStateBorder.Visibility = Visibility.Visible;
        HistoryList.Visibility = Visibility.Collapsed;
        HistoryList.ItemsSource = null;
        ShowSelectedEntry(null);
    }

    private void ShowSelectedEntry(PayloadHistoryEntry? entry)
    {
        if (entry == null)
        {
            DetailsHost.Visibility = Visibility.Collapsed;
            NoSelectionText.Visibility = _entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
            ? $"{entry.SourceFileSizeBytes:N0} bytes  ({FormatBytes(entry.SourceFileSizeBytes)})"
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
    }

    // Copy handlers
    private void CopyUrl_Click(object sender, RoutedEventArgs e) => CopyText(PayloadUrlValue.Text);
    private void CopyHash_Click(object sender, RoutedEventArgs e) => CopyText(SourceHashValue.Text);
    private void CopyChecksum_Click(object sender, RoutedEventArgs e) => CopyText(PayloadChecksumValue.Text);
    private void CopyCommandLine_Click(object sender, RoutedEventArgs e) => CopyText(CommandLineTextBox.Text);
    private void CopyPayload_Click(object sender, RoutedEventArgs e) => CopyText(PayloadTextBox.Text);

    private static void CopyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == "—") return;
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
    }

    private static string TextOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string FormatCodecValue(int index, string name, string description)
    {
        var baseText = string.IsNullOrWhiteSpace(name) ? $"{index}" : $"{index} — {name}";
        return string.IsNullOrWhiteSpace(description) ? baseText : $"{baseText}  ({description})";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes:N0} B";
        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }
        return $"{value:N2} {units[unitIndex]}";
    }
}

