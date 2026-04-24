using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Washmachine.Models;
using Washmachine.Services;
using Windows.Graphics;

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
public sealed class TemplateOptionsWindow
{
    private const int DefaultInputWidth = 260;
    private const int DefaultSelectorWidth = 360;
    private const int MinSelectorHeight = 90;
    private const int MaxSelectorHeight = 240;

    private readonly Window _window;
    private readonly TaskCompletionSource<TemplateOptionsState?> _tcs;
    private readonly TemplateOptionsState _initialState;
    private readonly List<FieldBinding> _bindings = new();
    private readonly List<SectionUi> _sections = new();
    private readonly Action<string>? _infoAction;
    private readonly StackPanel _sectionsHost;
    private readonly TextBlock _emptyLabel;

    private TemplateOptionsWindow(
        CodeTemplateDefinition template,
        IReadOnlyList<CodeSnippetSection> sections,
        TemplateOptionsState? existingState,
        Action<string>? infoAction,
        TaskCompletionSource<TemplateOptionsState?> tcs)
    {
        _tcs = tcs;
        _initialState = existingState?.Clone() ?? new TemplateOptionsState();
        _infoAction = infoAction;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: title bar
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: header buttons
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: content
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: bottom buttons

        // Custom title bar (drag region + title text)
        var titleBar = new Grid { Height = 48 };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var titleIcon = new FontIcon
        {
            Glyph = "\uE943",
            FontSize = 16,
            Margin = new Thickness(16, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var titleText = new TextBlock
        {
            Text = $"Template Options — {template.Display}",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14
        };
        Grid.SetColumn(titleIcon, 0);
        Grid.SetColumn(titleText, 1);
        titleBar.Children.Add(titleIcon);
        titleBar.Children.Add(titleText);
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
            Foreground = App.ThemeBrush("TextFillColorSecondaryBrush"),
            Margin = new Thickness(12, 12, 12, 0)
        };

        var bottomPanel = BuildBottomPanel();
        Grid.SetRow(bottomPanel, 3);
        root.Children.Add(bottomPanel);

        _window = new Window
        {
            Title = $"Template Options — {template.Display}",
            Content = root,
            SystemBackdrop = new DesktopAcrylicBackdrop()
        };
        _window.ExtendsContentIntoTitleBar = true;
        _window.SetTitleBar(titleBar);

        _window.Closed += OnWindowClosedCancel;

        BuildSections(sections);
    }

    public static Task<TemplateOptionsState?> ShowAsync(
        nint ownerHandle,
        CodeTemplateDefinition template,
        IReadOnlyList<CodeSnippetSection> sections,
        TemplateOptionsState? existingState,
        Action<string>? infoAction = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(sections);

        var tcs = new TaskCompletionSource<TemplateOptionsState?>();
        var dlg = new TemplateOptionsWindow(template, sections, existingState, infoAction, tcs);

        dlg._window.AppWindow.Resize(new SizeInt32(900, 620));
        var display = DisplayArea.GetFromWindowId(dlg._window.AppWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        dlg._window.AppWindow.Move(new PointInt32(
            work.X + (work.Width - 900) / 2,
            work.Y + (work.Height - 620) / 2));

        // Make modal: disable the owner window until this window closes.
        if (ownerHandle != 0)
        {
            EnableWindow(ownerHandle, false);
            dlg._window.Closed += (_, _) => EnableWindow(ownerHandle, true);
        }

        dlg._window.Activate();
        return tcs.Task;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnableWindow(nint hWnd, bool bEnable);

    private FrameworkElement BuildHeaderPanel()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 10, 12, 4),
            Spacing = 8
        };

        var expandButton = new Button { Content = "Expand all", MinWidth = 110 };
        expandButton.Click += (_, _) => SetAllSectionsCollapsed(false);

        var collapseButton = new Button { Content = "Collapse all", MinWidth = 110 };
        collapseButton.Click += (_, _) => SetAllSectionsCollapsed(true);

        panel.Children.Add(expandButton);
        panel.Children.Add(collapseButton);
        return panel;
    }

    private FrameworkElement BuildBottomPanel()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 8, 12, 12),
            Spacing = 8
        };

        var saveButton = new Button
        {
            Content = "Save",
            MinWidth = 110,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        saveButton.Click += (_, _) =>
        {
            _tcs.TrySetResult(BuildResultState());
            _window.Closed -= OnWindowClosedCancel;
            _window.Close();
        };

        var cancelButton = new Button { Content = "Cancel", MinWidth = 110 };
        cancelButton.Click += (_, _) =>
        {
            _tcs.TrySetResult(null);
            _window.Closed -= OnWindowClosedCancel;
            _window.Close();
        };

        panel.Children.Add(saveButton);
        panel.Children.Add(cancelButton);
        return panel;
    }

    private void OnWindowClosedCancel(object sender, WindowEventArgs e)
        => _tcs.TrySetResult(null);

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
            Header = string.IsNullOrWhiteSpace(section.Display) ? "Section" : section.Display,
            IsExpanded = false,
            Margin = new Thickness(12, 8, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        var body = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 4, 0, 0)
        };

        AddInputs(body, section, SnippetInputPlacement.BeforeSelector);
        AddSnippetSelector(body, section);
        AddInputs(body, section, SnippetInputPlacement.AfterSelector);

        if (body.Children.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = "No options available.",
                Foreground = App.ThemeBrush("TextFillColorSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        expander.Content = body;
        return new SectionUi(section, expander, body);
    }

    private void AddInputs(Panel host, CodeSnippetSection section, SnippetInputPlacement placement)
    {
        foreach (var input in section.Inputs.Where(i => i != null && i.Placement == placement))
            AddInputRow(host, input);
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
            Text = GetInitialTextValue(key),
            PlaceholderText = input.Placeholder ?? string.Empty
        };

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
                    Foreground = App.ThemeBrush("TextFillColorSecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                host.Children.Add(row);
                return;
            }

            var selectedIds = new HashSet<string>(resolved, StringComparer.OrdinalIgnoreCase);
            var checkPanel = new StackPanel { Orientation = Orientation.Vertical };

            // Map item IDs to their CodeSnippetItem for input lookup
            var itemById = section.Items
                .Where(it => it != null && !string.IsNullOrWhiteSpace(it.Id))
                .ToDictionary(it => it.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var option in options)
            {
                var isChecked = !string.IsNullOrWhiteSpace(option.Id) && selectedIds.Contains(option.Id);
                var cb = new CheckBox
                {
                    Content = option.Display,
                    Tag = option.Id,
                    IsChecked = isChecked,
                    Margin = new Thickness(2, 1, 2, 1)
                };
                checkPanel.Children.Add(cb);

                // Per-snippet inputs (show inline below checkbox when checked)
                if (!string.IsNullOrWhiteSpace(option.Id)
                    && itemById.TryGetValue(option.Id, out var snippetItem)
                    && snippetItem.Inputs.Count > 0)
                {
                    var inputPanel = BuildSnippetItemInputPanel(section, snippetItem);
                    inputPanel.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
                    checkPanel.Children.Add(inputPanel);

                    cb.Checked += (_, _) => inputPanel.Visibility = Visibility.Visible;
                    cb.Unchecked += (_, _) =>
                    {
                        inputPanel.Visibility = Visibility.Collapsed;
                        // Clear stale values from the textboxes
                        foreach (var child in inputPanel.Children.OfType<StackPanel>())
                            foreach (var tb in child.Children.OfType<TextBox>())
                                tb.Text = tb.PlaceholderText ?? string.Empty;
                    };
                }
            }

            int height = Math.Clamp(options.Count * 30, MinSelectorHeight, MaxSelectorHeight);
            // Expand height when items have inputs
            bool hasInputs = section.Items.Any(it => it.Inputs.Count > 0);
            if (hasInputs)
                height = Math.Min(height + 120, MaxSelectorHeight + 120);

            var sv = new ScrollViewer
            {
                Content = checkPanel,
                Width = DefaultSelectorWidth,
                Height = height,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            row.Children.Add(sv);
            _bindings.Add(new FieldBinding(key, FieldKind.MultiSelect, checkPanel));
        }
        else
        {
            string key = SnippetControlNaming.GetComboName(section, 0);
            string storedValue = GetInitialComboValue(key);
            string resolved = ResolveStoredSingle(storedValue, section.Items);
            var options = BuildOptionItems(
                section.Items,
                string.IsNullOrWhiteSpace(resolved) ? Array.Empty<string>() : new[] { resolved },
                includeNone: true);

            var combo = new ComboBox { Width = DefaultSelectorWidth, IsEditable = false };
            foreach (var option in options)
                combo.Items.Add(option);

            SelectComboItem(combo, options, resolved);
            row.Children.Add(combo);
            _bindings.Add(new FieldBinding(key, FieldKind.SingleSelect, combo));

            // Per-snippet inputs for single-select: show inputs for selected item
            var itemById = section.Items
                .Where(it => it != null && !string.IsNullOrWhiteSpace(it.Id))
                .ToDictionary(it => it.Id, StringComparer.OrdinalIgnoreCase);

            var inputHost = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 4, 0, 0) };
            var currentItemInputPanel = new StackPanel { Orientation = Orientation.Vertical };
            inputHost.Children.Add(currentItemInputPanel);

            void UpdateComboInputs()
            {
                currentItemInputPanel.Children.Clear();
                // Remove old bindings for scoped text keys of this section
                _bindings.RemoveAll(b => b.Kind == FieldKind.Text && b.Key.StartsWith(section.Template + "_", StringComparison.Ordinal));

                if (combo.SelectedItem is OptionItem sel && !string.IsNullOrWhiteSpace(sel.Id)
                    && itemById.TryGetValue(sel.Id, out var snippetItem) && snippetItem.Inputs.Count > 0)
                {
                    var panel = BuildSnippetItemInputPanel(section, snippetItem);
                    currentItemInputPanel.Children.Add(panel);
                }
            }

            combo.SelectionChanged += (_, _) => UpdateComboInputs();
            UpdateComboInputs();

            // Only add host if any items actually have inputs
            if (section.Items.Any(it => it.Inputs.Count > 0))
                host.Children.Add(inputHost);
        }

        host.Children.Add(row);
    }

    /// <summary>
    /// Builds a panel of inline text inputs for a specific snippet item.
    /// Uses scoped keys: {sectionTemplate}_{itemId}_{inputId}.
    /// </summary>
    private StackPanel BuildSnippetItemInputPanel(CodeSnippetSection section, CodeSnippetItem item)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(24, 2, 0, 4),
            Padding = new Thickness(8, 4, 8, 4),
            BorderBrush = App.ThemeBrush("DraculaCurrentLineBrush"),
            BorderThickness = new Thickness(1, 0, 0, 0)
        };

        foreach (var input in item.Inputs)
        {
            var scopedKey = Washmachine.Services.CompilerService.BuildScopedInputKey(
                section.Template, item.Id, input.Id);

            var inputRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

            string labelText = string.IsNullOrWhiteSpace(input.Label) ? input.Id : input.Label;
            inputRow.Children.Add(new TextBlock
            {
                Text = labelText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 100
            });

            string initial = GetInitialTextValue(scopedKey);
            if (string.IsNullOrWhiteSpace(initial) && !string.IsNullOrWhiteSpace(input.DefaultValue))
                initial = input.DefaultValue;

            var textBox = new TextBox
            {
                Width = 120,
                Text = initial,
                PlaceholderText = input.Placeholder ?? input.DefaultValue ?? string.Empty,
                Margin = new Thickness(0, 0, 4, 0)
            };

            inputRow.Children.Add(textBox);
            panel.Children.Add(inputRow);

            _bindings.Add(new FieldBinding(scopedKey, FieldKind.Text, textBox));
        }

        return panel;
    }

    private static StackPanel CreateRow()
        => new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };

    private TemplateOptionsState BuildResultState()
    {
        var state = new TemplateOptionsState();

        foreach (var binding in _bindings)
        {
            switch (binding.Kind)
            {
                case FieldKind.Text:
                {
                    var text = ((TextBox)binding.Control).Text?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                        state.TextValues[binding.Key] = text;
                    break;
                }
                case FieldKind.SingleSelect:
                {
                    var combo = (ComboBox)binding.Control;
                    var id = (combo.SelectedItem is OptionItem opt ? opt.Id : combo.SelectedItem?.ToString() ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(id))
                        state.ComboValues[binding.Key] = id;
                    break;
                }
                case FieldKind.MultiSelect:
                {
                    var checkPanel = (StackPanel)binding.Control;
                    var selections = checkPanel.Children.OfType<CheckBox>()
                        .Where(cb => cb.IsChecked == true && cb.Tag is string id && !string.IsNullOrWhiteSpace(id))
                        .Select(cb => (string)cb.Tag!)
                        .ToList();
                    if (selections.Count > 0)
                        state.ListValues[binding.Key] = selections;
                    break;
                }
            }
        }

        return state;
    }

    private string GetInitialTextValue(string key)
        => string.IsNullOrWhiteSpace(key) ? string.Empty
         : _initialState.TextValues.TryGetValue(key, out var v) ? v ?? string.Empty : string.Empty;

    private string GetInitialComboValue(string key)
        => string.IsNullOrWhiteSpace(key) ? string.Empty
         : _initialState.ComboValues.TryGetValue(key, out var v) ? v ?? string.Empty : string.Empty;

    private IReadOnlyList<string> GetInitialListValues(string key)
        => string.IsNullOrWhiteSpace(key) ? Array.Empty<string>()
         : _initialState.ListValues.TryGetValue(key, out var v) && v != null ? v : Array.Empty<string>();

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

        // Prefer first real snippet over "None"
        int firstReal = -1;
        int noneIndex = -1;
        for (int i = 0; i < options.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(options[i].Id)) { noneIndex = i; }
            else if (firstReal < 0) { firstReal = i; }
        }

        combo.SelectedIndex = firstReal >= 0 ? firstReal : (noneIndex >= 0 ? noneIndex : -1);
    }

    private static List<OptionItem> BuildOptionItems(
        IReadOnlyList<CodeSnippetItem> items,
        IEnumerable<string>? extraIds,
        bool includeNone)
    {
        var options = new List<OptionItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (includeNone) { options.Add(OptionItem.None); seen.Add(string.Empty); }

        foreach (var item in items)
        {
            if (item == null) continue;
            var id = item.Id ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (seen.Add(id)) options.Add(new OptionItem(id, item.Display));
        }

        if (extraIds != null)
        {
            foreach (var raw in extraIds)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (seen.Add(raw)) options.Add(new OptionItem(raw, $"{raw} (custom)"));
            }
        }

        return options;
    }

    private static string ResolveStoredSingle(string raw, IReadOnlyList<CodeSnippetItem> items)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var byId = new Dictionary<string, CodeSnippetItem>(StringComparer.OrdinalIgnoreCase);
        var byDisplay = new Dictionary<string, CodeSnippetItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id)) continue;
            byId[item.Id] = item;
            if (!string.IsNullOrWhiteSpace(item.Display)) byDisplay[item.Display] = item;
        }

        if (byId.TryGetValue(raw, out var m)) return m.Id;
        if (byDisplay.TryGetValue(raw, out m)) return m.Id;
        return raw;
    }

    private static IReadOnlyList<string> ResolveStoredSelections(IEnumerable<string> rawValues, IReadOnlyList<CodeSnippetItem> items)
    {
        var selections = new List<string>();
        if (rawValues == null) return selections;

        var byId = new Dictionary<string, CodeSnippetItem>(StringComparer.OrdinalIgnoreCase);
        var byDisplay = new Dictionary<string, CodeSnippetItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id)) continue;
            byId[item.Id] = item;
            if (!string.IsNullOrWhiteSpace(item.Display)) byDisplay[item.Display] = item;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in rawValues)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string resolved = byId.TryGetValue(raw, out var m) ? m.Id
                            : byDisplay.TryGetValue(raw, out m) ? m.Id
                            : raw;
            if (seen.Add(resolved)) selections.Add(resolved);
        }

        return selections;
    }

    private void SetAllSectionsCollapsed(bool collapsed)
    {
        foreach (var s in _sections)
            s.Container.IsExpanded = !collapsed;
    }

    private static string BuildInputLabel(CodeSnippetInput input)
    {
        if (input == null) return string.Empty;
        string label = string.IsNullOrWhiteSpace(input.Label) ? input.Id : input.Label;
        if (input.Required && !string.IsNullOrWhiteSpace(label)) label += " *";
        return label;
    }

    private sealed class SectionUi
    {
        public SectionUi(CodeSnippetSection section, Expander container, StackPanel body)
        {
            Section = section; Container = container; Body = body;
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

    private enum FieldKind { Text, SingleSelect, MultiSelect }
}
