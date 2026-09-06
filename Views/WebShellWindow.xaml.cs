using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Washmachine.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace Washmachine.Views;

/// <summary>
/// WebView2-hosted shell for Washmachine.
///
/// Chrome model:
/// <list type="bullet">
///   <item>System title bar is suppressed via <c>OverlappedPresenter.SetBorderAndTitleBar(true, false)</c>.</item>
///   <item>A 36-px custom title bar at the top of the XAML hosts the W logo, app title,
///   a transparent drag region (PointerPressed dispatches a Win32 caption drag), and
///   custom min/max/close buttons that route to <c>AppWindow</c>.</item>
///   <item>The remaining area is the WebView2 control, covered by a loading overlay
///   until <c>NavigationCompleted</c> fires.</item>
/// </list>
///
/// Why custom chrome: user wanted "fullscreen with own min/max/close buttons" and
/// the previous slim-strip approach left the system caption visible. With the strip
/// gone, the entire window paints in app colors with no Windows chrome bleed-through.
/// </summary>
public sealed partial class WebShellWindow : Window
{
    private WebShellBridge? _bridge;
    private IntPtr _hwnd;

    // Win32 messages we need for caption drag + double-click maximize
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION        = 0x0002;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    public WebShellWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        SystemBackdrop = new MicaBackdrop();
        Title = "Washmachine";

        // Hide the system caption entirely — we draw our own.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            presenter.IsMinimizable = true;
            presenter.IsMaximizable = true;
            presenter.IsResizable = true;
        }

        // Window-resize guard (min size)
        AppWindow.Changed += (_, args) =>
        {
            if (!args.DidSizeChange) return;
            var size = AppWindow.Size;
            const int minWidth = 1100;
            const int minHeight = 700;
            int w = Math.Max(size.Width, minWidth);
            int h = Math.Max(size.Height, minHeight);
            if (w != size.Width || h != size.Height)
                AppWindow.Resize(new SizeInt32(w, h));

            UpdateMaximizeGlyph();
        };

        // Sensible starting size + position
        if (AppWindow.Presenter is OverlappedPresenter p2)
        {
            p2.Maximize();
        }
        else
        {
            AppWindow.Resize(new SizeInt32(1440, 900));
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            if (display != null)
            {
                var work = display.WorkArea;
                AppWindow.Move(new PointInt32(
                    work.X + (work.Width - 1440) / 2,
                    work.Y + (work.Height - 900) / 2));
            }
        }

        // Drag region: standard Win32 ReleaseCapture + WM_NCLBUTTONDOWN(HTCAPTION).
        // This gives the OS-native drag feel (snap, multi-monitor) without using
        // SetTitleBar (which forced the entire window into title-bar mode).
        DragRegion.PointerPressed += (s, e) =>
        {
            if (!e.GetCurrentPoint(DragRegion).Properties.IsLeftButtonPressed) return;
            ReleaseCapture();
            SendMessage(_hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        };
        // Double-click toggles maximize, just like a normal title bar.
        DragRegion.DoubleTapped += (_, _) => ToggleMaximize();

        // WebView2 init kicks off in constructor so Chromium is warm by the time
        // the window paints. Loading overlay covers the WebView until ready.
        _ = InitializeWebViewAsync();
        UpdateMaximizeGlyph();
    }

    // ── Title-bar button handlers ────────────────────────────────────

    private void MinBtn_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Minimize();
    }

    private void MaxBtn_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        if (presenter.State == OverlappedPresenterState.Maximized)
            presenter.Restore();
        else
            presenter.Maximize();
        UpdateMaximizeGlyph();
    }

    /// <summary>
    /// Swap the maximize button glyph between the "single square" (will
    /// maximize) and "two stacked squares" (will restore) variants.
    /// </summary>
    private void UpdateMaximizeGlyph()
    {
        if (MaxGlyph == null || RestoreGlyph == null) return;
        bool isMax = AppWindow.Presenter is OverlappedPresenter p
                     && p.State == OverlappedPresenterState.Maximized;
        MaxGlyph.Visibility     = isMax ? Visibility.Collapsed : Visibility.Visible;
        RestoreGlyph.Visibility = isMax ? Visibility.Visible   : Visibility.Collapsed;
    }

    // ── WebView2 init ────────────────────────────────────────────────

    private async System.Threading.Tasks.Task InitializeWebViewAsync()
    {
        try
        {
            // Prefer the prewarmed environment from App.OnLaunched if available —
            // saves the ~1-2s CoreWebView2 cold-start.
            CoreWebView2Environment? env = null;
            if (App.WebView2EnvPrewarm != null)
            {
                SetLoadingStatus("attaching to prewarmed runtime…");
                env = await App.WebView2EnvPrewarm;
            }

            if (env == null)
            {
                SetLoadingStatus("starting chromium…");
                var userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Washmachine", "WebView2");
                Directory.CreateDirectory(userData);

                var envOptions = new CoreWebView2EnvironmentOptions();
                env = await CoreWebView2Environment.CreateWithOptionsAsync(null, userData, envOptions);
            }

            SetLoadingStatus("attaching webview control…");
            await WebView.EnsureCoreWebView2Async(env);

            var core = WebView.CoreWebView2;

            // Lock down chrome
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = System.Diagnostics.Debugger.IsAttached;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;

            // Map WebApp/ to a virtual https origin
            SetLoadingStatus("mounting webapp…");
            var webAppPath = ResolveWebAppPath();
            core.SetVirtualHostNameToFolderMapping(
                "washmachine.local",
                webAppPath,
                CoreWebView2HostResourceAccessKind.DenyCors);

            core.NavigationStarting += (_, e2) =>
            {
                if (!e2.Uri.StartsWith("https://washmachine.local/", StringComparison.OrdinalIgnoreCase)
                    && !e2.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                {
                    e2.Cancel = true;
                }
            };

            core.NavigationCompleted += (_, e2) =>
            {
                if (e2.IsSuccess) HideLoadingOverlay();
                else SetLoadingStatus($"failed to load app: {e2.WebErrorStatus}");
            };

            // Wire JS ↔ C# bridge BEFORE the page loads
            _bridge = new WebShellBridge(core, this);

            SetLoadingStatus("loading washmachine…");
            core.Navigate("https://washmachine.local/app.html");
        }
        catch (Exception ex)
        {
            SetLoadingStatus($"init error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[WebShell] init failed: {ex}");
        }
    }

    private void HideLoadingOverlay()
    {
        if (LoadingOverlay == null) return;
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void SetLoadingStatus(string text)
    {
        try { if (LoadingStatusText != null) LoadingStatusText.Text = text; }
        catch { /* not on UI thread yet */ }
    }

    private static string ResolveWebAppPath()
    {
        var exeDir = AppContext.BaseDirectory;
        var local = Path.Combine(exeDir, "WebApp");
        if (Directory.Exists(local)) return local;

        var probe = exeDir;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(probe); i++)
        {
            var candidate = Path.Combine(probe, "WebApp");
            if (Directory.Exists(candidate)) return candidate;
            probe = Path.GetDirectoryName(probe) ?? string.Empty;
        }

        return local;
    }

    /// <summary>Expose the bridge so the app shell can push status events from C#.</summary>
    public WebShellBridge? Bridge => _bridge;

    /// <summary>Window-control helpers callable from the bridge (when JS requests min/max/close).</summary>
    internal void MinimizeFromBridge()
    {
        if (AppWindow.Presenter is OverlappedPresenter p) p.Minimize();
    }
    internal void MaximizeFromBridge() => ToggleMaximize();
    internal void CloseFromBridge() => Close();
    internal IntPtr Hwnd => _hwnd;
}
