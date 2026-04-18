using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Washmachine.Views;
using Windows.Graphics;

namespace Washmachine;

public sealed partial class MainWindow : Window
{
    public Frame GetContentFrame() => ContentFrame;

    public MainWindow()
    {
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        Title = "Washmachine - Loader Builder";

        // Prevent the window from shrinking below a usable size (fixes double-click
        // on the NavigationView toggle that would collapse the window).
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
            const int minWidth = 700;
            const int minHeight = 520;

            int width = Math.Max(size.Width, minWidth);
            int height = Math.Max(size.Height, minHeight);
            if (width != size.Width || height != size.Height)
            {
                AppWindow.Resize(new SizeInt32(width, height));
            }
        };

        AppWindow.Resize(new SizeInt32(980, 820));
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (display != null)
        {
            var work = display.WorkArea;
            AppWindow.Move(new PointInt32(
                work.X + (work.Width - 980) / 2,
                work.Y + (work.Height - 820) / 2));
        }

        ContentFrame.Navigate(typeof(MainPage));
        mainNavigationView.SelectedItem = mainNavigationView.MenuItems[0];
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            return;

        var pageType = tag switch
        {
            "MainPage" => typeof(MainPage),
            "CompilePage" => typeof(CompilePage),
            "PackingPage" => typeof(PackingPage),
            "FinalizePage" => typeof(FinalizePage),
            "BackdooringPage" => typeof(BackdooringPage),
            _ => (Type?)null
        };

        if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            ContentFrame.Navigate(pageType);
    }
}
