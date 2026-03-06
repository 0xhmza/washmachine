using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Washmachine.Views;
using Windows.Graphics;

namespace Washmachine;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        Title = "Washmachine - Loader Builder";

        AppWindow.Resize(new SizeInt32(980, 820));
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        AppWindow.Move(new PointInt32(
            work.X + (work.Width - 980) / 2,
            work.Y + (work.Height - 820) / 2));

        ContentFrame.Navigate(typeof(MainPage));
        mainNavigationView.SelectedItem = mainNavigationView.MenuItems[0];
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            return;

        var pageType = tag switch
        {
            "MainPage" => typeof(MainPage),
            "PackingPage" => typeof(PackingPage),
            "BackdooringPage" => typeof(BackdooringPage),
            _ => (Type?)null
        };

        if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            ContentFrame.Navigate(pageType);
    }
}
