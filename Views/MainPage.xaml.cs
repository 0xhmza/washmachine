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
    private readonly IAppLogger _logger;
    private readonly MainFormCoordinator _coordinator;
    private readonly IRequirementProvisioner _requirements;
    private readonly AppPaths _paths;
    private ShellcodeSource _currentSource = ShellcodeSource.None;
    private DateTime _compileStartedAt;

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

        _logger.Debug("Initializing...");
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
    public TextBox ShellcodeUrlFileTextBox => shellcodeURLFile;
    public Button SubmitButton => submitButton;
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
            await _requirements.EnsureRequirementsAsync(this);
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
            submitButton.IsEnabled = false;
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

    private async void submitButton_Click(object sender, RoutedEventArgs e)
    {
        compileStatusPanel.Visibility = Visibility.Visible;
        compileStatusIcon.Glyph = "\uE895"; // sync icon
        compileStatusIcon.Foreground = null;
        compileStatusText.Text = "Compiling...";
        compileOutputLink.Visibility = Visibility.Collapsed;
        _compileStartedAt = DateTime.UtcNow;

        await _coordinator.HandleSubmitAsync(this);

        UpdateCompileStatus();
    }

    private void UpdateCompileStatus()
    {
        try
        {
            var outputDir = System.IO.Path.Combine(
                _paths.EnsureTempSourceDirectory(), "Compiled BInaries");

            if (System.IO.Directory.Exists(outputDir))
            {
                var latest = System.IO.Directory.GetFiles(outputDir, "*.exe")
                    .Select(f => new System.IO.FileInfo(f))
                    .Where(fi => fi.LastWriteTimeUtc >= _compileStartedAt.AddSeconds(-2))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latest != null)
                {
                    compileStatusIcon.Glyph = "\uE73E"; // checkmark
                    compileStatusIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
                    compileStatusText.Text = "Compiled: ";
                    compileOutputLinkText.Text = latest.Name;
                    compileOutputLink.Tag = latest.DirectoryName;
                    compileOutputLink.Visibility = Visibility.Visible;
                    return;
                }
            }
        }
        catch
        {
            // If file-system access fails, fall through to the failure state.
        }

        compileStatusIcon.Glyph = "\uEA39"; // warning
        compileStatusIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange);
        compileStatusText.Text = "Compilation failed. Check logs for details.";
        compileOutputLink.Visibility = Visibility.Collapsed;
    }

    private void CompileOutputLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton btn && btn.Tag is string folder &&
            System.IO.Directory.Exists(folder))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
    }

    private async void templateComboBox_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        await _coordinator.HandleTemplateChanged(this);
    }

    private async void button4_Click(object sender, RoutedEventArgs e) =>
        await _coordinator.OpenTemplateConfig(this);

    private void refreshTemplateButton_Click(object sender, RoutedEventArgs e) =>
        _coordinator.RefreshTemplateCatalog(this);

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

        // Reset compile status
        compileStatusPanel.Visibility = Visibility.Collapsed;

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

    private enum ShellcodeSource { None, File, Raw, Url, Generic }
}
