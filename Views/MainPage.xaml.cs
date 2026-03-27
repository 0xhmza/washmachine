using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Washmachine.Controllers;
using Washmachine.Logging;
using Washmachine.Services;
using WinRT.Interop;

namespace Washmachine.Views;

public sealed partial class MainPage : Page, IMainFormView
{
    public static MainPage? Instance { get; private set; }

    private readonly IAppLogger _logger;
    private readonly MainFormCoordinator _coordinator;
    private readonly IRequirementProvisioner _requirements;
    private readonly AppPaths _paths;
    private ShellcodeSource _currentSource = ShellcodeSource.None;
    private bool _suppressPlaybookSelection;

    public MainPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        InitializeComponent();
        Instance = this;

        _logger = new RichEditBoxLogger(debugBox);

        _paths = new AppPaths();
        var clipboard = new ClipboardService();
        var interaction = new UserInteractionService();
        var snippetCatalog = new YamlCodeSnippetCatalogService(_paths);
        var bin2ShellRunner = new Bin2ShellRunner(_paths);
        var encodingCatalog = new ShellcodeEncodingCatalogService(bin2ShellRunner, _paths);
        var toolLocator = new CompilerToolLocator(_logger);
        var compiler = new CompilerService(_paths, bin2ShellRunner, snippetCatalog, toolLocator, _logger);

        _requirements = new RequirementProvisioner(_paths, _logger);
        _coordinator = new MainFormCoordinator(
            _logger,
            _paths,
            snippetCatalog,
            encodingCatalog,
            bin2ShellRunner,
            compiler,
            clipboard,
            interaction);

        _logger.Debug("Initializing...");
        templateCatalogPath.Text = $"Catalog: {_paths.ActivePlaybookPath}";
        PopulatePlaybookCombo();
        SetShellcodeSource(ShellcodeSource.None, clearInputs: false);
        Loaded += MainPage_Loaded;
    }

    public XamlRoot ViewXamlRoot => XamlRoot;
    public nint WindowHandle => WindowNative.GetWindowHandle(App.ActiveWindow!);
    public DependencyObject ContentRoot => this;

    public ComboBox EncoderCombo => bin2hexEncoder;
    public ComboBox EnvelopeCombo => bin2hexEnvelope;
    public ComboBox TemplateCombo => templateComboBox;
    public ComboBox PlaybookCombo => playbookComboBox;
    public ComboBox GenericShellcodeCombo => genericShellcodeComboBox;
    public TextBlock EncoderDescriptionTextBlock => encoderDescriptionText;
    public TextBlock EnvelopeDescriptionTextBlock => envelopeDescriptionText;
    public TextBlock PlaybookPathTextBlock => playbookPathText;
    public TextBox ShellcodeFileTextBox => shellcodeFile;
    public TextBox ShellcodeRawTextBox => shellcodeRAW;
    public TextBox ShellcodeUrlTextBox => shellcodeURL;
    public TextBox ShellcodeUrlFileTextBox => shellcodeURLFile;
    public Button SubmitButton => goToBackdooringButton;
    public MainFormCoordinator Coordinator => _coordinator;

    public void SetPayloadEncodingEnabled(bool enabled)
    {
        PayloadEncodingExpander.IsEnabled = enabled;
        PayloadEncodingExpander.Opacity = enabled ? 1.0 : 0.4;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _requirements.EnsureRequirementsAsync(new WindowProgressReporter());
            PopulatePlaybookCombo();
            await _coordinator.InitializeAsync(this);
            _logger.Ok("Ready.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Startup failed: {ex.Message}");
            var dialog = new ContentDialog
            {
                Title = "Startup Error",
                Content = $"Failed to prepare the application's requirements.\n\n{ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
            goToBackdooringButton.IsEnabled = false;
        }
    }

    private async void button1_Click(object sender, RoutedEventArgs e) =>
        await _coordinator.SelectShellcodeFile(this);

    private async void button2_Click(object sender, RoutedEventArgs e) =>
        await _coordinator.PasteShellcodeFromClipboard(this, shellcodeRAW);

    private async void urlBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.SelectShellcodeFileForUrl(this);

        // Re-enable wizard if user picked a new file
        startWizardButton.IsEnabled = true;
        wizardStatusText.Text = string.Empty;
    }

    private async void StartWizard_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.GenerateWebPayloadAsync(this);

        // After wizard completes, grey out the button and show guidance
        if (!string.IsNullOrEmpty(shellcodeURL.Text))
        {
            startWizardButton.IsEnabled = false;
            wizardStatusText.Text = "Now adjust the template and compile, or choose another source/file.";
        }
    }

    private void RAWShellcodeInfo_Click(object sender, TappedRoutedEventArgs e)
    {
        _coordinator.ShowShellcodeTip(this);
        e.Handled = true;
    }

    private async void templateComboBox_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        await _coordinator.HandleTemplateChanged(this);
    }

    private void bin2hexEncoder_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _coordinator.UpdateEncodingDescriptions(this);

    private void bin2hexEnvelope_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _coordinator.UpdateEncodingDescriptions(this);

    private async void button4_Click(object sender, RoutedEventArgs e) =>
        await _coordinator.OpenTemplateConfig(this);

    private void refreshTemplateButton_Click(object sender, RoutedEventArgs e) =>
        _coordinator.RefreshTemplateCatalog(this);

    private async void playbookComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPlaybookSelection || !IsLoaded)
            return;

        if (playbookComboBox.SelectedItem is not PlaybookComboItem selected)
            return;

        if (!_paths.SetActivePlaybook(selected.Path))
        {
            _logger.Warn($"Failed to activate playbook: {selected.Path}");
            return;
        }

        playbookPathText.Text = selected.Path;
        templateCatalogPath.Text = $"Catalog: {_paths.ActivePlaybookPath}";
        _coordinator.RefreshTemplateCatalog(this);
        await _coordinator.ReloadEncodingCatalogAsync(this);
    }

    private void GoToBackdooringPage_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to BackdooringPage
        if (App.ActiveWindow is MainWindow mainWindow)
        {
            var navView = mainWindow.Content as NavigationView;
            if (navView != null)
            {
                var item = navView.MenuItems.OfType<NavigationViewItem>()
                    .FirstOrDefault(i => i.Tag?.ToString() == "BackdooringPage");
                if (item != null)
                {
                    navView.SelectedItem = item;
                }
            }
        }
    }

    private void ShellcodeSourceSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase button || button.Tag is not string tag) return;
        switch (tag)
        {
            case "File":    SetShellcodeSource(ShellcodeSource.File);    break;
            case "Raw":     SetShellcodeSource(ShellcodeSource.Raw);     break;
            case "Url":     SetShellcodeSource(ShellcodeSource.Url);     break;
            case "Generic": SetShellcodeSource(ShellcodeSource.Generic); break;
        }
    }

    private void ChangeShellcodeSource_Click(object sender, RoutedEventArgs e) =>
        SetShellcodeSource(ShellcodeSource.None, clearInputs: false);

    private void SetShellcodeSource(ShellcodeSource source, bool clearInputs = true)
    {
        _currentSource = source;

        ShellcodeSourcePicker.Visibility  = source == ShellcodeSource.None ? Visibility.Visible : Visibility.Collapsed;
        ShellcodeSourceDetails.Visibility = source == ShellcodeSource.None ? Visibility.Collapsed : Visibility.Visible;

        FileSourcePanel.Visibility    = source == ShellcodeSource.File    ? Visibility.Visible : Visibility.Collapsed;
        RawSourcePanel.Visibility     = source == ShellcodeSource.Raw     ? Visibility.Visible : Visibility.Collapsed;
        UrlSourcePanel.Visibility     = source == ShellcodeSource.Url     ? Visibility.Visible : Visibility.Collapsed;
        GenericSourcePanel.Visibility = source == ShellcodeSource.Generic ? Visibility.Visible : Visibility.Collapsed;

        // Disable Payload Encoding when URL source is selected (wizard handles encoding)
        SetPayloadEncodingEnabled(source != ShellcodeSource.Url);

        // Re-enable wizard button when switching sources
        startWizardButton.IsEnabled = true;
        wizardStatusText.Text = string.Empty;

        if (source == ShellcodeSource.None)
        {
            ShellcodeSourceSummary.Text = "Selected source: None";
            ShellcodeSourceIcon.Glyph  = "\uE943";
            return;
        }

        if (clearInputs) ClearOtherSources(source);

        switch (source)
        {
            case ShellcodeSource.File:
                ShellcodeSourceSummary.Text = "Selected source: File";
                ShellcodeSourceIcon.Glyph   = "\uE8A5";
                break;
            case ShellcodeSource.Raw:
                ShellcodeSourceSummary.Text = "Selected source: Raw bytes";
                ShellcodeSourceIcon.Glyph   = "\uE943";
                break;
            case ShellcodeSource.Url:
                ShellcodeSourceSummary.Text = "Selected source: URL";
                ShellcodeSourceIcon.Glyph   = "\uE71B";
                break;
            case ShellcodeSource.Generic:
                ShellcodeSourceSummary.Text = "Selected source: Generic";
                ShellcodeSourceIcon.Glyph   = "\uE7B8";
                break;
        }
    }

    private void ClearOtherSources(ShellcodeSource s)
    {
        if (s != ShellcodeSource.File)    shellcodeFile.Text = string.Empty;
        if (s != ShellcodeSource.Raw)     shellcodeRAW.Text  = string.Empty;
        if (s != ShellcodeSource.Url)   { shellcodeURL.Text  = string.Empty; shellcodeURLFile.Text = string.Empty; }
        if (s != ShellcodeSource.Generic) genericShellcodeComboBox.SelectedIndex = -1;
    }

    private void PopulatePlaybookCombo()
    {
        var playbooks = _paths.GetAvailablePlaybookFiles();
        playbookComboBox.Items.Clear();

        _suppressPlaybookSelection = true;
        try
        {
            foreach (var path in playbooks)
                playbookComboBox.Items.Add(new PlaybookComboItem(path));

            playbookComboBox.DisplayMemberPath = nameof(PlaybookComboItem.Name);
            playbookComboBox.SelectedValuePath = nameof(PlaybookComboItem.Path);
            playbookComboBox.IsEnabled = playbookComboBox.Items.Count > 0;

            string active = _paths.ActivePlaybookPath;
            var selectedIndex = playbookComboBox.Items
                .OfType<PlaybookComboItem>()
                .Select((item, index) => new { item, index })
                .Where(entry => string.Equals(entry.item.Path, active, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.index)
                .DefaultIfEmpty(-1)
                .First();

            if (selectedIndex >= 0)
                playbookComboBox.SelectedIndex = selectedIndex;
            else if (playbookComboBox.Items.Count > 0)
                playbookComboBox.SelectedIndex = 0;

            if (playbookComboBox.Items.Count > 0)
                playbookPathText.Text = active;
            else
                playbookPathText.Text = "No playbooks found in Assets.";
        }
        finally
        {
            _suppressPlaybookSelection = false;
        }
    }

    private sealed class PlaybookComboItem
    {
        public PlaybookComboItem(string path)
        {
            Path = path ?? string.Empty;
            Name = System.IO.Path.GetFileNameWithoutExtension(Path);
        }

        public string Name { get; }
        public string Path { get; }
    }

    private enum ShellcodeSource { None, File, Raw, Url, Generic }
}
