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
        await RefreshCompilers();
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

        // Update Payload summary (from MainPage)
        if (mainPage != null)
        {
            // Get shellcode info from coordinator if available
            SummaryShellcode.Text = "Configured";
            SummaryTemplate.Text = "Default";
            SummaryEncoder.Text = "XOR";
            SummaryEnvelope.Text = "None";
        }
        else
        {
            SummaryShellcode.Text = "Not configured";
            SummaryTemplate.Text = "Not selected";
            SummaryEncoder.Text = "None";
            SummaryEnvelope.Text = "None";
        }

        // Update Backdooring summary
        if (backdoorPage != null && backdoorPage.IsBackdooringEnabled)
        {
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
            BackdoorStatusBadge.Background = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            BackdoorStatusText.Text = "Disabled";
            BackdoorDetails.Visibility = Visibility.Collapsed;
            BackdoorDisabledText.Visibility = Visibility.Visible;
        }

        // Update Packing summary
        if (packingPage != null && packingPage.IsPackingEnabled)
        {
            PackingStatusBadge.Background = new SolidColorBrush(Microsoft.UI.Colors.Green);
            PackingStatusText.Text = "Enabled";
            PackingDetails.Visibility = Visibility.Visible;
            PackingDisabledText.Visibility = Visibility.Collapsed;

            SummaryPacker.Text = "UPX";
            SummaryPackLevel.Text = "Best";
        }
        else
        {
            PackingStatusBadge.Background = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            PackingStatusText.Text = "Disabled";
            PackingDetails.Visibility = Visibility.Collapsed;
            PackingDisabledText.Visibility = Visibility.Visible;
        }

        // Update build description
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

            // Get page references
            var backdoorPage = BackdooringPage.Instance;
            var packingPage = PackingPage.Instance;
            
            // Step 1: Compile the loader
            CompileProgressText.Text = "Step 1: Compiling payload...";
            _logger.Info("\n[Step 1] Compiling payload loader...");

            var mainPage = MainPage.Instance;
            if (mainPage == null)
            {
                _logger.Error("Cannot compile: MainPage not found. Configure shellcode and template first.");
                ShowCompileResult(false, "Configure shellcode and template on Payload page first.");
                return;
            }

            var coordinator = mainPage.Coordinator;
            await coordinator.HandleSubmitAsync(mainPage);
            
            // Find the compiled output
            var outputDir = string.IsNullOrEmpty(OutputPath.Text) 
                ? _paths.EnsureTempSourceDirectory()
                : OutputPath.Text;

            Directory.CreateDirectory(outputDir);

            var compiledExe = Directory.GetFiles(_paths.EnsureTempSourceDirectory(), "*.exe")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .FirstOrDefault();

            if (compiledExe == null)
            {
                ShowCompileResult(false, "Compilation failed. Check the log for errors.");
                return;
            }

            _logger.Ok($"Compiled: {Path.GetFileName(compiledExe)}");
            var currentOutput = compiledExe;

            // Step 2: Backdoor if enabled
            if (backdoorPage?.IsBackdooringEnabled == true && backdoorPage.TargetPeFilePath != null)
            {
                CompileProgressText.Text = "Step 2: Backdooring target PE...";
                _logger.Info("\n[Step 2] Backdooring target executable...");

                var backdoorOptions = new PeBackdoorOptions
                {
                    TargetPePath = backdoorPage.TargetPeFilePath,
                    ShellcodePath = currentOutput, // Use compiled loader as "shellcode" (it needs to be shellcode actually)
                    OutputPath = Path.Combine(outputDir, "backdoored_" + Path.GetFileName(backdoorPage.TargetPeFilePath)),
                    Method = backdoorPage.SelectedInjectionMethod,
                    CarrierInvoke = backdoorPage.SelectedCarrierInvoke,
                    PreserveOriginalEntry = backdoorPage.PreserveOriginalEntry,
                    PatchIat = backdoorPage.PatchIat,
                    RemoveSignature = backdoorPage.RemoveSignature,
                    PatchSubsystemToGui = backdoorPage.PatchSubsystemToGui
                };

                // For backdooring, we actually need the raw shellcode, not the exe
                // The bin2shell output (from payload step) should have the shellcode
                var shellcodeFile = Path.Combine(_paths.EnsureTempSourceDirectory(), "shellcode.bin");
                if (File.Exists(shellcodeFile))
                {
                    backdoorOptions.ShellcodePath = shellcodeFile;
                    var result = await _backdoorService.BackdoorAsync(backdoorOptions);

                    if (result.Success)
                    {
                        _logger.Ok($"Backdoored: {Path.GetFileName(result.OutputPath)}");
                        currentOutput = result.OutputPath;

                        foreach (var step in result.Steps)
                        {
                            _logger.Info($"  • {step}");
                        }
                    }
                    else
                    {
                        _logger.Warn($"Backdooring skipped: {result.ErrorMessage}");
                    }
                }
                else
                {
                    _logger.Warn("Shellcode file not found. Backdooring skipped.");
                }
            }
            else
            {
                _logger.Info("\n[Step 2] Backdooring: Skipped (disabled)");
            }

            // Step 3: Pack if enabled
            if (packingPage?.IsPackingEnabled == true && !string.IsNullOrEmpty(packingPage.UpxPath))
            {
                CompileProgressText.Text = "Step 3: Packing with UPX...";
                _logger.Info("\n[Step 3] Packing with UPX...");

                var packedOutput = Path.Combine(outputDir, "packed_" + Path.GetFileName(currentOutput));
                var upxArgs = packingPage.GetUpxArguments();

                var psi = new ProcessStartInfo
                {
                    FileName = packingPage.UpxPath,
                    Arguments = $"{upxArgs} -o \"{packedOutput}\" \"{currentOutput}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = await proc.StandardOutput.ReadToEndAsync();
                    var error = await proc.StandardError.ReadToEndAsync();
                    await proc.WaitForExitAsync();

                    if (proc.ExitCode == 0 && File.Exists(packedOutput))
                    {
                        _logger.Ok($"Packed: {Path.GetFileName(packedOutput)}");
                        _logger.Info(output);
                        currentOutput = packedOutput;
                    }
                    else
                    {
                        _logger.Warn($"Packing failed: {error}");
                    }
                }
            }
            else
            {
                _logger.Info("\n[Step 3] Packing: Skipped (disabled or UPX not found)");
            }

            // Final output
            var finalOutput = Path.Combine(outputDir, Path.GetFileName(currentOutput ?? "output.exe"));
            if (!string.IsNullOrEmpty(currentOutput) && currentOutput != finalOutput)
            {
                File.Copy(currentOutput, finalOutput, true);
            }

            _lastOutputPath = outputDir;

            _logger.Info("\n═══════════════════════════════════════════════");
            _logger.Ok($"Build complete! Output: {Path.GetFileName(finalOutput)}");
            _logger.Info("═══════════════════════════════════════════════");

            ShowCompileResult(true, $"Success: {Path.GetFileName(finalOutput)}");

            if (OpenFolderAfterCompile.IsChecked == true)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = outputDir,
                    UseShellExecute = true
                });
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
