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
        var headerLists = new HeaderListProvider(snippetCatalog);
        var bin2ShellRunner = new Bin2ShellRunner(paths);
        var encodingCatalog = new ShellcodeEncodingCatalogService(bin2ShellRunner, paths);
        var toolchains = new MsvcToolchainLocator(_logger, interaction, paths);
        var compiler = new CompilerService(paths, bin2ShellRunner, snippetCatalog, _logger);

        _requirements = new RequirementProvisioner(paths, _logger);
        _coordinator = new MainFormCoordinator(
            _logger,
            paths,
            headerLists,
            encodingCatalog,
            toolchains,
            compiler,
            clipboard,
            interaction);

        _logger.Info("Initializing application...");
    }

    public Control RootControl => this;
    public ComboBox EncoderCombo => bin2hexEncoder;
    public ComboBox CompressorCombo => bin2hexCompressor;
    public ComboBox EnvelopeCombo => bin2hexEnvelope;
    public ComboBox ProcessInjectionCombo => psInjComboBox;
    public ComboBox ShellcodeExecutionCombo => shellcodeExecutionComboBox;
    public ComboBox UacBypassCombo => UACBComboBox;
    public ComboBox GenericShellcodeCombo => genericShellcodeComboBox;
    public ComboBox GuardrailCombo => guardrailComboBox;
    public ListBox AntiDebugList => antiDebugListBox;
    public TextBox ShellcodeFileTextBox => shellcodeFile;
    public TextBox ShellcodeRawTextBox => shellcodeRAW;
    public TextBox ShellcodeUrlTextBox => shellcodeURL;
    public TextBox ProcessInjectionTargetTextBox => PsInjPsNameTextBox;
    public TextBox GuardrailParameterTextBox => guardrailParamTextBox;
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
}




