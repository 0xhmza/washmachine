using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Washmachine.Views;

public sealed class ModeSelectionWindow : Window
{
    private readonly Action<AppExperience, ModeSelectionWindow> _start;
    private bool _selectionMade;

    public ModeSelectionWindow(Action<AppExperience, ModeSelectionWindow> start)
    {
        _start = start ?? throw new ArgumentNullException(nameof(start));
        Title = "Washmachine";
        SystemBackdrop = new MicaBackdrop();
        Content = BuildContent();

        AppWindow.Resize(new SizeInt32(760, 520));
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (display != null)
        {
            var area = display.WorkArea;
            AppWindow.Move(new PointInt32(
                area.X + (area.Width - 760) / 2,
                area.Y + (area.Height - 520) / 2));
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }
    }

    private UIElement BuildContent()
    {
        var root = new Grid { Padding = new Thickness(44, 36, 44, 40) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(new TextBlock
        {
            Text = "Washmachine",
            FontSize = 34,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        header.Children.Add(new TextBlock
        {
            Text = "Choose how you want to work",
            FontSize = 15,
            Foreground = App.ThemeBrush("TextFillColorSecondaryBrush"),
        });
        root.Children.Add(header);

        var choices = new Grid
        {
            ColumnSpacing = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (var i = 0; i < 3; i++)
            choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        choices.Children.Add(CreateChoice(AppExperience.Web, "Web app", "Modern WebView interface with the complete visual pipeline.", "\uE774", 0, true));
        choices.Children.Add(CreateChoice(AppExperience.Cli, "CLI", "Interactive terminal and scriptable commands.", "\uE756", 1));
        choices.Children.Add(CreateChoice(AppExperience.WinUi, "WinUI GUI", "Classic native Windows interface.", "\uE737", 2));

        Grid.SetRow(choices, 1);
        root.Children.Add(choices);
        return root;
    }

    private Button CreateChoice(
        AppExperience experience,
        string title,
        string description,
        string glyph,
        int column,
        bool isPrimary = false)
    {
        var content = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 28 });
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        content.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = App.ThemeBrush("TextFillColorSecondaryBrush"),
        });

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(18),
            MinHeight = 180,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, $"Launch{experience}");
        if (isPrimary && Application.Current.Resources["AccentButtonStyle"] is Style accentStyle)
            button.Style = accentStyle;
        button.Click += (_, _) => Select(experience);
        Grid.SetColumn(button, column);
        return button;
    }

    private void Select(AppExperience experience)
    {
        if (_selectionMade)
            return;
        _selectionMade = true;
        _start(experience, this);
    }
}
