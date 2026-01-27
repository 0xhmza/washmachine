using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using Washmachine.Controllers;
using Washmachine.Logging;
using Washmachine.Services;
using Wpf.Ui.Animations;

namespace Washmachine.Views;

public partial class MainPage : Page, IMainFormView
{
    private readonly IAppLogger _logger;
    private readonly MainFormCoordinator _coordinator;
    private readonly IRequirementProvisioner _requirements;
    private readonly AppPaths _paths;
    private ShellcodeSource _currentSource = ShellcodeSource.None;

    public MainPage()
    {
        InitializeComponent();

        _logger = new RichTextBoxLogger(debugBox);

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

    public Window RootWindow
    {
        get
        {
            var window = Window.GetWindow(this);
            return window ?? throw new InvalidOperationException("Main window is not available yet.");
        }
    }

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
            TransitionAnimationProvider.ApplyTransition(Root, Transition.FadeInWithSlide, 240);
            await _requirements.EnsureRequirementsAsync(this);
            await _coordinator.InitializeAsync(this);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to initialize application: {ex.Message}");
            MessageBox.Show(
                RootWindow,
                $"Failed to prepare the application's requirements.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            submitButton.IsEnabled = false;
        }
    }

    private void button1_Click(object sender, RoutedEventArgs e)
    {
        _coordinator.SelectShellcodeFile(this);
    }

    private void button2_Click(object sender, RoutedEventArgs e)
    {
        _coordinator.PasteShellcodeFromClipboard(this, shellcodeRAW);
    }

    private void button3_Click(object sender, RoutedEventArgs e)
    {
        _coordinator.PasteShellcodeFromClipboard(this, shellcodeURL);
    }

    private void RAWShellcodeInfo_Click(object sender, MouseButtonEventArgs e)
    {
        _coordinator.ShowShellcodeTip(this);
        e.Handled = true;
    }

    private async void submitButton_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.HandleSubmitAsync(this);
    }

    private void templateComboBox_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        _coordinator.HandleTemplateChanged(this);
    }

    private void button4_Click(object sender, RoutedEventArgs e)
    {
        _coordinator.OpenTemplateConfig(this);
    }

    private async void importTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.ImportTemplateCatalogAsync(this);
    }

    private async void WebPayloadGenerator_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.GenerateWebPayloadAsync(this);
    }

    private async void importTemplateFileButton_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.ImportTemplateCatalogFromFileAsync(this);
    }

    private async void importTemplateUrlButton_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.ImportTemplateCatalogFromUrlAsync(this, templateCatalogUrl.Text);
    }

    private void refreshTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        _coordinator.RefreshTemplateCatalog(this);
    }

    private void openTemplateFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _coordinator.OpenTemplateCatalogLocation(this);
    }

    private void ShellcodeSourceSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase button || button.Tag is not string tag)
            return;

        switch (tag)
        {
            case "File":
                SetShellcodeSource(ShellcodeSource.File);
                break;
            case "Raw":
                SetShellcodeSource(ShellcodeSource.Raw);
                break;
            case "Url":
                SetShellcodeSource(ShellcodeSource.Url);
                break;
            case "Generic":
                SetShellcodeSource(ShellcodeSource.Generic);
                break;
        }
    }

    private void ChangeShellcodeSource_Click(object sender, RoutedEventArgs e)
    {
        SetShellcodeSource(ShellcodeSource.None, clearInputs: false);
    }

    private void SetShellcodeSource(ShellcodeSource source, bool clearInputs = true)
    {
        _currentSource = source;

        ShellcodeSourcePicker.Visibility = source == ShellcodeSource.None ? Visibility.Visible : Visibility.Collapsed;
        ShellcodeSourceDetails.Visibility = source == ShellcodeSource.None ? Visibility.Collapsed : Visibility.Visible;

        FileSourcePanel.Visibility = source == ShellcodeSource.File ? Visibility.Visible : Visibility.Collapsed;
        RawSourcePanel.Visibility = source == ShellcodeSource.Raw ? Visibility.Visible : Visibility.Collapsed;
        UrlSourcePanel.Visibility = source == ShellcodeSource.Url ? Visibility.Visible : Visibility.Collapsed;
        GenericSourcePanel.Visibility = source == ShellcodeSource.Generic ? Visibility.Visible : Visibility.Collapsed;

        if (source == ShellcodeSource.None)
        {
            ShellcodeSourceSummary.Text = "Selected source: None";
            ShellcodeSourceIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CodeBlock24;
            return;
        }

        if (clearInputs)
            ClearOtherSources(source);

        switch (source)
        {
            case ShellcodeSource.File:
                ShellcodeSourceSummary.Text = "Selected source: File";
                ShellcodeSourceIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Document24;
                break;
            case ShellcodeSource.Raw:
                ShellcodeSourceSummary.Text = "Selected source: Raw bytes";
                ShellcodeSourceIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CodeBlock24;
                break;
            case ShellcodeSource.Url:
                ShellcodeSourceSummary.Text = "Selected source: URL";
                ShellcodeSourceIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Link24;
                break;
            case ShellcodeSource.Generic:
                ShellcodeSourceSummary.Text = "Selected source: Generic";
                ShellcodeSourceIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Box24;
                break;
        }
    }

    private void ClearOtherSources(ShellcodeSource selectedSource)
    {
        if (selectedSource != ShellcodeSource.File)
            shellcodeFile.Text = string.Empty;

        if (selectedSource != ShellcodeSource.Raw)
            shellcodeRAW.Text = string.Empty;

        if (selectedSource != ShellcodeSource.Url)
            shellcodeURL.Text = string.Empty;

        if (selectedSource != ShellcodeSource.Generic)
            genericShellcodeComboBox.SelectedIndex = -1;
    }

    private enum ShellcodeSource
    {
        None,
        File,
        Raw,
        Url,
        Generic
    }
}
