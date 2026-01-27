using System.Windows;
using Washmachine.Views;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using Wpf.Ui.Controls;

namespace Washmachine;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        mainNavigationView.ApplyTemplate();
        if (mainNavigationView.Template != null)
        {
            var presenter = mainNavigationView.Template.FindName(
                "NavigationViewContentPresenter",
                mainNavigationView) as Wpf.Ui.Controls.NavigationViewContentPresenter;
            if (presenter != null)
            {
                presenter.SetValue(
                    Wpf.Ui.Controls.NavigationViewContentPresenter.IsDynamicScrollViewerEnabledProperty,
                    true);
            }
        }
        mainNavigationView.Navigate(typeof(MainPage));
    }
}
