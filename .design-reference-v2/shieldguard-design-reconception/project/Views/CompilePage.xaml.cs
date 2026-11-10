using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShieldGuard.Controllers;
using ShieldGuard.Logging;
using ShieldGuard.Models;
using ShieldGuard.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ShieldGuard.Views;

public sealed partial class CompilePage : Page
{
    public static CompilePage? Instance { get; private set; }

    // Read-only accessors used by PipelinePage to build the live recipe view.
    public bool   IsStripToBinChecked      => StripToBinCheck?.IsChecked == true;
    public string? OutputDirectory          => OutputPath?.Text;
    public bool   GenerateDebugInfoEnabled => GenerateDebugInfo?.IsChecked == true;
    public bool   VerboseBuildEnabled      => VerboseBuildCheck?.IsChecked == true;
    public string? SelectedCompilerDisplay  => CompilerCombo?.SelectedItem?.ToString();

    private readonly IAppLogger _logger;
    private readonly AppPaths _paths;
    private readonly ICompilerService _compiler;
    private readonly ICompilerToolLocator _toolLocator;
    private readonly PeLicenseCaveService _licensecaveService;
    private readonly ICodeSnippetCatalogService _snippets;
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
        _licensecaveService = new PeLicenseCaveService(_paths, _logger);

        var bin2ShellRunner = new Bin2ShellRunner(_paths);
        _snippets = new YamlCodeSnippetCatalogService(_paths);
        _compiler = new CompilerService(_paths, bin2ShellRunner, _snippets, _toolLocator, _logger);

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
        CompilerCombo.Items.Clear();
        CompilerStatus.Text = "Detecting compilers...";
        DownloadCompilerPanel.Visibility = Visibility.Collapsed;

        try
        {
            var result = await _toolLocator.DiscoverAsync();
            _compilerCandidates = result.Candidates?.ToList();
            
            if (result.Candidates == null || result.Candidates.Count == 0)
            {
                CompilerCombo.Items.Add("No compiler found");
                CompilerCombo.SelectedIndex = 0;
                CompilerCombo.IsEnabled = false;
                CompileButton.IsEnabled = false;
                CompilerStatus.Text = "No compiler detected. Install Visual Studio or MinGW.";
                CompilerStatus.Foreground = App.ThemeBrush("DraculaOrangeBrush");
                DownloadCompilerPanel.Visibility = Visibility.Visible;
            }
            else
            {
                foreach (var compiler in result.Candidates)
                {
                    var displayName = $"{compiler.Kind} ({compiler.Path})";
                    CompilerCombo.Items.Add(displayName);
                }
                CompilerCombo.SelectedIndex = 0;
                CompilerCombo.IsEnabled = true;
                CompileButton.IsEnabled = true;
                CompilerStatus.Text = $"Found {result.Candidates.Count} compiler(s)";
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
                var status = pass.IsBuilt ? "ready" : "not built";
                var cb = new CheckBox
                {
                    Content = $"{pass.Name}  ({status})",
                    Tag = pass.Id,
                    IsEnabled = pass.IsBuilt,
                };
                ToolTipService.SetToolTip(cb, pass.Description);
                LlvmPassCheckboxes.Children.Add(cb);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Could not load LLVM passes: {ex.Message}");
        }
    }

    private void CompilationBackendCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LlvmPassesPanel is null) return; // not yet initialized during InitializeComponent

        if (CompilationBackendCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            tag.Equals("LlvmObfuscated", StringComparison.OrdinalIgnoreCase))
        {
            LlvmPassesPanel.Visibility = Visibility.Visible;
        }
        else
        {
            LlvmPassesPanel.Visibility = Visibility.Collapsed;
        }
    }

    private string GetSelectedCompilationBackend()
    {
        if (CompilationBackendCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            return tag;
        return "Deterministic";
    }

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

    private void UpdateConfigurationSummary()
    {
        var mainPage = MainPage.Instance;
        var licensecavePage = LicenseCavingPage.Instance;
        var packingPage = PackingPage.Instance;
        var finalizePage = FinalizePage.Instance;

        // ── LicensePayload summary ────────────────────────────────────────────
        if (mainPage != null)
        {
            // LicenseStub source
            var src = mainPage.CurrentLicenseStubSource;
            SummaryLicenseStub.Text = src switch
            {
                LicenseStubSource.File    => !string.IsNullOrEmpty(mainPage.LicenseStubFileTextBox.Text)
                                            ? Path.GetFileName(mainPage.LicenseStubFileTextBox.Text)
                                            : "File (not set)",
                LicenseStubSource.Raw     => !string.IsNullOrEmpty(mainPage.LicenseStubRawTextBox.Text)
                                            ? $"Raw ({mainPage.LicenseStubRawTextBox.Text.Length} chars)"
                                            : "Raw (empty)",
                LicenseStubSource.Url     => !string.IsNullOrEmpty(mainPage.LicenseStubUrlTextBox.Text)
                                            ? "Web licensepayload"
                                            : "URL (not set)",
                LicenseStubSource.Generic => mainPage.GenericLicenseStubCombo.SelectedItem is not null
                                            ? mainPage.GenericLicenseStubCombo.SelectedItem.ToString() ?? "Generic"
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
                SummaryEncoder.Text = $"{SummaryEncoder.Text} + POLY (x{mainPage.ShikataGaNaiEncodeCount}, max {mainPage.ShikataGaNaiMaxBytes} bytes)";
            }

            // Envelope
            if (mainPage.EnvelopeCombo.SelectedItem is not null)
                SummaryEnvelope.Text = mainPage.EnvelopeCombo.SelectedItem.ToString() ?? "None";
            else
                SummaryEnvelope.Text = "None";
        }
        else
        {
            SummaryLicenseStub.Text = "Not configured";
            SummaryTemplate.Text = "Not selected";
            SummaryEncoder.Text = "None";
            SummaryEnvelope.Text = "None";
        }

        // ── LicenseCaving summary ────────────────────────────────────────
        if (licensecavePage != null && licensecavePage.IsLicenseCavingEnabled)
        {
            LicenseCaveSummaryCard.Opacity = 1.0;
            LicenseCaveStatusBadge.Background = App.ThemeBrush("DraculaGreenBrush");
            LicenseCaveStatusText.Text = "Enabled";
            LicenseCaveDetails.Visibility = Visibility.Visible;
            LicenseCaveDisabledText.Visibility = Visibility.Collapsed;

            SummaryTargetPe.Text = licensecavePage.TargetPeFilePath != null 
                ? Path.GetFileName(licensecavePage.TargetPeFilePath) 
                : "Not selected";

            SummaryLicenseMethod.Text = licensecavePage.SelectedLicenseMethod switch
            {
                LicenseMethod.LicenseCave        => "Code Cave",
                LicenseMethod.NewSection      => "New Section",
                LicenseMethod.SectionExtension => "Section Extension",
                _                               => licensecavePage.SelectedLicenseMethod.ToString()
            };

            SummaryCarrier.Text = licensecavePage.SelectedLicenseInvoke switch
            {
                LicenseInvoke.LicenseEntryHijack     => "Entry Point Hijack",
                LicenseInvoke.LicenseFunctionCave => "Function LicenseCave",
                LicenseInvoke.LicenseTlsCallback           => "TLS Callback",
                _                                  => licensecavePage.SelectedLicenseInvoke.ToString()
            };
        }
        else
        {
            LicenseCaveSummaryCard.Opacity = 0.5;
            LicenseCaveStatusBadge.Background = App.ThemeBrush("TextFillColorSecondaryBrush");
            LicenseCaveStatusText.Text = "Disabled";
            LicenseCaveDetails.Visibility = Visibility.Collapsed;
            LicenseCaveDisabledText.Visibility = Visibility.Visible;
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
            steps.Insert(0, "Poly Encoder (pre)");
        steps.Insert(mainPage?.IsShikataGaNaiEnabled == true && !mainPage.IsShikataGaNaiPostPlacement ? 1 : 0, "Bin2Shell");
        if (StripToBinCheck?.IsChecked == true)
            steps.Add("Strip to .bin");
        if (mainPage?.IsShikataGaNaiEnabled == true && mainPage.IsShikataGaNaiPostPlacement)
            steps.Add("Poly Encoder (post)");
        if (licensecavePage?.IsLicenseCavingEnabled == true)
            steps.Add("LicenseCave PE");
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

    private void GoToLicensePayload_Click(object sender, RoutedEventArgs e) => NavigateToPage("MainPage");
    private void GoToLicenseCaving_Click(object sender, RoutedEventArgs e) => NavigateToPage("LicenseCavingPage");
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
            CompilerCombo.Items.Clear();

            if (result.Candidates == null || result.Candidates.Count == 0)
            {
                CompilerCombo.Items.Add("No compiler found");
                CompilerCombo.SelectedIndex = 0;
                CompilerCombo.IsEnabled = false;
                CompileButton.IsEnabled = false;
                CompilerStatus.Text = "No compiler detected. Install Visual Studio or MinGW.";
                CompilerStatus.Foreground = App.ThemeBrush("DraculaOrangeBrush");
                DownloadCompilerPanel.Visibility = Visibility.Visible;

                detectionWindow.SetComplete(false, "No compilers found");
                detectionWindow.AppendLog("\n❌ No compilers detected. Please install Visual Studio or MinGW.");
            }
            else
            {
                foreach (var compiler in result.Candidates)
                {
                    var displayName = $"{compiler.Kind} ({compiler.Path})";
                    CompilerCombo.Items.Add(displayName);
                    detectionWindow.AppendLog($"  ✓ Found: {compiler.Kind} at {compiler.Path}");
                }
                CompilerCombo.SelectedIndex = 0;
                CompilerCombo.IsEnabled = true;
                CompileButton.IsEnabled = true;
                CompilerStatus.Text = $"Found {result.Candidates.Count} compiler(s)";
                CompilerStatus.Foreground = App.ThemeBrush("DraculaGreenBrush");
                DownloadCompilerPanel.Visibility = Visibility.Collapsed;

                detectionWindow.SetComplete(true, $"Found {result.Candidates.Count} compiler(s)");
                detectionWindow.AppendLog($"\n✓ Detection complete. {result.Candidates.Count} compiler(s) available.");
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
                File.Exists(Path.Combine(current, "shieldguard.sln")))
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
            var licensecavePage = LicenseCavingPage.Instance;
            var packingPage  = PackingPage.Instance;

            if (mainPage == null)
            {
                ShowCompileResult(false, "Navigate to LicensePayload page first to configure licensestub and template.");
                return;
            }

            // ── Step 1: Compile licensepayload via CLI ──────────────────────────
            CompileProgressText.Text = "Step 1: Compiling licensepayload...";
            _logger.Info("\n[Step 1] Compiling licensepayload loader...");

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
                    _logger.Info("  (strip auto-enabled because POLY placement = post)");
                strippedBinPath = await RunStripStepAsync(compiledExePath, tempDir ?? Path.GetTempPath());
                if (strippedBinPath != null)
                    _logger.Ok($"Stripped: {Path.GetFileName(strippedBinPath)}");
                else
                    _logger.Warn("Strip step failed or produced no output.");
            }

            // ── Optional: Post-placement POLY on stripped .bin ─────────────
            if (needPostSgn)
            {
                if (strippedBinPath != null && File.Exists(strippedBinPath))
                {
                    CompileProgressText.Text = "Step 1c: Applying POLY to stripped .bin...";
                    _logger.Info("\n[Step 1c] Applying Poly Encoder to stripped loader.bin (post placement)...");
                    var sgnOut = await RunPostSgnStepAsync(
                        strippedBinPath,
                        mainPage.ShikataGaNaiEncodeCount,
                        mainPage.ShikataGaNaiMaxBytes);
                    if (sgnOut != null)
                        _logger.Ok($"POLY-encoded: {Path.GetFileName(sgnOut)}");
                    else
                        _logger.Warn("POLY step failed — stripped .bin left unencoded.");
                }
                else
                {
                    _logger.Warn("POLY placement=post requested, but no stripped .bin was produced — skipping POLY.");
                }
            }

            // ── Resolve final output: licensecave flow vs loader-only flow ───
            //
            // LicenseCave enabled  → defer the Save dialog until AFTER licensecaveing,
            //                     and only export the licensecaveed PE (the loader.exe and
            //                     bin2license intermediates stay in temp, never shipped).
            // LicenseCave disabled → keep the historical flow: prompt for the loader.exe
            //                     destination up front and copy it there.
            string outputDir;
            bool licensecaveEnabled = licensecavePage?.IsLicenseCavingEnabled == true
                                    && licensecavePage.TargetPeFilePath != null;

            if (licensecaveEnabled)
            {
                // Run the licensecave step into temp first.
                CompileProgressText.Text = "Step 2: LicenseCaving target PE...";
                _logger.Info("\n[Step 2] LicenseCaving target executable...");

                string licensecaveWorkDir = tempDir ?? Path.GetTempPath();
                string? originalLicenseStub = ResolveOriginalLicenseStubPath(mainPage);

                if (originalLicenseStub == null || !File.Exists(originalLicenseStub))
                {
                    _logger.Error("Original licensestub .bin not found — cannot licensecave.");
                    ShowCompileResult(false, "LicenseCave step requires the original licensestub .bin.");
                    return;
                }

                _logger.Info($"Using original licensestub: {Path.GetFileName(originalLicenseStub)} ({new FileInfo(originalLicenseStub).Length:N0} bytes)");
                var licensecaveedTemp = await RunLicenseCaveStepAsync(licensecavePage!, originalLicenseStub, licensecaveWorkDir);
                if (licensecaveedTemp == null || !File.Exists(licensecaveedTemp))
                {
                    ShowCompileResult(false, "LicenseCave step failed — see log.");
                    return;
                }

                // Resolve where the licensecaveed PE should land (user choice, or pre-set OutputPath).
                if (!string.IsNullOrWhiteSpace(OutputPath.Text))
                {
                    outputDir = OutputPath.Text;
                    Directory.CreateDirectory(outputDir);
                    var dest = Path.Combine(outputDir, Path.GetFileName(licensecaveedTemp));
                    File.Copy(licensecaveedTemp, dest, overwrite: true);
                    currentOutput = dest;
                }
                else
                {
                    var picker = new FileSavePicker();
                    picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                    picker.SuggestedFileName = Path.GetFileNameWithoutExtension(licensecavePage!.TargetPeFilePath!) + "_licensecaveed";
                    picker.FileTypeChoices.Add("Executable", new List<string> { ".exe" });

                    var hwnd = WindowNative.GetWindowHandle(App.ActiveWindow!);
                    InitializeWithWindow.Initialize(picker, hwnd);

                    var file = await picker.PickSaveFileAsync();
                    if (file != null)
                    {
                        outputDir = Path.GetDirectoryName(file.Path) ?? Path.GetTempPath();
                        Directory.CreateDirectory(outputDir);
                        File.Copy(licensecaveedTemp, file.Path, overwrite: true);
                        currentOutput = file.Path;
                    }
                    else
                    {
                        // User cancelled — leave the licensecaveed artifact in the temp work dir.
                        outputDir = licensecaveWorkDir;
                        currentOutput = licensecaveedTemp;
                    }
                }
            }
            else
            {
                _logger.Info("\n[Step 2] LicenseCaving: Skipped (disabled)");

                // No licensecave → the loader.exe IS the deliverable.
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

        // LicenseStub source
        switch (mainPage.CurrentLicenseStubSource)
        {
            case LicenseStubSource.File:
                var scFile = mainPage.LicenseStubFileTextBox.Text.Trim();
                if (string.IsNullOrEmpty(scFile) || !File.Exists(scFile))
                {
                    _logger.Error("LicenseStub file not set or not found.");
                    return (null, false);
                }
                // If the source is a PE (.exe), strip/convert it to a temp .bin first
                if (Path.GetExtension(scFile).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var peOpts = mainPage.GetPeSourceOptions();
                    var stripped = await StripExeLicenseStubAsync(scFile, peOpts);
                    if (stripped == null) return (null, false);
                    scFile = stripped;
                }
                args.AddRange(["-s", scFile]);
                break;

            case LicenseStubSource.Raw:
                var hex = mainPage.LicenseStubRawTextBox.Text.Trim();
                if (string.IsNullOrEmpty(hex))
                {
                    _logger.Error("Raw licensestub is empty.");
                    return (null, false);
                }
                // Write hex to a temp .bin and pass as file
                var tempBin = Path.Combine(Path.GetTempPath(), $"wm_raw_{Guid.NewGuid():N}.bin");
                await File.WriteAllBytesAsync(tempBin, Convert.FromHexString(hex.Replace(" ", "").Replace("\n", "")));
                args.AddRange(["-s", tempBin]);
                break;

            case LicenseStubSource.Url:
                var url = mainPage.LicenseStubUrlTextBox.Text.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    _logger.Error("LicenseStub URL is empty.");
                    return (null, false);
                }
                args.AddRange(["-LicenseStubUrl", url]);
                break;

            default:
                _logger.Error($"Unsupported licensestub source: {mainPage.CurrentLicenseStubSource}. Use File, Raw, or URL.");
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

        // Compilation backend
        var backend = GetSelectedCompilationBackend();
        if (!string.IsNullOrEmpty(backend) && !backend.Equals("Deterministic", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(["-Backend", backend]);
            foreach (var passId in GetSelectedLlvmPassIds())
                args.AddRange(["-LlvmPass", passId]);
        }

        // JSON output for machine-readable result
        args.Add("-Json");

        _logger.Info($"CLI: shieldguard-cli {string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}");

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
    /// Resolves the original raw licensestub .bin path from the current MainPage configuration.
    /// For File source: returns the file path directly.
    /// For Raw source: writes hex to a temp .bin file.
    /// For URL source: not supported (returns null).
    /// </summary>
    private string? ResolveOriginalLicenseStubPath(MainPage mainPage)
    {
        switch (mainPage.CurrentLicenseStubSource)
        {
            case LicenseStubSource.File:
                var scFile = mainPage.LicenseStubFileTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(scFile) && File.Exists(scFile))
                    return scFile;
                _logger.Warn($"LicenseStub file not found: {scFile}");
                return null;

            case LicenseStubSource.Raw:
                var hex = mainPage.LicenseStubRawTextBox.Text.Trim();
                if (string.IsNullOrEmpty(hex)) return null;
                try
                {
                    var tempBin = Path.Combine(Path.GetTempPath(), $"wm_raw_{Guid.NewGuid():N}.bin");
                    File.WriteAllBytes(tempBin, Convert.FromHexString(hex.Replace(" ", "").Replace("\n", "").Replace("\r", "")));
                    return tempBin;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to write raw licensestub: {ex.Message}");
                    return null;
                }

            default:
                _logger.Warn($"LicenseStub source '{mainPage.CurrentLicenseStubSource}' not supported for direct licensecaveing.");
                return null;
        }
    }

    /// <summary>
    /// Run POLY on the stripped loader .bin, producing a parallel .sgn.bin next to it.
    /// This is the "post-Bin2Shell" POLY placement: the loader itself is unmodified,
    /// but an additional POLY-wrapped artifact is emitted for external injection.
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

                _logger.Warn($"POLY exit {process.ExitCode}: {(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr)}");
                return null;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
            {
                // Not found — try the next candidate.
                continue;
            }
            catch (Exception ex)
            {
                _logger.Error($"POLY process failed: {ex.Message}");
                return null;
            }
        }

        _logger.Warn("POLY executable not found. Run 'shieldguard-cli provision' or install POLY.");
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

    private async Task<string?> RunLicenseCaveStepAsync(
        LicenseCavingPage licensecavePage,
        string licensestubBinPath,
        string outputDir)
    {
        var targetPe = licensecavePage.TargetPeFilePath;
        if (targetPe == null) return null;

        if (!licensecavePage.IsInjectionValid)
        {
            _logger.Warn("LicenseCave configuration is currently invalid — skipping licensecave step.");
            return null;
        }

        if (!File.Exists(licensestubBinPath))
        {
            _logger.Warn($"LicenseStub binary not found: {licensestubBinPath} — skipping.");
            return null;
        }

        if (!_cli.IsAvailable)
        {
            _logger.Warn("CLI not available — licensecave step skipped.");
            return null;
        }

        var outFile = Path.Combine(outputDir, "licensecaveed_" + Path.GetFileName(targetPe));

        var args = new List<string>
        {
            "licensecave",
            "-Pe",        targetPe,
            "-LicenseStub", licensestubBinPath,
            "-Output",    outFile,
        };

        // Pass GUI-selected options to CLI
        var method = licensecavePage.SelectedLicenseMethod switch
        {
            LicenseMethod.LicenseCave => "code-cave",
            LicenseMethod.NewSection => "new-section",
            LicenseMethod.SectionExtension => "section-ext",
            _ => "code-cave"
        };
        args.AddRange(new[] { "-Method", method });

        if (!licensecavePage.RemoveSignature)
            args.Add("-NoRemoveSig");
        if (!licensecavePage.PatchSubsystemToGui)
            args.Add("-NoPatchSubsystem");
        if (!licensecavePage.PreserveOriginalEntry)
            args.Add("-NoPreserveEntry");
        if (!licensecavePage.PatchIat)
            args.Add("-NoPatchIat");

        // Carrier invoke method
        var carrierStr = licensecavePage.SelectedLicenseInvoke switch
        {
            LicenseInvoke.LicenseEntryHijack => "entry-point",
            LicenseInvoke.LicenseFunctionCave => "function-licensecave",
            LicenseInvoke.LicenseTlsCallback => "tls",
            _ => "entry-point"
        };
        args.AddRange(new[] { "-Carrier", carrierStr });

        // Encryption
        if (licensecavePage.SelectedEncryption != LicensePayloadEncryption.None)
        {
            var encStr = licensecavePage.SelectedEncryption switch
            {
                LicensePayloadEncryption.Xor  => "xor",
                LicensePayloadEncryption.Xor2 => "xor2",
                LicensePayloadEncryption.Rc4  => "rc4",
                _                      => "none"
            };
            args.AddRange(["-Encryption", encStr]);
            if (licensecavePage.XorKey is { } xorKey)
                args.AddRange(["-XorKey", xorKey]);
        }

        // Patch-exit
        if (!licensecavePage.PatchExit)
            args.Add("-NoPatchExit");

        // Section name
        if (licensecavePage.CustomSectionName is { } sectionName)
            args.AddRange(["-SectionName", sectionName]);

        // Cave min size
        if (licensecavePage.CaveMinSize != 64)
            args.AddRange(["-CaveMinSize", licensecavePage.CaveMinSize.ToString()]);

        // Dry run
        if (licensecavePage.DryRun)
            args.Add("-DryRun");

        var result = await _cli.RunAsync(args, line => _logger.Info(line));

        if (result.Success && File.Exists(outFile))
        {
            _logger.Ok($"LicenseCaveed: {Path.GetFileName(outFile)}");
            return outFile;
        }

        _logger.Warn($"LicenseCaving failed (exit {result.ExitCode}).");
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
    //   - Below this size the result is almost certainly not real licensestub (just a stub).
    //   - Above this size the resulting C array trips MSVC's C1060 "out of heap" error.
    private const long SuspiciousMinStripSize = 64;
    private const long LargeStripWarnSize     = 1 * 1024 * 1024;

    /// <summary>
    /// Resolves the user's input .exe licensestub source to a flat <c>.bin</c> in the temp folder.
    /// Routes managed (.NET) inputs through licenseassembly and native licensestub-format PEs through the
    /// CLI <c>strip</c> command. Returns the temp file path on success, <c>null</c> on failure.
    /// </summary>
    private async Task<string?> StripExeLicenseStubAsync(string exePath, PeSourceOptions peOpts)
    {
        var tempBin = Path.Combine(Path.GetTempPath(), $"wm_strip_{Guid.NewGuid():N}.bin");

        return peOpts.IsLicenseAssemblyConversion
            ? await ConvertManagedPeWithLicenseAssemblyAsync(exePath, tempBin, peOpts)
            : await StripNativePeWithCliAsync(exePath, tempBin, peOpts);
    }

    /// <summary>Run licenseassembly.exe to convert a managed .NET assembly to PIC licensestub.</summary>
    private async Task<string?> ConvertManagedPeWithLicenseAssemblyAsync(string exePath, string tempBin, PeSourceOptions peOpts)
    {
        var licenseassemblySvc = new LicenseAssemblyService(_paths.LicenseAssemblyExecutable, _logger);
        if (!licenseassemblySvc.IsAvailable)
        {
            _logger.Error("licenseassembly.exe not found. Provision optional tools from the Settings page to download LicenseAssembly.");
            return null;
        }

        var opts = new LicenseAssemblyOptions
        {
            InputPath  = exePath,
            OutputPath = tempBin,
            Arch       = peOpts.LicenseAssemblyArch,
            Class      = peOpts.LicenseAssemblyClass,
            Method     = peOpts.LicenseAssemblyMethod,
            Params     = peOpts.LicenseAssemblyParams,
        };

        _logger.Info($"Converting .NET assembly to licensestub via licenseassembly (arch={opts.Arch})...");
        var licenseassemblyResult = await licenseassemblySvc.ConvertAsync(opts);

        if (!licenseassemblyResult.Success || !File.Exists(tempBin))
        {
            _logger.Error($"LicenseAssembly conversion failed: {licenseassemblyResult.Error}");
            return null;
        }

        var size = new FileInfo(tempBin).Length;
        _logger.Ok($"LicenseAssembly conversion complete → temp .bin ({size:N0} bytes)");
        return tempBin;
    }

    /// <summary>Shell out to the CLI to strip a native licensestub-format PE down to flat bytes.</summary>
    private async Task<string?> StripNativePeWithCliAsync(string exePath, string tempBin, PeSourceOptions peOpts)
    {
        if (!_cli.IsAvailable)
        {
            _logger.Error("CLI not available — cannot strip PE licensestub source.");
            return null;
        }

        var args = BuildStripArgs(exePath, tempBin, peOpts);

        _logger.Info($"Stripping PE licensestub source ({peOpts.PeStripMode})...");
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
            _logger.Warn($"Stripped binary is only {size} bytes — this PE is almost certainly not a licensestub-format " +
                         "executable. Use raw licensestub (.bin from msfvenom -f raw) instead.");
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
        picker.SuggestedFileName = $"shieldguard_recipe_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        picker.FileTypeChoices.Add("ShieldGuard Recipe", new List<string> { ".json" });

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
        var licensecavePage = LicenseCavingPage.Instance;
        var packingPage  = PackingPage.Instance;
        var finalizePage = FinalizePage.Instance;

        var recipe = new PipelineRecipe
        {
            Version = 1,
            CreatedAt = DateTime.UtcNow,
        };

        if (mainPage != null)
        {
            recipe.LicensePayload = new LicensePayloadRecipe
            {
                Source = mainPage.CurrentLicenseStubSource.ToString(),
                File = mainPage.LicenseStubFileTextBox.Text,
                RawHex = mainPage.LicenseStubRawTextBox.Text,
                Url = mainPage.LicenseStubUrlTextBox.Text,
                Generic = mainPage.GenericLicenseStubCombo.SelectedItem?.ToString(),
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
                foreach (var kv in opts.ComboValues) recipe.LicensePayload.SnippetCombos[kv.Key] = kv.Value;
                foreach (var kv in opts.TextValues) recipe.LicensePayload.SnippetTexts[kv.Key] = kv.Value;
                foreach (var kv in opts.ListValues) recipe.LicensePayload.SnippetLists[kv.Key] = new List<string>(kv.Value ?? new List<string>());
            }
        }

        if (licensecavePage != null)
        {
            recipe.LicenseCave = new LicenseCaveRecipe
            {
                Enabled = licensecavePage.IsLicenseCavingEnabled,
                TargetPePath = licensecavePage.TargetPeFilePath,
                LicenseMethod = licensecavePage.SelectedLicenseMethod.ToString(),
                LicenseInvoke = licensecavePage.SelectedLicenseInvoke.ToString(),
                PreserveEntry = licensecavePage.PreserveOriginalEntry,
                PatchIat = licensecavePage.PatchIat,
                RemoveSignature = licensecavePage.RemoveSignature,
                PatchSubsystem = licensecavePage.PatchSubsystemToGui,
                PatchExit = licensecavePage.PatchExit,
                DryRun = licensecavePage.DryRun,
                XorKey = licensecavePage.XorKey,
                SectionName = licensecavePage.CustomSectionName,
                CaveMinSize = licensecavePage.CaveMinSize,
                Encryption = licensecavePage.SelectedEncryption.ToString(),
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
        var licensecavePage = LicenseCavingPage.Instance;
        var packingPage  = PackingPage.Instance;
        var finalizePage = FinalizePage.Instance;

        if (mainPage != null && recipe.LicensePayload != null)
        {
            if (!string.IsNullOrEmpty(recipe.LicensePayload.File))
                mainPage.LicenseStubFileTextBox.Text = recipe.LicensePayload.File;
            if (!string.IsNullOrEmpty(recipe.LicensePayload.RawHex))
                mainPage.LicenseStubRawTextBox.Text = recipe.LicensePayload.RawHex;
            if (!string.IsNullOrEmpty(recipe.LicensePayload.Url))
                mainPage.LicenseStubUrlTextBox.Text = recipe.LicensePayload.Url;

            // Best-effort: apply POLY state by finding the checkbox/radios on MainPage
            mainPage.ApplySgnRecipe(
                recipe.LicensePayload.SgnEnabled,
                recipe.LicensePayload.SgnEncodeCount,
                recipe.LicensePayload.SgnMaxBytes,
                recipe.LicensePayload.SgnPlacement);

            // Template + encoder/envelope are best-effort from display text lookup
            mainPage.ApplyTemplateAndEncoding(
                recipe.LicensePayload.TemplateId,
                recipe.LicensePayload.EncoderIndex,
                recipe.LicensePayload.EnvelopeIndex);

            var opts = mainPage.Coordinator?.TemplateOptions;
            if (opts != null && recipe.LicensePayload.SnippetCombos.Count + recipe.LicensePayload.SnippetTexts.Count + recipe.LicensePayload.SnippetLists.Count > 0)
            {
                opts.ComboValues.Clear();
                foreach (var kv in recipe.LicensePayload.SnippetCombos) opts.ComboValues[kv.Key] = kv.Value;
                opts.TextValues.Clear();
                foreach (var kv in recipe.LicensePayload.SnippetTexts) opts.TextValues[kv.Key] = kv.Value;
                opts.ListValues.Clear();
                foreach (var kv in recipe.LicensePayload.SnippetLists) opts.ListValues[kv.Key] = new List<string>(kv.Value ?? new List<string>());
            }
        }

        if (licensecavePage != null && recipe.LicenseCave != null)
        {
            licensecavePage.ApplyRecipe(recipe.LicenseCave);
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
//  Recipe serialization models
// ═══════════════════════════════════════════════════════════════════════

public sealed class PipelineRecipe
{
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public LicensePayloadRecipe? LicensePayload { get; set; }
    public LicenseCaveRecipe? LicenseCave { get; set; }
    public PackingRecipe? Packing { get; set; }
    public FinalizeRecipe? Finalize { get; set; }
    public CompileRecipe? Compile { get; set; }
}

public sealed class LicensePayloadRecipe
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

public sealed class LicenseCaveRecipe
{
    public bool Enabled { get; set; }
    public string? TargetPePath { get; set; }
    public string? LicenseMethod { get; set; }
    public string? LicenseInvoke { get; set; }
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
