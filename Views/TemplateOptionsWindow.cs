using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Washmachine.Models;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using TitleBar = Wpf.Ui.Controls.TitleBar;

namespace Washmachine.Views;

/// <summary>
/// Captures template option selections so they can be reused across dialog sessions.
/// </summary>
public sealed class TemplateOptionsState
{
    public Dictionary<string, string> TextValues { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ComboValues { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> ListValues { get; } = new(StringComparer.Ordinal);

    public TemplateOptionsState Clone()
    {
        var clone = new TemplateOptionsState();
        foreach (var entry in TextValues)
            clone.TextValues[entry.Key] = entry.Value;
        foreach (var entry in ComboValues)
            clone.ComboValues[entry.Key] = entry.Value;
        foreach (var entry in ListValues)
            clone.ListValues[entry.Key] = entry.Value == null ? new List<string>() : new List<string>(entry.Value);
        return clone;
    }
}

/// <summary>
/// Renders template-specific inputs and snippet selectors in a scrollable dialog.
/// </summary>
public sealed class TemplateOptionsWindow : FluentWindow
{
    private const int DefaultInputWidth = 260;
    private const int DefaultSelectorWidth = 360;
    private const int MinSelectorHeight = 90;
    private const int MaxSelectorHeight = 240;

    private readonly TemplateOptionsState _initialState;
    private readonly List<FieldBinding> _bindings = new();
    private readonly List<SectionUi> _sections = new();
    private readonly Action<string>? _infoAction;
    private readonly StackPanel _sectionsHost;
    private readonly TextBlock _emptyLabel;

    public TemplateOptionsWindow(
        CodeTemplateDefinition template,
        IReadOnlyList<CodeSnippetSection> sections,
        TemplateOptionsState? existingState,
        Action<string>? infoAction = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(sections);

        _initialState = existingState?.Clone() ?? new TemplateOptionsState();
        _infoAction = infoAction;

        Title = $"Template Options - {template.Display}";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Width = 900;
        Height = 620;
        MinWidth = 720;
        MinHeight = 480;
        ExtendsContentIntoTitleBar = true;
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = new TitleBar
        {
            Title = Title
        };
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var headerPanel = BuildHeaderPanel();
        Grid.SetRow(headerPanel, 1);
        root.Children.Add(headerPanel);

        _sectionsHost = new StackPanel { Orientation = Orientation.Vertical };
        var scrollViewer = new ScrollViewer
        {
            Content = _sectionsHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 4, 0, 8)
        };
        Grid.SetRow(scrollViewer, 2);
        root.Children.Add(scrollViewer);

        _emptyLabel = new TextBlock
        {
            Text = "No options are available for this template.",
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(12, 12, 12, 0)
        };

        var bottomPanel = BuildBottomPanel();
        Grid.SetRow(bottomPanel, 3);
        root.Children.Add(bottomPanel);

        Content = root;

        BuildSections(sections);
    }

    public TemplateOptionsState ResultState { get; private set; } = new();

    private UIElement BuildHeaderPanel()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 10, 12, 4)
        };

        var expandButton = new Button
        {
            Content = "Expand All",
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = 90
        };
        expandButton.Click += (_, _) => SetAllSectionsCollapsed(false);

        var collapseButton = new Button
        {
            Content = "Collapse All",
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = 90
        };
        collapseButton.Click += (_, _) => SetAllSectionsCollapsed(true);

        panel.Children.Add(expandButton);
        panel.Children.Add(collapseButton);

        return panel;
    }

    private UIElement BuildBottomPanel()
    {
        var panel = new DockPanel
        {
            Margin = new Thickness(12, 0, 12, 10),
            LastChildFill = false
        };

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 90,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        okButton.Click += (_, _) =>
        {
            ResultState = BuildResultState();
            DialogResult = true;
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 90,
            IsCancel = true
        };
        cancelButton.Click += (_, _) => DialogResult = false;

        DockPanel.SetDock(cancelButton, Dock.Right);
        DockPanel.SetDock(okButton, Dock.Right);
        panel.Children.Add(cancelButton);
        panel.Children.Add(okButton);

        return panel;
    }

    private void BuildSections(IReadOnlyList<CodeSnippetSection> sections)
    {
        _sectionsHost.Children.Clear();
        _sections.Clear();
        _bindings.Clear();

        var list = sections.Where(s => s != null).ToList();
        if (list.Count == 0)
        {
            _sectionsHost.Children.Add(_emptyLabel);
            return;
        }

        foreach (var section in list)
        {
            var ui = CreateSection(section);
            _sections.Add(ui);
            _sectionsHost.Children.Add(ui.Container);
        }
    }

    private SectionUi CreateSection(CodeSnippetSection section)
    {
        var expander = new Expander
        {
            Header = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(section.Display) ? "Section" : section.Display,
                FontWeight = FontWeights.SemiBold
            },
            IsExpanded = true,
            Margin = new Thickness(12, 8, 12, 0)
        };

        var body = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(12, 6, 12, 6)
        };

        AddInputs(body, section, SnippetInputPlacement.BeforeSelector);
        AddSnippetSelector(body, section);
        AddInputs(body, section, SnippetInputPlacement.AfterSelector);

        if (body.Children.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = "No options available.",
                Foreground = SystemColors.GrayTextBrush,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        expander.Content = body;
        return new SectionUi(section, expander, body);
    }

    private void AddInputs(Panel host, CodeSnippetSection section, SnippetInputPlacement placement)
    {
        foreach (var input in section.Inputs.Where(i => i != null && i.Placement == placement))
        {
            AddInputRow(host, input);
        }
    }

    private void AddInputRow(Panel host, CodeSnippetInput input)
    {
        var row = CreateRow();

        string labelText = BuildInputLabel(input);
        if (!string.IsNullOrWhiteSpace(labelText))
        {
            row.Children.Add(new TextBlock
            {
                Text = labelText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
        }

        string key = input.Id ?? string.Empty;
        var textBox = new TextBox
        {
            Width = input.Width.HasValue && input.Width.Value > 0 ? input.Width.Value : DefaultInputWidth,
            Margin = new Thickness(0, 0, 8, 0),
            Text = GetInitialTextValue(key)
        };

        if (!string.IsNullOrWhiteSpace(input.Placeholder))
            textBox.ToolTip = input.Placeholder;

        row.Children.Add(textBox);

        if (!string.IsNullOrWhiteSpace(input.InfoAction))
        {
            var infoButton = new Button
            {
                Content = string.IsNullOrWhiteSpace(input.InfoButtonLabel) ? "Info" : input.InfoButtonLabel,
                MinWidth = 64
            };
            infoButton.Click += (_, _) => _infoAction?.Invoke(input.InfoAction);
            row.Children.Add(infoButton);
        }

        if (!string.IsNullOrWhiteSpace(key))
            _bindings.Add(new FieldBinding(key, FieldKind.Text, textBox));

        host.Children.Add(row);
    }

    private void AddSnippetSelector(Panel host, CodeSnippetSection section)
    {
        var row = CreateRow();
        row.Children.Add(new TextBlock
        {
            Text = section.AllowMultiple ? "Snippets" : "Snippet",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        if (section.AllowMultiple)
        {
            string key = SnippetControlNaming.GetListName(section, 0);
            var storedValues = GetInitialListValues(key);
            var resolved = ResolveStoredSelections(storedValues, section.Items);
            var options = BuildOptionItems(section.Items, resolved, includeNone: false);

            if (options.Count == 0)
            {
                row.Children.Add(new TextBlock
                {
                    Text = "No snippets defined.",
                    Foreground = SystemColors.GrayTextBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });
                host.Children.Add(row);
                return;
            }

            var list = new ListBox
            {
                Width = DefaultSelectorWidth,
                SelectionMode = SelectionMode.Multiple
            };

            var itemCount = Math.Max(1, options.Count);
            int height = Math.Clamp(itemCount * 22, MinSelectorHeight, MaxSelectorHeight);
            list.Height = height;

            foreach (var option in options)
            {
                list.Items.Add(option);
            }

            var selectedIds = new HashSet<string>(resolved, StringComparer.OrdinalIgnoreCase);
            foreach (var option in options)
            {
                if (!string.IsNullOrWhiteSpace(option.Id) && selectedIds.Contains(option.Id))
                {
                    list.SelectedItems.Add(option);
                }
            }

            list.ItemTemplate = BuildCheckBoxTemplate();

            row.Children.Add(list);
            _bindings.Add(new FieldBinding(key, FieldKind.MultiSelect, list));
        }
        else
        {
            string key = SnippetControlNaming.GetComboName(section, 0);
            string storedValue = GetInitialComboValue(key);
            string resolved = ResolveStoredSingle(storedValue, section.Items);
            var options = BuildOptionItems(section.Items, string.IsNullOrWhiteSpace(resolved) ? Array.Empty<string>() : new[] { resolved }, includeNone: true);

            var combo = new ComboBox
            {
                Width = DefaultSelectorWidth,
                DisplayMemberPath = nameof(OptionItem.Display),
                SelectedValuePath = nameof(OptionItem.Id),
                IsEditable = false
            };

            foreach (var option in options)
            {
                combo.Items.Add(option);
            }

            SelectComboItem(combo, options, resolved);

            row.Children.Add(combo);
            _bindings.Add(new FieldBinding(key, FieldKind.SingleSelect, combo));
        }

        host.Children.Add(row);
    }

    private static DataTemplate BuildCheckBoxTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(CheckBox));
        factory.SetBinding(CheckBox.ContentProperty, new Binding(nameof(OptionItem.Display)));
        factory.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsSelected")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1),
            Mode = BindingMode.TwoWay
        });
        factory.SetValue(CheckBox.MarginProperty, new Thickness(2, 1, 2, 1));

        return new DataTemplate { VisualTree = factory };
    }

    private static StackPanel CreateRow()
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0)
        };
    }

    private TemplateOptionsState BuildResultState()
    {
        var state = new TemplateOptionsState();

        foreach (var binding in _bindings)
        {
            switch (binding.Kind)
            {
                case FieldKind.Text:
                    {
                        var text = (binding.Control as TextBox)?.Text ?? string.Empty;
                        text = text.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                            state.TextValues[binding.Key] = text;
                        break;
                    }
                case FieldKind.SingleSelect:
                    {
                        var combo = binding.Control as ComboBox;
                        var selected = combo?.SelectedItem as OptionItem;
                        var id = selected?.Id ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(id))
                            id = combo?.SelectedValue?.ToString() ?? combo?.Text ?? string.Empty;

                        id = id.Trim();
                        if (!string.IsNullOrWhiteSpace(id))
                            state.ComboValues[binding.Key] = id;
                        break;
                    }
                case FieldKind.MultiSelect:
                    {
                        var list = binding.Control as ListBox;
                        if (list == null)
                            break;

                        var selections = new List<string>();
                        foreach (var item in list.SelectedItems)
                        {
                            if (item is OptionItem option && !string.IsNullOrWhiteSpace(option.Id))
                            {
                                selections.Add(option.Id);
                            }
                            else if (item != null)
                            {
                                var raw = item.ToString() ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(raw))
                                    selections.Add(raw);
                            }
                        }

                        if (selections.Count > 0)
                            state.ListValues[binding.Key] = selections;
                        break;
                    }
            }
        }

        return state;
    }

    private string GetInitialTextValue(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        return _initialState.TextValues.TryGetValue(key, out var raw) ? raw ?? string.Empty : string.Empty;
    }

    private string GetInitialComboValue(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        return _initialState.ComboValues.TryGetValue(key, out var raw) ? raw ?? string.Empty : string.Empty;
    }

    private IReadOnlyList<string> GetInitialListValues(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Array.Empty<string>();

        return _initialState.ListValues.TryGetValue(key, out var raw) && raw != null
            ? raw
            : Array.Empty<string>();
    }

    private static void SelectComboItem(ComboBox combo, IReadOnlyList<OptionItem> options, string selectedId)
    {
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].Id, selectedId, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        int noneIndex = -1;
        for (int i = 0; i < options.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(options[i].Id))
            {
                noneIndex = i;
                break;
            }
        }

        combo.SelectedIndex = noneIndex >= 0 ? noneIndex : (options.Count > 0 ? 0 : -1);
    }

    private static List<OptionItem> BuildOptionItems(
        IReadOnlyList<CodeSnippetItem> items,
        IEnumerable<string>? extraIds,
        bool includeNone)
    {
        var options = new List<OptionItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (includeNone)
        {
            options.Add(OptionItem.None);
            seen.Add(string.Empty);
        }

        foreach (var item in items)
        {
            if (item == null)
                continue;

            var id = item.Id ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (seen.Add(id))
                options.Add(new OptionItem(id, item.Display));
        }

        if (extraIds != null)
        {
            foreach (var raw in extraIds)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                if (seen.Add(raw))
                    options.Add(new OptionItem(raw, $"{raw} (custom)"));
            }
        }

        return options;
    }

    private static string ResolveStoredSingle(string raw, IReadOnlyList<CodeSnippetItem> items)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var byId = new Dictionary<string, CodeSnippetItem>(StringComparer.OrdinalIgnoreCase);
        var byDisplay = new Dictionary<string, CodeSnippetItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id))
                continue;
            byId[item.Id] = item;
            if (!string.IsNullOrWhiteSpace(item.Display))
                byDisplay[item.Display] = item;
        }

        if (byId.TryGetValue(raw, out var matched))
            return matched.Id;

        if (byDisplay.TryGetValue(raw, out matched))
            return matched.Id;

        return raw;
    }

    private static IReadOnlyList<string> ResolveStoredSelections(IEnumerable<string> rawValues, IReadOnlyList<CodeSnippetItem> items)
    {
        var selections = new List<string>();
        if (rawValues == null)
            return selections;

        var byId = new Dictionary<string, CodeSnippetItem>(StringComparer.OrdinalIgnoreCase);
        var byDisplay = new Dictionary<string, CodeSnippetItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id))
                continue;

            byId[item.Id] = item;
            if (!string.IsNullOrWhiteSpace(item.Display))
                byDisplay[item.Display] = item;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in rawValues)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string resolved;
            if (byId.TryGetValue(raw, out var matched))
                resolved = matched.Id;
            else if (byDisplay.TryGetValue(raw, out matched))
                resolved = matched.Id;
            else
                resolved = raw;

            if (seen.Add(resolved))
                selections.Add(resolved);
        }

        return selections;
    }

    private void SetAllSectionsCollapsed(bool collapsed)
    {
        foreach (var section in _sections)
        {
            section.Container.IsExpanded = !collapsed;
        }
    }

    private static string BuildInputLabel(CodeSnippetInput input)
    {
        if (input == null)
            return string.Empty;

        string label = string.IsNullOrWhiteSpace(input.Label) ? input.Id : input.Label;
        if (input.Required && !string.IsNullOrWhiteSpace(label))
            label += " *";
        return label;
    }

    private sealed class SectionUi
    {
        public SectionUi(CodeSnippetSection section, Expander container, StackPanel body)
        {
            Section = section;
            Container = container;
            Body = body;
        }

        public CodeSnippetSection Section { get; }
        public Expander Container { get; }
        public StackPanel Body { get; }
    }

    private sealed class FieldBinding
    {
        public FieldBinding(string key, FieldKind kind, FrameworkElement control)
        {
            Key = key ?? string.Empty;
            Kind = kind;
            Control = control ?? throw new ArgumentNullException(nameof(control));
        }

        public string Key { get; }
        public FieldKind Kind { get; }
        public FrameworkElement Control { get; }
    }

    private sealed class OptionItem
    {
        public static OptionItem None { get; } = new(string.Empty, "None");

        public OptionItem(string id, string display)
        {
            Id = id ?? string.Empty;
            Display = string.IsNullOrWhiteSpace(display) ? Id : display;
        }

        public string Id { get; }
        public string Display { get; }
        public override string ToString() => Display;
    }

    private enum FieldKind
    {
        Text,
        SingleSelect,
        MultiSelect
    }
}

