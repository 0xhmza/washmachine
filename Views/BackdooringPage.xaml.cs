using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Animations;

namespace Washmachine.Views;

public partial class BackdooringPage : Page
{
    public BackdooringPage()
    {
        InitializeComponent();
        Loaded += BackdooringPage_Loaded;
    }

    private void BackdooringPage_Loaded(object sender, RoutedEventArgs e)
    {
        TransitionAnimationProvider.ApplyTransition(Root, Transition.FadeInWithSlide, 220);
    }
}
