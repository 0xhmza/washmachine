using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Animations;

namespace Washmachine.Views;

public partial class PackingPage : Page
{
    public PackingPage()
    {
        InitializeComponent();
        Loaded += PackingPage_Loaded;
    }

    private void PackingPage_Loaded(object sender, RoutedEventArgs e)
    {
        TransitionAnimationProvider.ApplyTransition(Root, Transition.FadeInWithSlide, 220);
    }
}
