using System;
using System.Drawing;
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
    private readonly FlowLayoutPanel SnippetsPicker;

    public MainForm()
    {
        InitializeComponent();

        SnippetsPicker = new FlowLayoutPanel
        {
            Name = "SnippetsPicker",
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(groupBox1.Left, groupBox1.Bottom + 6),
            Size = new Size(groupBox1.Width, 140),
            Margin = new Padding(3, 4, 3, 4),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Visible = false,
            TabStop = false
        };
        MainTab.Controls.Add(SnippetsPicker);

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
    }

    public Control RootControl => this;
    public ComboBox EncoderCombo => bin2hexEncoder;
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

    private void button4_Click(object sender, EventArgs e)
    {
        _coordinator.OpenTemplateConfig(this);
    }

    private async void WebPayloadGenerator_Click(object sender, EventArgs e)
    {
        await _coordinator.GenerateWebPayloadAsync(this).ConfigureAwait(true);
    }
}
