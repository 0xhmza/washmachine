using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Washmachine.Models;

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
public sealed class TemplateOptionsDialog : ContentDialog
{
    private const int DefaultInputWidth = 260;
    private const int DefaultSelectorWidth = 360;
    private const int MinSelectorHeight = 90;
    private const int MaxSelectorHeight = 240;

    private readonly TemplateOptionsState _initialState;
    private readonly List<FieldBinding> _bindings = new();
    private readonly List<SectionUi> _sections = new();
    private readonly Func<string, Task>? _infoAction;
    private readonly StackPanel _sectionsHost;
    private readonly TextBlock _emptyLabel;

    public TemplateOptionsDialog(
        CodeTemplateDefinition template,
        IReadOnlyList<CodeSnippetSection> sections,
        TemplateOptionsState? existingState,
        Func<string, Task>? infoAction = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(sections);

        _initialState = existingState?.Clone() ?? new TemplateOptionsState();
        _infoAction = infoAction;

        Title = $"Template Options - {template.Display}";
        PrimaryButtonText = "OK";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        var headerPanel = BuildHeaderPanel();
        _sectionsHost = BuildSectionsHost();
        _emptyLabel = new TextBlock
        {
            Text = "No options are available for this template.",
            Opacity = 0.7,
            Margin = new Thickness(8, 8, 8, 0)
        };

        var scrollViewer = new ScrollViewer
        {
            Content = _sectionsHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            }
        };

        root.Children.Add(headerPanel);
        Grid.SetRow(scrollViewer, 1);
        root.Children.Add(scrollViewer);

        Content = root;

        PrimaryButtonClick += (_, _) => ResultState = BuildResultState();

        BuildSections(sections);
    }

    public TemplateOptionsState ResultState { get; private set; } = new();

    public async Task<TemplateOptionsState?> ShowDialogAsync(XamlRoot xamlRoot)
    {
        XamlRoot = xamlRoot;
        var result = await ShowAsync();
        return result == ContentDialogResult.Primary ? ResultState : null;
    }

    private UIElement BuildHeaderPanel()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(8, 8, 8, 4)
        };

        var expandButton = new Button
        {
            Content = "Expand All",
            MinWidth = 110
        };

        var collapseButton = new Button
        {
            Content = "Collapse All",
            MinWidth = 110
        };

        expandButton.Click += (_, _) => SetAllSectionsExpanded(true);
        collapseButton.Click += (_, _) => SetAllSectionsExpanded(false);

        panel.Children.Add(expandButton);
        panel.Children.Add(collapseButton);

        return panel;
    }

    private StackPanel BuildSectionsHost()
    {
        return new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(8, 0, 8, 8)
        };
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
            IsExpanded = true,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        headerRow.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(section.Display) ? "Section" : section.Display,
            FontWeight = Windows.UI.Text.FontWeights.SemiBold
        });

        expander.Header = headerRow;

        var body = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(8, 4, 8, 8)
        };

        AddInputs(body, section, SnippetInputPlacement.BeforeSelector);
        AddSnippetSelector(body, section);
        AddInputs(body, section, SnippetInputPlacement.AfterSelector);

        if (body.Children.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = "No options available.",
                Opacity = 0.7
            });
        }

        expander.Content = body;

        var ui = new SectionUi(section, expander, body);
        return ui;
    }

    private void AddInputs(StackPanel host, CodeSnippetSection section, SnippetInputPlacement placement)
    {
        foreach (var input in section.Inputs.Where(i => i != null && i.Placement == placement))
        {
            AddInputRow(host, input);
        }
    }

    private void AddInputRow(StackPanel host, CodeSnippetInput input)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        string labelText = BuildInputLabel(input);
        if (!string.IsNullOrWhiteSpace(labelText))
        {
            row.Children.Add(new TextBlock
            {
                Text = labelText,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        string key = input.Id ?? string.Empty;
        var textBox = new TextBox
        {
            Width = input.Width.HasValue && input.Width.Value > 0 ? input.Width.Value : DefaultInputWidth
        };

        if (!string.IsNullOrWhiteSpace(key))
            textBox.Name = key;

        if (!string.IsNullOrWhiteSpace(input.Placeholder))
            textBox.PlaceholderText = input.Placeholder;

        textBox.Text = GetInitialTextValue(key);
        row.Children.Add(textBox);

        if (!string.IsNullOrWhiteSpace(input.InfoAction))
        {
            var infoButton = new Button
            {
                Content = string.IsNullOrWhiteSpace(input.InfoButtonLabel) ? "Info" : input.InfoButtonLabel
            };

            infoButton.Click += async (_, _) =>
            {
                if (_infoAction != null)
                    await _infoAction(input.InfoAction);
            };

            row.Children.Add(infoButton);
        }

        if (!string.IsNullOrWhiteSpace(key))
            _bindings.Add(new FieldBinding(key, FieldKind.Text, textBox));

        host.Children.Add(row);
    }

    private void AddSnippetSelector(StackPanel host, CodeSnippetSection section)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        row.Children.Add(new TextBlock
        {
            Text = section.AllowMultiple ? "Snippets" : "Snippet",
            VerticalAlignment = VerticalAlignment.Top
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
                    Opacity = 0.7,
                    VerticalAlignment = VerticalAlignment.Center
                });
                host.Children.Add(row);
                return;
            }

            var list = new ListView
            {
                Width = DefaultSelectorWidth,
                SelectionMode = ListViewSelectionMode.Multiple,
                IsMultiSelectCheckBoxEnabled = true,
                ItemsSource = options,
                DisplayMemberPath = nameof(OptionItem.Display)
            };

            int height = Math.Clamp(options.Count * 32 + 12, MinSelectorHeight, MaxSelectorHeight);
            list.Height = height;

            var selectedIds = new HashSet<string>(resolved, StringComparer.OrdinalIgnoreCase);
            foreach (var option in options)
            {
                if (!string.IsNullOrWhiteSpace(option.Id) && selectedIds.Contains(option.Id))
                    list.SelectedItems.Add(option);
            }

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
                ItemsSource = options,
                DisplayMemberPath = nameof(OptionItem.Display),
                SelectedValuePath = nameof(OptionItem.Id),
                IsEditable = false
            };

            SelectComboItem(combo, options, resolved);

            row.Children.Add(combo);
            _bindings.Add(new FieldBinding(key, FieldKind.SingleSelect, combo));
        }

        host.Children.Add(row);
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
                            id = combo?.SelectedValue as string ?? combo?.Text ?? string.Empty;

                        id = id.Trim();
                        if (!string.IsNullOrWhiteSpace(id))
                            state.ComboValues[binding.Key] = id;
                        break;
                    }
                case FieldKind.MultiSelect:
                    {
                        if (binding.Control is not ListView list)
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
                    combo.SelectedItem = options[i];
                    return;
                }
            }
        }

        OptionItem? noneItem = options.FirstOrDefault(option => string.IsNullOrWhiteSpace(option.Id));
        combo.SelectedItem = noneItem ?? options.FirstOrDefault();
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

    private void SetAllSectionsExpanded(bool expanded)
    {
        foreach (var section in _sections)
        {
            section.Container.IsExpanded = expanded;
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
