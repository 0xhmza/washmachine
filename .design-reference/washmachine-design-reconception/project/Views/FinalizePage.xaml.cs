using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Washmachine.Views;

public sealed partial class FinalizePage : Page
{
    public static FinalizePage? Instance { get; private set; }

    public FinalizePage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        InitializeComponent();
        Instance = this;
    }

    public bool IsFinalizationEnabled => EnableFinalizeToggle.IsOn;

    public bool IsCloneEnabled =>
        IsFinalizationEnabled &&
        !string.IsNullOrWhiteSpace(CloneSourcePath.Text);

    public string? CloneSourceExePath =>
        IsCloneEnabled ? CloneSourcePath.Text.Trim() : null;

    public bool CloneResources => CloneResourcesCheck.IsChecked == true;
    public bool CloneIcon => CloneIconCheck.IsChecked == true;
    public bool CloneMetadata => CloneMetadataCheck.IsChecked == true;

    public long NopPaddingBytes
    {
        get
        {
            if (!IsFinalizationEnabled || EnableNopPaddingToggle.IsOn != true)
                return 0;

            var raw = NopPaddingBytesBox.Value;
            if (double.IsNaN(raw) || double.IsInfinity(raw) || raw <= 0)
                return 0;

            double multiplier = NopPaddingUnitCombo.SelectedIndex switch
            {
                1 => 1024d,
                2 => 1024d * 1024d,
                3 => 1024d * 1024d * 1024d,
                _ => 1d,
            };

            var bytes = raw * multiplier;
            if (bytes >= long.MaxValue) return long.MaxValue;
            var rounded = Math.Round(bytes, MidpointRounding.AwayFromZero);
            return (long)rounded;
        }
    }

    public void ApplyRecipe(FinalizeRecipe recipe)
    {
        EnableFinalizeToggle.IsOn = recipe.Enabled;
        if (recipe.CloneSource != null) CloneSourcePath.Text = recipe.CloneSource;
        CloneResourcesCheck.IsChecked = recipe.CloneResources;
        CloneIconCheck.IsChecked = recipe.CloneIcon;
        CloneMetadataCheck.IsChecked = recipe.CloneMetadata;

        if (recipe.NopPaddingBytes > 0)
        {
            EnableNopPaddingToggle.IsOn = true;
            NopPaddingUnitCombo.SelectedIndex = 0; // bytes
            NopPaddingBytesBox.Value = recipe.NopPaddingBytes;
        }
        else
        {
            EnableNopPaddingToggle.IsOn = false;
        }
    }

    private void EnableFinalizeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        bool enabled = EnableFinalizeToggle.IsOn;
        FinalizeConfigPanel.Opacity = enabled ? 1.0 : 0.4;
        FinalizeConfigPanel.IsHitTestVisible = enabled;
    }

    private void EnableNopPaddingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        bool on = EnableNopPaddingToggle.IsOn;
        NopPaddingBytesBox.IsEnabled = on;
        NopPaddingUnitCombo.IsEnabled = on;
    }

    private void CloneSourcePath_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CloneSourcePath.Text))
            CloneSettingsStatusText.Text = "No source selected.";
    }

    private async void BrowseCloneSource_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".exe");

        var hwnd = WindowNative.GetWindowHandle(App.ActiveWindow!);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            CloneSourcePath.Text = file.Path;
            CloneSettingsStatusText.Text = $"Loaded: {file.Name}";
        }
    }

    private void ConfirmCloneSettings_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CloneSourcePath.Text))
        {
            CloneSettingsStatusText.Text = "Pick a source EXE before confirming.";
            return;
        }

        CloneSettingsStatusText.Text = "Import settings confirmed.";
    }

    private void GoToCompilePage_Click(object sender, RoutedEventArgs e)
    {
        if (App.ActiveWindow is MainWindow mainWindow)
        {
            var navView = mainWindow.Content as NavigationView;
            if (navView != null)
            {
                var item = navView.MenuItems.OfType<NavigationViewItem>()
                    .FirstOrDefault(i => i.Tag?.ToString() == "CompilePage");
                if (item != null)
                {
                    navView.SelectedItem = item;
                }
            }
        }
    }
}
