using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Washmachine.Logging;
using Washmachine.Models;
using Washmachine.Services;
using Windows.Graphics;

namespace Washmachine.Views;

/// <summary>
/// First-run / launch progress window. Provisions external requirements
/// (Bin2Shell) and detects a C/C++ compiler; if no compiler is found the
/// user is asked to locate one or opt in to downloading MinGW-w64.
/// The main window is only opened after this flow completes.
/// </summary>
public sealed class StartupWindow
{
    private readonly Window _window;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly StackPanel _steps;
    private readonly ProgressBar _progress;
    private readonly TextBlock _footer;

    private readonly StepRow _provisionRow;
    private readonly StepRow _compilerRow;

    private readonly AppPaths _paths = new();
    private readonly DebugLogger _logger = new();

    public StartupWindow()
    {
        // Brand cluster — icon glyph + product/role text
        var brandIcon = new FontIcon
        {
            Glyph = "\uE943",
            FontSize = 22,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };

        _title = new TextBlock
        {
            Text = "Washmachine",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
        };

        var brandRow = new StackPanel { Orientation = Orientation.Horizontal };
        brandRow.Children.Add(brandIcon);
        brandRow.Children.Add(_title);

        _subtitle = new TextBlock
        {
            Text = "Preparing requirements…",
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Margin = new Thickness(0, 2, 0, 14),
        };

        _provisionRow = new StepRow("Ensuring external requirements (Bin2Shell, SGN)");
        _compilerRow = new StepRow("Locating a C/C++ compiler");

        _steps = new StackPanel { Spacing = 8 };
        _steps.Children.Add(_provisionRow.Root);
        _steps.Children.Add(_compilerRow.Root);

        // Steps live inside a subtle Fluent card so they read as a grouped panel
        var stepsCard = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Child = _steps,
        };

        _progress = new ProgressBar
        {
            IsIndeterminate = true,
            Margin = new Thickness(0, 16, 0, 6),
        };

        _footer = new TextBlock
        {
            Text = "First-run downloads cache under Tools/.",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        };

        var root = new StackPanel
        {
            // Top padding accounts for the now-extended (invisible) title bar
            Margin = new Thickness(28, 44, 28, 22),
            Spacing = 2,
        };
        root.Children.Add(brandRow);
        root.Children.Add(_subtitle);
        root.Children.Add(stepsCard);
        root.Children.Add(_progress);
        root.Children.Add(_footer);

        _window = new Window { Title = "Washmachine — starting up", Content = root };

        // Fluent chrome to match MainWindow
        _window.SystemBackdrop = new MicaBackdrop();
        _window.ExtendsContentIntoTitleBar = true;

        _window.AppWindow.Resize(new SizeInt32(560, 320));

        var display = DisplayArea.GetFromWindowId(_window.AppWindow.Id, DisplayAreaFallback.Primary);
        if (display != null)
        {
            var work = display.WorkArea;
            _window.AppWindow.Move(new PointInt32(
                work.X + (work.Width - 560) / 2,
                work.Y + (work.Height - 320) / 2));
        }

        var presenter = _window.AppWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
    }

    public void Show() => _window.Activate();
    public void Close() => _window.Close();
    public XamlRoot XamlRoot => ((FrameworkElement)_window.Content).XamlRoot;

    /// <summary>
    /// Orchestrate provisioning and compiler detection. Returns a tuple
    /// describing whether the app is ready to proceed.
    /// </summary>
    public async Task<StartupOutcome> RunAsync()
    {
        // ── 1) Provision ─────────────────────────────────────────
        _provisionRow.SetRunning("Checking Bin2Shell, SGN, Donut toolchains…");
        bool provisioned;
        try
        {
            var provisioner = new RequirementProvisioner(_paths, _logger);
            var reporter = new RowProgressReporter(this, _provisionRow);
            await provisioner.EnsureRequirementsAsync(reporter);
            _provisionRow.SetOk("Requirements ready.");
            provisioned = true;
        }
        catch (Exception ex)
        {
            _provisionRow.SetWarning($"Provisioning failed: {ex.Message}");
            provisioned = false;
        }

        // ── 2) Detect compiler ───────────────────────────────────
        _compilerRow.SetRunning("Scanning PATH, Visual Studio, bundled toolchains…");
        var locator = new CompilerToolLocator(_logger);
        var discovery = await locator.DiscoverAsync();

        CompilerToolCandidate? best = discovery.Best;

        if (best != null)
        {
            _compilerRow.SetOk($"{best.Kind} → {Trim(best.Path, 60)}");
        }
        else
        {
            _compilerRow.SetWarning("No C/C++ compiler found on this machine.");

            _progress.IsIndeterminate = false;
            _progress.Value = 0;

            var resolved = await PromptForCompilerAsync(locator);
            if (resolved != null)
            {
                best = resolved;
                _compilerRow.SetOk($"{best.Kind} → {Trim(best.Path, 60)}");
            }
        }

        _progress.IsIndeterminate = false;
        _progress.Value = 100;

        return new StartupOutcome(
            Provisioned: provisioned,
            CompilerPath: best?.Path,
            CompilerKind: best?.Kind);
    }

    private async Task<CompilerToolCandidate?> PromptForCompilerAsync(ICompilerToolLocator locator)
    {
        var dialog = new ContentDialog
        {
            Title = "No compiler found",
            Content = "Washmachine needs cl.exe (MSVC), clang++.exe, or g++.exe to compile loaders. " +
                      "You can point to an existing install on disk, or let the app download MinGW-w64 " +
                      "(~60 MB) for later use.",
            PrimaryButtonText = "Locate…",
            SecondaryButtonText = "Download MinGW-w64",
            CloseButtonText = "Skip",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
            return await LocateCompilerAsync(locator);

        if (result == ContentDialogResult.Secondary)
            return await DownloadMingwAsync(locator);

        return null;
    }

    private async Task<CompilerToolCandidate?> LocateCompilerAsync(ICompilerToolLocator locator)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add(".exe");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file == null) return null;

        try
        {
            var result = await locator.AddManualCandidateAsync(file.Path);
            return result.Best;
        }
        catch (Exception ex)
        {
            var err = new ContentDialog
            {
                Title = "Invalid compiler",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot,
            };
            await err.ShowAsync();
            return null;
        }
    }

    private async Task<CompilerToolCandidate?> DownloadMingwAsync(ICompilerToolLocator locator)
    {
        _compilerRow.SetRunning("Downloading MinGW-w64…");
        _progress.IsIndeterminate = false;
        _progress.Value = 0;

        var downloader = new MingwDownloader(_logger, _paths);
        var ok = await downloader.DownloadAndExtractAsync((msg, pct) =>
        {
            _window.DispatcherQueue.TryEnqueue(() =>
            {
                _compilerRow.SetRunning(msg);
                if (pct >= 0)
                {
                    _progress.IsIndeterminate = false;
                    _progress.Value = pct;
                }
                else
                {
                    _progress.IsIndeterminate = true;
                }
            });
        });

        if (!ok)
        {
            _compilerRow.SetWarning("Download failed. Locate a compiler manually, or retry later.");
            return null;
        }

        var redetected = await locator.DiscoverAsync();
        return redetected.Best;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : "…" + s[^(max - 1)..];

    // ── Helpers ──────────────────────────────────────────────────

    private sealed class RowProgressReporter : IProgressReporter
    {
        private readonly StartupWindow _owner;
        private readonly StepRow _row;

        public RowProgressReporter(StartupWindow owner, StepRow row)
        {
            _owner = owner;
            _row = row;
        }

        public void UpdateStatus(string message, int percentComplete)
        {
            _owner._window.DispatcherQueue.TryEnqueue(() =>
            {
                _row.SetRunning(message);
                if (percentComplete >= 0)
                {
                    _owner._progress.IsIndeterminate = false;
                    _owner._progress.Value = percentComplete;
                }
                else
                {
                    _owner._progress.IsIndeterminate = true;
                }
            });
        }

        public void Close() { /* final state set by caller */ }
    }

    private sealed class DebugLogger : IAppLogger
    {
        public void Info(string message) => System.Diagnostics.Debug.WriteLine($"[INFO] {message}");
        public void Ok(string message) => System.Diagnostics.Debug.WriteLine($"[OK] {message}");
        public void Warn(string message) => System.Diagnostics.Debug.WriteLine($"[WARN] {message}");
        public void Error(string message) => System.Diagnostics.Debug.WriteLine($"[ERR] {message}");
        public void Debug(string message) => System.Diagnostics.Debug.WriteLine($"[DBG] {message}");
    }

    private sealed class StepRow
    {
        public StackPanel Root { get; }
        private readonly TextBlock _icon;
        private readonly TextBlock _label;
        private readonly TextBlock _detail;

        public StepRow(string label)
        {
            _icon = new TextBlock
            {
                Text = "○",
                Width = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.6,
                FontFamily = new FontFamily("Segoe UI"),
            };
            _label = new TextBlock
            {
                Text = label,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _detail = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Opacity = 0.6,
                Margin = new Thickness(26, 0, 0, 0),
                TextWrapping = TextWrapping.NoWrap,
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(_icon);
            row.Children.Add(_label);

            Root = new StackPanel();
            Root.Children.Add(row);
            Root.Children.Add(_detail);
        }

        public void SetRunning(string detail)
        {
            _icon.Text = "◐";
            _icon.Opacity = 1.0;
            _icon.Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
            _detail.Text = detail;
        }

        public void SetOk(string detail)
        {
            _icon.Text = "✓";
            _icon.Opacity = 1.0;
            _icon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            _detail.Text = detail;
        }

        public void SetWarning(string detail)
        {
            _icon.Text = "!";
            _icon.Opacity = 1.0;
            _icon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            _detail.Text = detail;
        }
    }
}

public sealed record StartupOutcome(bool Provisioned, string? CompilerPath, string? CompilerKind)
{
    public bool CompilerFound => !string.IsNullOrEmpty(CompilerPath);
}
