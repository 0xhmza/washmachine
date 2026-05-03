using System.Text;
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
    private readonly AppPaths _paths;
    private readonly CliExecutor _cli = new();
    private readonly PeStripService _peStripper;
    private ShellcodeSource _currentSource = ShellcodeSource.None;
    private bool _suppressPlaybookEvents;
    private bool _initialized;

    public MainPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        InitializeComponent();
        Instance = this;

        _logger = new RichEditBoxLogger(statusLogBox);

        _paths = new AppPaths();
        _peStripper = new PeStripService(new ConsoleLogger()); // analysis-only, no UI logging needed
        var clipboard = new ClipboardService();
        var interaction = new UserInteractionService();
        var snippetCatalog = new YamlCodeSnippetCatalogService(_paths);
        var bin2ShellRunner = new Bin2ShellRunner(_paths);
        var encodingCatalog = new ShellcodeEncodingCatalogService(bin2ShellRunner, _paths);
        var toolLocator = new CompilerToolLocator(_logger);
        var compiler = new CompilerService(_paths, bin2ShellRunner, snippetCatalog, toolLocator, _logger);

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
        //templateCatalogPath.Text = $"Catalog: {_paths.ActivePlaybookPath}";
        PopulatePlaybookCombo();
        SetShellcodeSource(ShellcodeSource.None, clearInputs: false);
        shellcodeFileInput.TextChanged += async (s, e) => await UpdateShellcodeFileBadgeAsync(shellcodeFileInput.Text);
        Loaded += MainPage_Loaded;
    }

    public XamlRoot ViewXamlRoot => XamlRoot;
    public nint WindowHandle => WindowNative.GetWindowHandle(App.ActiveWindow!);
    public DependencyObject ContentRoot => this;

    /// <summary>Currently selected shellcode source type.</summary>
    public ShellcodeSource CurrentShellcodeSource => _currentSource;

    public ComboBox EncoderCombo => encoderCombo;
    public ComboBox EnvelopeCombo => envelopeCombo;
    public ComboBox TemplateCombo => templateCombo;
    public ComboBox PlaybookCombo => playbookComboBox;
    public ComboBox GenericShellcodeCombo => genericShellcodeCombo;
    public TextBlock EncoderDescriptionTextBlock => encoderDescriptionText;
    public TextBlock EnvelopeDescriptionTextBlock => envelopeDescriptionText;
    public TextBlock PlaybookPathTextBlock => playbookPathText;
    public TextBox ShellcodeFileTextBox => shellcodeFileInput;
    public TextBox ShellcodeRawTextBox => shellcodeRawInput;
    public TextBox ShellcodeUrlTextBox => shellcodeUrlValue;
    public TextBox ShellcodeUrlFileTextBox => shellcodeUrlFileInput;
    public Button SubmitButton => goToBackdooringButton;
    public MainFormCoordinator Coordinator => _coordinator;

    /// <summary>Selected template ID (e.g. "shellcode-minimal").</summary>
    public string? SelectedTemplateId => TemplateCombo.SelectedValue as string;

    /// <summary>Selected Bin2Shell encoder index, or null if nothing selected.</summary>
    public int? SelectedEncoderIndex => EncoderCombo.SelectedValue is int i ? i : (int?)null;

    /// <summary>Selected Bin2Shell envelope index, or null if nothing selected.</summary>
    public int? SelectedEnvelopeIndex => EnvelopeCombo.SelectedValue is int i ? i : (int?)null;

    public bool IsShikataGaNaiEnabled => shikataGaNaiEnabledCheckBox.IsChecked == true;

    public int ShikataGaNaiEncodeCount => GetPositiveNumberBoxValue(shikataGaNaiEncodeCountInput, 1);

    public int ShikataGaNaiMaxBytes => GetPositiveNumberBoxValue(shikataGaNaiMaxBytesInput, 50);

    public string ShikataGaNaiPlacement => shikataPlacementPost?.IsChecked == true ? "post" : "pre";

    public bool IsShikataGaNaiPostPlacement => ShikataGaNaiPlacement == "post";

    public void SetPayloadEncodingEnabled(bool enabled)
    {
        PayloadEncodingExpander.IsEnabled = enabled;
        PayloadEncodingExpander.Opacity = enabled ? 1.0 : 0.4;
    }

    private static int GetPositiveNumberBoxValue(NumberBox numberBox, int fallback)
    {
        if (numberBox == null)
            return fallback;

        var raw = numberBox.Value;
        if (double.IsNaN(raw) || double.IsInfinity(raw))
            return fallback;

        var rounded = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        return rounded > 0 ? rounded : fallback;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true; // Set immediately — never retry, even on error

        if (!_cli.IsAvailable)
        {
            _logger.Error($"CLI not found: {_cli.CliPath}");
            _logger.Warn("Build the Washmachine.Cli project to enable compilation features.");
            goToBackdooringButton.IsEnabled = false;
        }

        try
        {
            await RunProvisionAsync();
            PopulatePlaybookCombo();
            await _coordinator.InitializeAsync(this);
            _logger.Ok("Ready.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Startup failed: {ex.Message}");
            var dialog = new ContentDialog
            {
                Title          = "Startup Error",
                Content        = $"Failed to prepare requirements.\n\n{ex.Message}",
                CloseButtonText = "OK",
                XamlRoot       = XamlRoot
            };
            await dialog.ShowAsync();
            goToBackdooringButton.IsEnabled = false;
        }
    }

    private async Task RunProvisionAsync()
    {
        if (!_cli.IsAvailable)
        {
            _logger.Warn("Skipping provision: CLI not available.");
            return;
        }

        _logger.Info("Checking requirements via CLI...");
        var result = await _cli.RunAsync(
            ["provision", "--core-only"],
            line => _logger.Info(line));

        if (!result.Success)
            throw new InvalidOperationException($"Provision failed (exit {result.ExitCode}).\n{result.Output}");
    }

    private async void BrowseShellcodeFile_Click(object sender, RoutedEventArgs e)
    {
        _coordinator.LogUiAction("Browse shellcode file");
        await _coordinator.SelectShellcodeFile(this);
    }

    private async void PasteShellcode_Click(object sender, RoutedEventArgs e)
    {
        _coordinator.LogUiAction("Paste shellcode from clipboard");
        await _coordinator.PasteShellcodeFromClipboard(this, shellcodeRawInput);
    }

    private async void urlBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        _coordinator.LogUiAction("Browse shellcode URL file");
        await _coordinator.SelectShellcodeFileForUrl(this);

        // Re-enable wizard if user picked a new file
        startWizardButton.IsEnabled = true;
        wizardStatusText.Text = string.Empty;
    }

    private async void StartWizard_Click(object sender, RoutedEventArgs e)
    {
        _coordinator.LogUiAction("Start web payload wizard");
        await _coordinator.GenerateWebPayloadAsync(this);

        // After wizard completes, grey out the button and show guidance
        if (!string.IsNullOrEmpty(shellcodeUrlValue.Text))
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

    private async void Template_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        await _coordinator.HandleTemplateChanged(this);
    }

    private void Encoder_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _coordinator.UpdateEncodingDescriptions(this);

    private void Envelope_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _coordinator.UpdateEncodingDescriptions(this);

    private void ShikataGaNaiEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (shikataGaNaiEncodeCountInput is null || shikataGaNaiMaxBytesInput is null)
            return;

        bool enabled = IsShikataGaNaiEnabled;
        shikataGaNaiEncodeCountInput.IsEnabled = enabled;
        shikataGaNaiMaxBytesInput.IsEnabled = enabled;

        if (enabled)
        {
            shikataGaNaiEncodeCountInput.Value = ShikataGaNaiEncodeCount;
            shikataGaNaiMaxBytesInput.Value = ShikataGaNaiMaxBytes;
        }
    }

    private void ShikataGaNaiPlacementChanged(object sender, RoutedEventArgs e)
    {
        // Pipeline page re-renders on navigation; no push notification needed here.
    }

    public void ApplySgnRecipe(bool enabled, int encodeCount, int maxBytes, string placement)
    {
        shikataGaNaiEnabledCheckBox.IsChecked = enabled;
        if (shikataGaNaiEncodeCountInput != null)
            shikataGaNaiEncodeCountInput.Value = encodeCount > 0 ? encodeCount : 1;
        if (shikataGaNaiMaxBytesInput != null)
            shikataGaNaiMaxBytesInput.Value = maxBytes > 0 ? maxBytes : 50;

        bool post = string.Equals(placement, "post", StringComparison.OrdinalIgnoreCase);
        if (shikataPlacementPost != null) shikataPlacementPost.IsChecked = post;
        if (shikataPlacementPre != null)  shikataPlacementPre.IsChecked  = !post;
    }

    public void ApplyTemplateAndEncoding(string? templateId, int? encoderIndex, int? envelopeIndex)
    {
        if (!string.IsNullOrEmpty(templateId))
        {
            foreach (var item in TemplateCombo.Items)
            {
                if (item is string s && s == templateId) { TemplateCombo.SelectedItem = item; break; }
                // Items may be objects with a Value property; fall back to ToString compare
                if (item?.ToString() == templateId) { TemplateCombo.SelectedItem = item; break; }
            }
        }

        if (encoderIndex is int ei)
        {
            foreach (var item in EncoderCombo.Items)
            {
                if (item is int i && i == ei) { EncoderCombo.SelectedItem = item; break; }
            }
        }

        if (envelopeIndex is int vi)
        {
            foreach (var item in EnvelopeCombo.Items)
            {
                if (item is int i && i == vi) { EnvelopeCombo.SelectedItem = item; break; }
            }
        }
    }

    private async void ConfigureTemplate_Click(object sender, RoutedEventArgs e)
    {
        _coordinator.LogUiAction("Open template config dialog");
        await _coordinator.OpenTemplateConfig(this);
    }

    private void refreshTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        _coordinator.LogUiAction("Refresh template catalog");
        _coordinator.RefreshTemplateCatalog(this);
    }

    private async void Playbook_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPlaybookEvents || !IsLoaded)
            return;

        if (playbookComboBox.SelectedItem is not PlaybookComboItem selected)
            return;

        if (!_paths.SetActivePlaybook(selected.Path))
        {
            _logger.Warn($"Failed to activate playbook: {selected.Path}");
            return;
        }

        playbookPathText.Text = selected.Path;
        //templateCatalogPath.Text = $"Catalog: {_paths.ActivePlaybookPath}";
        _coordinator.RefreshTemplateCatalog(this);
        await _coordinator.ReloadEncodingCatalogAsync(this);
    }

    private void GoToBackdooringPage_Click(object sender, RoutedEventArgs e)
    {
        if (App.ActiveWindow is MainWindow mainWindow)
            mainWindow.NavigateToBackdooringPage();
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
        if (s != ShellcodeSource.File)    shellcodeFileInput.Text = string.Empty;
        if (s != ShellcodeSource.Raw)     shellcodeRawInput.Text  = string.Empty;
        if (s != ShellcodeSource.Url)   { shellcodeUrlValue.Text  = string.Empty; shellcodeUrlFileInput.Text = string.Empty; }
        if (s != ShellcodeSource.Generic) genericShellcodeCombo.SelectedIndex = -1;
    }

    private void PopulatePlaybookCombo()
    {
        var playbooks = _paths.GetAvailablePlaybookFiles();
        playbookComboBox.Items.Clear();

        _suppressPlaybookEvents = true;
        try
        {
            foreach (var path in playbooks)
                playbookComboBox.Items.Add(new PlaybookComboItem(path));

            playbookComboBox.DisplayMemberPath = nameof(PlaybookComboItem.Name);
            playbookComboBox.SelectedValuePath = nameof(PlaybookComboItem.Path);
            playbookComboBox.IsEnabled = playbookComboBox.Items.Count > 0;

            string active = _paths.ActivePlaybookFullPath;
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
                playbookPathText.Text = _paths.ActivePlaybookPath;
            else
                playbookPathText.Text = "No playbooks found in Assets.";
        }
        finally
        {
            _suppressPlaybookEvents = false;
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

    private bool _isManaged;

    private async Task UpdateShellcodeFileBadgeAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            FileByteCountBadge.Visibility = Visibility.Collapsed;
            FileMissingBadge.Visibility   = Visibility.Collapsed;
            PeStripOptionsPanel.Visibility = Visibility.Collapsed;
            DonutOptionsPanel.Visibility   = Visibility.Collapsed;
            _isManaged = false;
            return;
        }
        if (System.IO.File.Exists(path))
        {
            var bytes = new System.IO.FileInfo(path).Length;
            FileByteCountText.Text        = $"{bytes:N0} bytes";
            FileByteCountBadge.Visibility = Visibility.Visible;
            FileMissingBadge.Visibility   = Visibility.Collapsed;
        }
        else
        {
            FileByteCountBadge.Visibility = Visibility.Collapsed;
            FileMissingBadge.Visibility   = Visibility.Visible;
        }

        bool isExe = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        if (!isExe)
        {
            PeStripOptionsPanel.Visibility = Visibility.Collapsed;
            DonutOptionsPanel.Visibility   = Visibility.Collapsed;
            _isManaged = false;
            return;
        }

        // Detect managed (.NET) PE asynchronously
        _isManaged = false;
        if (System.IO.File.Exists(path))
        {
            try
            {
                var data = await System.IO.File.ReadAllBytesAsync(path);
                _isManaged = PeStripService.IsManagedPe(data);
            }
            catch { }
        }

        if (_isManaged)
        {
            PeStripOptionsPanel.Visibility = Visibility.Collapsed;
            DonutOptionsPanel.Visibility   = Visibility.Visible;
        }
        else
        {
            PeStripOptionsPanel.Visibility = Visibility.Visible;
            DonutOptionsPanel.Visibility   = Visibility.Collapsed;
            UpdatePeStripModeControls();
        }
    }

    private void UpdateShellcodeFileBadge(string? path)
    {
        _ = UpdateShellcodeFileBadgeAsync(path);
    }

    // ── PE / Donut source options — consumed by CompilePage ──────────────────

    public PeSourceOptions GetPeSourceOptions() => new()
    {
        IsDonutConversion      = _isManaged,
        DonutArch              = donutArchCombo?.SelectedValue is string tag && int.TryParse(tag, out var arch) ? arch : 2,
        DonutClass             = string.IsNullOrWhiteSpace(donutClassInput?.Text)   ? null : donutClassInput.Text.Trim(),
        DonutMethod            = string.IsNullOrWhiteSpace(donutMethodInput?.Text)  ? null : donutMethodInput.Text.Trim(),
        DonutParams            = string.IsNullOrWhiteSpace(donutParamsInput?.Text)  ? null : donutParamsInput.Text.Trim(),
        PeStripMode            = (peStripModeCombo?.SelectedValue as string) ?? "ep",
        PeStripSection         = peStripSectionInput?.Text?.Trim() ?? string.Empty,
        PeStripTrimTrailingZeros = peStripTrimCheck?.IsChecked != false,
    };

    private void PeStripMode_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdatePeStripModeControls();

    private void UpdatePeStripModeControls()
    {
        var mode = (peStripModeCombo?.SelectedValue as string) ?? "ep";
        if (peStripSectionRow != null)
            peStripSectionRow.Height = mode == "section" ? GridLength.Auto : new GridLength(0);
    }

    private async void AnalyzePe_Click(object sender, RoutedEventArgs e)
    {
        var path = shellcodeFileInput.Text.Trim();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _logger.Warn("No valid .exe file selected for PE analysis.");
            return;
        }

        var analysis = await _peStripper.AnalyzeAsync(path);

        if (!analysis.Success)
        {
            await new ContentDialog
            {
                Title = "PE Analysis Failed",
                Content = analysis.Error ?? "Unknown error.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Close,
            }.ShowAsync();
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Architecture : {(analysis.Is64Bit ? "x64" : "x86")}");
        sb.AppendLine($"Managed .NET : {(analysis.IsManaged ? "YES — use donut conversion" : "no")}");
        sb.AppendLine($"Entry Point  : 0x{analysis.EntryPoint:X8}");
        sb.AppendLine($"EP Section   : {analysis.EntryPointSection ?? "unknown"}");
        sb.AppendLine($"Est. bin size: {analysis.EstimatedBinSize:N0} bytes  (ep mode, after trim)");
        sb.AppendLine();
        sb.AppendLine($"{"Section",-12} {"RawAddr",10} {"RawSize",12} {"Exec",6} {"EP",4}");
        sb.AppendLine($"{"-------",-12} {"-------",10} {"-------",12} {"----",6} {"--",4}");
        foreach (var s in analysis.Sections)
        {
            sb.AppendLine($"{s.Name,-12} 0x{s.RawAddress:X6}  {s.RawSize,10:N0} {(s.IsExecutable ? "yes" : ""),6} {(s.ContainsEntryPoint ? "◄ EP" : ""),4}");
        }

        if (analysis.IsManaged)
        {
            sb.AppendLine();
            sb.AppendLine("ℹ  This is a managed .NET assembly.");
            sb.AppendLine("   Standard PE strip will NOT produce working shellcode.");
            sb.AppendLine("   Washmachine will automatically use donut to convert it.");
        }
        else if (analysis.EstimatedBinSize < 64)
        {
            sb.AppendLine();
            sb.AppendLine("⚠  Extracted size is very small — this PE is likely not shellcode-compatible.");
            sb.AppendLine("   Use raw .bin shellcode (msfvenom -f raw) instead.");
        }

        var contentBlock = new TextBlock
        {
            Text = sb.ToString(),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
        };

        var scrollViewer = new ScrollViewer
        {
            Content = contentBlock,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 420,
            MinWidth = 460,
        };

        await new ContentDialog
        {
            Title = $"PE Analysis — {Path.GetFileName(path)}",
            Content = scrollViewer,
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close,
        }.ShowAsync();
    }

}

public enum ShellcodeSource { None, File, Raw, Url, Generic }
