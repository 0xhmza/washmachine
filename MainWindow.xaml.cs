using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Washmachine.Views;
using Windows.Graphics;
using Windows.UI;

namespace Washmachine;

public sealed partial class MainWindow : Window
{
    public Frame GetContentFrame() => ContentFrame;

    public void NavigateToPipeline()
    {
        if (ContentFrame.CurrentSourcePageType != typeof(PipelinePage))
            ContentFrame.Navigate(typeof(PipelinePage));

        foreach (var item in mainNavigationView.FooterMenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag as string == "PipelinePage")
            {
                mainNavigationView.SelectedItem = nvi;
                break;
            }
        }
    }

    public void NavigateToBackdooringPage()
    {
        foreach (var item in mainNavigationView.MenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag as string == "BackdooringPage")
            {
                mainNavigationView.SelectedItem = nvi;
                if (ContentFrame.CurrentSourcePageType != typeof(BackdooringPage))
                    ContentFrame.Navigate(typeof(BackdooringPage));
                break;
            }
        }
    }

    public void NavigateToSettingsPage()
    {
        mainNavigationView.SelectedItem = mainNavigationView.SettingsItem;
        if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
            ContentFrame.Navigate(typeof(SettingsPage));
    }

    /// <summary>
    /// Highlight a pipeline stage. Tag values: "src", "sgn", "enc", "tpl", "cmp", "bd", "pk", "fn".
    /// Pages call this from OnNavigatedTo to keep the pipeline rail in sync.
    /// </summary>
    public void SetPipelineActiveStage(string tag)
    {
        Grid[] stages = { PipeSrc, PipeSgn, PipeEnc, PipeTpl, PipeCmp, PipeBd, PipePk, PipeFn };
        foreach (var s in stages)
        {
            bool isActive = (s.Tag as string) == tag;
            s.Background = isActive
                ? (SolidColorBrush)App.Current.Resources["N3Brush"]
                : new SolidColorBrush(Colors.Transparent);
            if (s.Children.Count > 0 && s.Children[0] is StackPanel sp
                && sp.Children.Count > 0 && sp.Children[0] is StackPanel topRow
                && topRow.Children.Count >= 2)
            {
                if (topRow.Children[0] is TextBlock numBlock)
                {
                    numBlock.Foreground = isActive
                        ? (SolidColorBrush)App.Current.Resources["AccBrush"]
                        : (SolidColorBrush)App.Current.Resources["N7Brush"];
                }
                if (topRow.Children[1] is TextBlock nameBlock)
                {
                    nameBlock.Foreground = isActive
                        ? (SolidColorBrush)App.Current.Resources["N10Brush"]
                        : (SolidColorBrush)App.Current.Resources["N9Brush"];
                }
            }
        }
    }

    /// <summary>
    /// Update the breadcrumb display in the title bar.
    /// </summary>
    public void SetCrumbs(string root, string current)
    {
        CrumbRoot.Text = root;
        CrumbCurrent.Text = current;
    }

    /// <summary>
    /// Update a single key/value pair in the status bar.
    /// </summary>
    public void SetStatusKey(string label, string value)
    {
        StatusKey1Label.Text = label;
        StatusKey1Value.Text = value;
    }

    /// <summary>
    /// Update the SHA hash chip in the status bar.
    /// </summary>
    public void SetStatusSha(string sha)
    {
        StatusSha.Text = string.IsNullOrEmpty(sha) ? "—" : sha;
    }

    /// <summary>
    /// Update the session ID chip in the status bar.
    /// </summary>
    public void SetStatusSession(string session)
    {
        StatusSession.Text = string.IsNullOrEmpty(session) ? "—" : session;
    }

    /// <summary>
    /// Update the meta label under a specific pipeline stage tag.
    /// </summary>
    public void SetPipelineStageMeta(string tag, string meta)
    {
        Grid? target = tag switch
        {
            "src" => PipeSrc,
            "sgn" => PipeSgn,
            "enc" => PipeEnc,
            "tpl" => PipeTpl,
            "cmp" => PipeCmp,
            "bd"  => PipeBd,
            "pk"  => PipePk,
            "fn"  => PipeFn,
            _     => null
        };
        if (target == null) return;
        if (target.Children.Count == 0 || target.Children[0] is not StackPanel sp) return;
        if (sp.Children.Count < 2 || sp.Children[1] is not TextBlock metaBlock) return;
        metaBlock.Text = meta;
    }

    public MainWindow()
    {
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        Title = "Washmachine - Loader Builder";

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsMinimizable = true;
            presenter.IsMaximizable = true;
            presenter.IsResizable = true;
        }
        AppWindow.Changed += (_, e) =>
        {
            if (!e.DidSizeChange)
                return;

            var size = AppWindow.Size;
            const int minWidth = 980;
            const int minHeight = 620;

            int width = System.Math.Max(size.Width, minWidth);
            int height = System.Math.Max(size.Height, minHeight);
            if (width != size.Width || height != size.Height)
            {
                AppWindow.Resize(new SizeInt32(width, height));
            }
        };

        if (presenter != null)
        {
            presenter.Maximize();
        }
        else
        {
            AppWindow.Resize(new SizeInt32(1280, 860));
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            if (display != null)
            {
                var work = display.WorkArea;
                AppWindow.Move(new PointInt32(
                    work.X + (work.Width - 1280) / 2,
                    work.Y + (work.Height - 860) / 2));
            }
        }

        ContentFrame.Navigate(typeof(MainPage));
        foreach (var item in mainNavigationView.MenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag as string == "MainPage")
            {
                mainNavigationView.SelectedItem = nvi;
                break;
            }
        }
        SetPipelineActiveStage("src");
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                ContentFrame.Navigate(typeof(SettingsPage));
            SetCrumbs("settings", "preferences");
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            return;

        System.Type? pageType = tag switch
        {
            "MainPage" => typeof(MainPage),
            "CompilePage" => typeof(CompilePage),
            "PackingPage" => typeof(PackingPage),
            "FinalizePage" => typeof(FinalizePage),
            "BackdooringPage" => typeof(BackdooringPage),
            "PipelinePage" => typeof(PipelinePage),
            "PayloadHistoryPage" => typeof(PayloadHistoryPage),
            _ => null
        };

        // Update breadcrumb + pipeline highlight per page
        switch (tag)
        {
            case "MainPage": SetCrumbs("build", "source"); SetPipelineActiveStage("src"); break;
            case "CompilePage": SetCrumbs("build", "compile"); SetPipelineActiveStage("cmp"); break;
            case "BackdooringPage": SetCrumbs("build", "backdoor"); SetPipelineActiveStage("bd"); break;
            case "PackingPage": SetCrumbs("build", "pack"); SetPipelineActiveStage("pk"); break;
            case "FinalizePage": SetCrumbs("build", "finalize"); SetPipelineActiveStage("fn"); break;
            case "PipelinePage": SetCrumbs("build", "pipeline"); SetPipelineActiveStage(""); break;
            case "PayloadHistoryPage": SetCrumbs("history", "sessions"); SetPipelineActiveStage(""); break;
        }

        if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            ContentFrame.Navigate(pageType);
    }

    private async void RunBuildButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to CompilePage so the log is visible, then trigger its build flow.
        foreach (var item in mainNavigationView.MenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag as string == "CompilePage")
            {
                mainNavigationView.SelectedItem = nvi;
                break;
            }
        }

        // Give the navigation a tick to instantiate CompilePage.Instance.
        for (int i = 0; i < 30 && Views.CompilePage.Instance == null; i++)
        {
            await System.Threading.Tasks.Task.Delay(10);
        }

        Views.CompilePage.Instance?.TriggerBuildFromShell();
    }
}
