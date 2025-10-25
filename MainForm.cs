using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Washmachine.Controllers;
using Washmachine.Logging;
using Washmachine.Services;
using Washmachine.Views;

namespace Washmachine;

public partial class MainForm : Form, IMainFormView
{
    private readonly IAppLogger _logger;
    private readonly MainFormCoordinator _coordinator;
    private readonly IRequirementProvisioner _requirements;

    public MainForm()
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
            compiler,
            clipboard,
            interaction);

        _logger.Info("Initializing application...");
    }

    public Control RootControl => this;
    public ComboBox EncoderCombo => bin2hexEncoder;
    public ComboBox CompressorCombo => bin2hexCompressor;
    public ComboBox EnvelopeCombo => bin2hexEnvelope;
    public ComboBox TemplateCombo => templateComboBox;
    public FlowLayoutPanel SnippetPickerPanel => SnippetsPicker;
    public ComboBox GenericShellcodeCombo => genericShellcodeComboBox;
    public TextBox ShellcodeFileTextBox => shellcodeFile;
    public TextBox ShellcodeRawTextBox => shellcodeRAW;
    public TextBox ShellcodeUrlTextBox => shellcodeURL;
    public Button SubmitButton => submitButton;

    private async void MainForm_Load(object sender, EventArgs e)
    {
        try
        {
            await _requirements.EnsureRequirementsAsync(this).ConfigureAwait(true);
            await _coordinator.InitializeAsync(this).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to initialize application: {ex.Message}");
            MessageBox.Show(
                this,
                $"Failed to prepare the application's requirements.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Startup Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            submitButton.Enabled = false;
        }
    }

    private void button1_Click(object sender, EventArgs e)
    {
        _coordinator.SelectShellcodeFile(this);
    }

    private void button2_Click(object sender, EventArgs e)
    {
        _coordinator.PasteShellcodeFromClipboard(this, shellcodeRAW);
    }

    private void button3_Click(object sender, EventArgs e)
    {
        _coordinator.PasteShellcodeFromClipboard(this, shellcodeURL);
    }

    private void RAWShellcodeInfo_Click(object sender, EventArgs e)
    {
        _coordinator.ShowShellcodeTip(this);
    }

    private void guardRailsFormat_Click(object sender, EventArgs e)
    {
        _coordinator.ShowGuardRailInfo(this);
    }

    private async void submitButton_Click(object sender, EventArgs e)
    {
        await _coordinator.HandleSubmitAsync(this).ConfigureAwait(true);
    }

    private void templateComboBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        _coordinator.HandleTemplateChanged(this);
    }
}


