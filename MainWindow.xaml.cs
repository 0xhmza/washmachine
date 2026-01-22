using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Washmachine.Controllers;
using Washmachine.Logging;
using Washmachine.Services;
using Washmachine.Views;

namespace Washmachine;

public sealed partial class MainWindow : Window, IMainFormView
{
    private const string ElevationArg = "--elevated";
    private const uint TokenQuery = 0x0008;

    private readonly IAppLogger _logger;
    private readonly IUserInteractionService _interaction;
    private readonly MainFormCoordinator _coordinator;
    private readonly IRequirementProvisioner _requirements;

    public MainWindow()
    {
        InitializeComponent();

        _logger = new RichEditBoxLogger(debugBox);

        var paths = new AppPaths();
        var clipboard = new ClipboardService();
        _interaction = new UserInteractionService();
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
            _interaction);

        Loaded += MainWindow_Loaded;
        _logger.Info("Initializing application...");
    }

    public Window Window => this;
    public XamlRoot XamlRoot => Root.XamlRoot;
    public FrameworkElement RootElement => Root;

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
        if (!await EnsureElevatedAsync())
        {
            Close();
            return;
        }

        try
        {
            await _requirements.EnsureRequirementsAsync(this);
            await _coordinator.InitializeAsync(this);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to initialize application: {ex.Message}");
            await _interaction.ShowMessageAsync(
                this,
                $"Failed to prepare the application's requirements.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Startup Error",
                DialogButtons.Ok,
                DialogIcon.Error);
            submitButton.IsEnabled = false;
        }
    }

    private async void button1_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.SelectShellcodeFileAsync(this);
    }

    private async void button2_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.PasteShellcodeFromClipboardAsync(this, shellcodeRAW);
    }

    private async void button3_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.PasteShellcodeFromClipboardAsync(this, shellcodeURL);
    }

    private async void RAWShellcodeInfo_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.ShowShellcodeTipAsync(this);
    }

    private async void submitButton_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.HandleSubmitAsync(this);
    }

    private async void templateComboBox_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
    {
        await _coordinator.HandleTemplateChangedAsync(this);
    }

    private async void button4_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.OpenTemplateConfigAsync(this);
    }

    private async void WebPayloadGenerator_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.GenerateWebPayloadAsync(this);
    }

    private async Task<bool> EnsureElevatedAsync()
    {
        if (IsElevated())
            return true;

        var args = Environment.GetCommandLineArgs();
        if (args.Any(arg => string.Equals(arg, ElevationArg, StringComparison.OrdinalIgnoreCase)))
        {
            await _interaction.ShowMessageAsync(
                this,
                "Failed to obtain administrator access. Please restart the app and approve the UAC prompt.",
                "Elevation required",
                DialogButtons.Ok,
                DialogIcon.Warning);
            return false;
        }

        await _interaction.ShowMessageAsync(
            this,
            "Administrator access is required to locate Visual Studio C++ compilers.\r\n\r\nThe app will request elevation now.",
            "Administrator Required",
            DialogButtons.Ok,
            DialogIcon.Information);

        try
        {
            var exePath = GetExecutablePath();
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                await _interaction.ShowMessageAsync(
                    this,
                    "Unable to locate the executable path needed to relaunch with elevation.",
                    "Elevation error",
                    DialogButtons.Ok,
                    DialogIcon.Error);
                return false;
            }

            var argList = args
                .Skip(1)
                .Where(arg => !string.Equals(arg, ElevationArg, StringComparison.OrdinalIgnoreCase))
                .ToList();
            argList.Add(ElevationArg);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
                Arguments = argList.Count > 0
                    ? string.Join(" ", argList.Select(QuoteArg))
                    : string.Empty
            };

            var elevated = Process.Start(psi);
            if (elevated == null)
            {
                await _interaction.ShowMessageAsync(
                    this,
                    "Failed to launch the elevated instance.",
                    "Elevation error",
                    DialogButtons.Ok,
                    DialogIcon.Error);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            await _interaction.ShowMessageAsync(
                this,
                "This app needs administrator rights to continue.",
                "Elevation required",
                DialogButtons.Ok,
                DialogIcon.Warning);
        }
        catch (Exception ex)
        {
            await _interaction.ShowMessageAsync(
                this,
                $"Failed to elevate: {ex.Message}",
                "Elevation error",
                DialogButtons.Ok,
                DialogIcon.Error);
        }

        return false;
    }

    private static bool IsElevated()
    {
        if (TryGetElevationState(out bool elevated))
            return elevated;

        return IsAdministrator();
    }

    private static bool TryGetElevationState(out bool elevated)
    {
        elevated = false;
        try
        {
            using var process = Process.GetCurrentProcess();
            if (!OpenProcessToken(process.Handle, TokenQuery, out var token))
                return false;

            try
            {
                if (!GetTokenInformation(
                        token,
                        TokenInformationClass.TokenElevation,
                        out var tokenInfo,
                        Marshal.SizeOf<TokenElevation>(),
                        out _))
                {
                    return false;
                }

                elevated = tokenInfo.TokenIsElevated != 0;
                return true;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string? GetExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
            return path;

        try
        {
            var mainModule = Process.GetCurrentProcess().MainModule;
            if (!string.IsNullOrWhiteSpace(mainModule?.FileName))
                return mainModule.FileName;
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private static string QuoteArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        return value.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }

    private enum TokenInformationClass
    {
        TokenElevation = 20
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public int TokenIsElevated;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        TokenInformationClass tokenInformationClass,
        out TokenElevation tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
