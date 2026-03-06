using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Washmachine.Controllers;
using Washmachine.Logging;
using Washmachine.Services;
using WinRT.Interop;

namespace Washmachine.Views;

public sealed partial class MainPage : Page, IMainFormView
{
    private readonly IAppLogger _logger;
    private readonly MainFormCoordinator _coordinator;
    private readonly IRequirementProvisioner _requirements;
    private readonly AppPaths _paths;
    private ShellcodeSource _currentSource = ShellcodeSource.None;

    public MainPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        InitializeComponent();

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

        _logger.Info("Initializing application...");
        templateCatalogPath.Text = $"Catalog: {_paths.SnippetCatalogFile}";
        SetShellcodeSource(ShellcodeSource.None, clearInputs: false);
        Loaded += MainPage_Loaded;
    }

    public XamlRoot ViewXamlRoot => XamlRoot;
    public nint WindowHandle => WindowNative.GetWindowHandle(App.ActiveWindow!);
    public DependencyObject ContentRoot => this;

    public ComboBox EncoderCombo => bin2hexEncoder;
    public ComboBox EnvelopeCombo => bin2hexEnvelope;
    public ComboBox TemplateCombo => templateComboBox;
    public ComboBox GenericShellcodeCombo => genericShellcodeComboBox;
    public TextBox ShellcodeFileTextBox => shellcodeFile;
    public TextBox ShellcodeRawTextBox => shellcodeRAW;
    public TextBox ShellcodeUrlTextBox => shellcodeURL;
    public Button SubmitButton => submitButton;

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _requirements.EnsureRequirementsAsync(this);
            await _coordinator.InitializeAsync(this);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to initialize application: {ex.Message}");
            var dialog = new ContentDialog
            {
                Title = "Startup Error",
                Content = $"Failed to prepare the application's requirements.\n\n{ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
            submitButton.IsEnabled = false;
        }
    }

    private async void button1_Click(object sender, RoutedEventArgs e) =>
        await _coordinator.SelectShellcodeFile(this);

    private async void button2_Click(object sender, RoutedEventArgs e) =>
        await _coordinator.PasteShellcodeFromClipboard(this, shellcodeRAW);

    private async void button3_Click(object sender, RoutedEventArgs e) =>
        await _coordinator.PasteShellcodeFromClipboard(this, shellcodeURL);

    private void RAWShellcodeInfo_Click(object sender, TappedRoutedEventArgs e)
    {
        _coordinator.ShowShellcodeTip(this);
        e.Handled = true;
    }

    private async void submitButton_Click(object sender, RoutedEventArgs e) =>
        await _coordinator.HandleSubmitAsync(this);

    private async void templateComboBox_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        await _coordinator.HandleTemplateChanged(this);
    }

    private async void button4_Click(object sender, RoutedEventArgs e) =>
        await _coordinator.OpenTemplateConfig(this);

    private async void WebPayloadGenerator_Click(object sender, RoutedEventArgs e) =>
        await _coordinator.GenerateWebPayloadAsync(this);

    private async void importTemplateFileButton_Click(object sender, RoutedEventArgs e) =>
        await _coordinator.ImportTemplateCatalogFromFileAsync(this);

    private async void importTemplateUrlButton_Click(object sender, RoutedEventArgs e) =>
        await _coordinator.ImportTemplateCatalogFromUrlAsync(this, templateCatalogUrl.Text);

    private void refreshTemplateButton_Click(object sender, RoutedEventArgs e) =>
        _coordinator.RefreshTemplateCatalog(this);

    private void openTemplateFolderButton_Click(object sender, RoutedEventArgs e) =>
        _coordinator.OpenTemplateCatalogLocation(this);

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
        if (s != ShellcodeSource.Url)     shellcodeURL.Text  = string.Empty;
        if (s != ShellcodeSource.Generic) genericShellcodeComboBox.SelectedIndex = -1;
    }

    private enum ShellcodeSource { None, File, Raw, Url, Generic }
}
