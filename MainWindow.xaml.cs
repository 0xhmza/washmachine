using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Washmachine.Controllers;
using Washmachine.Logging;
using Washmachine.Services;
using Washmachine.Views;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Washmachine;

public partial class MainWindow : FluentWindow, IMainFormView
{
    private readonly IAppLogger _logger;
    private readonly MainFormCoordinator _coordinator;
    private readonly IRequirementProvisioner _requirements;

    public MainWindow()
    {
        InitializeComponent();

        _logger = new RichTextBoxLogger(debugBox);

        var paths = new AppPaths();
        var clipboard = new ClipboardService();
        var interaction = new UserInteractionService();
        var snippetCatalog = new YamlCodeSnippetCatalogService(paths);
        var bin2ShellRunner = new Bin2ShellRunner(paths);
        var encodingCatalog = new ShellcodeEncodingCatalogService(bin2ShellRunner, paths);
        var toolLocator = new CompilerToolLocator(_logger);
        var compiler = new CompilerService(paths, bin2ShellRunner, snippetCatalog, toolLocator, _logger);

        _requirements = new RequirementProvisioner(paths, _logger);
        _coordinator = new MainFormCoordinator(
            _logger,
            paths,
            snippetCatalog,
            encodingCatalog,
            bin2ShellRunner,
            compiler,
            clipboard,
            interaction);

        _logger.Info("Initializing application...");
        Loaded += MainWindow_Loaded;
    }

    public Window RootWindow => this;
    public ComboBox EncoderCombo => bin2hexEncoder;
    public ComboBox EnvelopeCombo => bin2hexEnvelope;
    public ComboBox TemplateCombo => templateComboBox;
    public ComboBox GenericShellcodeCombo => genericShellcodeComboBox;
    public TextBox ShellcodeFileTextBox => shellcodeFile;
    public TextBox ShellcodeRawTextBox => shellcodeRAW;
    public TextBox ShellcodeUrlTextBox => shellcodeURL;
    public Button SubmitButton => submitButton;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _requirements.EnsureRequirementsAsync(this);
            await _coordinator.InitializeAsync(this);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to initialize application: {ex.Message}");
            MessageBox.Show(
                this,
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
}
