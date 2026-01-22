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
public sealed class TemplateOptionsForm : Form
{
    private const int DefaultInputWidth = 260;
    private const int DefaultSelectorWidth = 360;
    private const int MinSelectorHeight = 90;
    private const int MaxSelectorHeight = 240;

    private readonly TemplateOptionsState _initialState;
    private readonly List<FieldBinding> _bindings = new();
    private readonly List<SectionUi> _sections = new();
    private readonly Action<string>? _infoAction;
    private readonly FlowLayoutPanel _sectionsHost;
    private readonly Label _emptyLabel;

    public TemplateOptionsForm(
        CodeTemplateDefinition template,
        IReadOnlyList<CodeSnippetSection> sections,
        TemplateOptionsState? existingState,
        Action<string>? infoAction = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(sections);

        _initialState = existingState?.Clone() ?? new TemplateOptionsState();
        _infoAction = infoAction;

        Text = $"Template Options - {template.Display}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(900, 620);
        Font = new Font("Segoe UI", 9f);
        BackColor = SystemColors.Window;

        var headerPanel = BuildHeaderPanel();
        _sectionsHost = BuildSectionsHost();
        _emptyLabel = new Label
        {
            AutoSize = true,
            Text = "No options are available for this template.",
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(12, 12, 12, 0)
        };

        var bottomPanel = BuildBottomPanel(out var okButton, out var cancelButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.Add(_sectionsHost);
        Controls.Add(bottomPanel);
        Controls.Add(headerPanel);

        BuildSections(sections);
    }

    public TemplateOptionsState ResultState { get; private set; } = new();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            ResultState = BuildResultState();
        }

        base.OnFormClosing(e);
    }

    private Panel BuildHeaderPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(12, 8, 12, 0)
        };

        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Dock = DockStyle.Left
        };

        var expandButton = new Button
        {
            AutoSize = true,
            Text = "Expand All",
            Margin = new Padding(0, 0, 6, 0)
        };
        expandButton.Click += (_, _) => SetAllSectionsCollapsed(false);

        var collapseButton = new Button
        {
            AutoSize = true,
            Text = "Collapse All",
            Margin = new Padding(0, 0, 6, 0)
        };
        collapseButton.Click += (_, _) => SetAllSectionsCollapsed(true);

        buttonRow.Controls.Add(expandButton);
        buttonRow.Controls.Add(collapseButton);
        panel.Controls.Add(buttonRow);

        return panel;
    }

    private static FlowLayoutPanel BuildSectionsHost()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 12)
        };
    }

    private static Panel BuildBottomPanel(out Button okButton, out Button cancelButton)
    {
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(0, 0, 12, 10)
        };

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Size = new Size(90, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Size = new Size(90, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };

        void LayoutButtons()
        {
            cancel.Location = new Point(
                bottomPanel.ClientSize.Width - cancel.Width,
                bottomPanel.ClientSize.Height - cancel.Height);
            ok.Location = new Point(
                cancel.Left - ok.Width - 10,
                cancel.Top);
        }

        bottomPanel.Controls.Add(ok);
        bottomPanel.Controls.Add(cancel);
        bottomPanel.Resize += (_, _) => LayoutButtons();
        LayoutButtons();

        okButton = ok;
        cancelButton = cancel;

        return bottomPanel;
    }

    private void BuildSections(IReadOnlyList<CodeSnippetSection> sections)
    {
        _sectionsHost.SuspendLayout();
        try
        {
            _sectionsHost.Controls.Clear();
            _sections.Clear();
            _bindings.Clear();

            var list = sections.Where(s => s != null).ToList();
            if (list.Count == 0)
            {
                _sectionsHost.Controls.Add(_emptyLabel);
                return;
            }

            foreach (var section in list)
            {
                var ui = CreateSection(section);
                _sections.Add(ui);
                _sectionsHost.Controls.Add(ui.Container);
            }
        }
        finally
        {
            _sectionsHost.ResumeLayout(true);
        }
    }

    private SectionUi CreateSection(CodeSnippetSection section)
    {
        var container = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8, 8, 8, 8),
            Margin = new Padding(12, 10, 12, 0)
        };

        var headerRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 6)
        };

        var toggleButton = new Button
        {
            Text = "[-]",
            Size = new Size(30, 24),
            Margin = new Padding(0, 0, 6, 0)
        };

        var titleLabel = new Label
        {
            AutoSize = true,
            Text = string.IsNullOrWhiteSpace(section.Display) ? "Section" : section.Display,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 4, 0, 0)
        };

        headerRow.Controls.Add(toggleButton);
        headerRow.Controls.Add(titleLabel);
        container.Controls.Add(headerRow);

        var body = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };

        AddInputs(body, section, SnippetInputPlacement.BeforeSelector);
        AddSnippetSelector(body, section);
        AddInputs(body, section, SnippetInputPlacement.AfterSelector);

        if (body.Controls.Count == 0)
        {
            body.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "No options available.",
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(12, 4, 3, 0)
            });
        }

        container.Controls.Add(body);

        var ui = new SectionUi(section, container, body, toggleButton);
        toggleButton.Click += (_, _) => ToggleSection(ui);
        return ui;
    }

    private void AddInputs(FlowLayoutPanel host, CodeSnippetSection section, SnippetInputPlacement placement)
    {
        foreach (var input in section.Inputs.Where(i => i != null && i.Placement == placement))
        {
            AddInputRow(host, input);
        }
    }

    private void AddInputRow(FlowLayoutPanel host, CodeSnippetInput input)
    {
        var row = CreateRow();

        string labelText = BuildInputLabel(input);
        if (!string.IsNullOrWhiteSpace(labelText))
        {
            row.Controls.Add(new Label
            {
                AutoSize = true,
                Text = labelText,
                Margin = new Padding(0, 5, 6, 0)
            });
        }

        string key = input.Id ?? string.Empty;
        var textBox = new TextBox
        {
            Width = input.Width.HasValue && input.Width.Value > 0 ? input.Width.Value : DefaultInputWidth,
            Margin = new Padding(0)
        };

        if (!string.IsNullOrWhiteSpace(key))
            textBox.Name = key;

        if (!string.IsNullOrWhiteSpace(input.Placeholder))
            textBox.PlaceholderText = input.Placeholder;

        textBox.Text = GetInitialTextValue(key);
        row.Controls.Add(textBox);

        if (!string.IsNullOrWhiteSpace(input.InfoAction))
        {
            var infoButton = new Button
            {
                AutoSize = true,
                Text = string.IsNullOrWhiteSpace(input.InfoButtonLabel) ? "Info" : input.InfoButtonLabel,
                Margin = new Padding(6, 0, 0, 0)
            };
            infoButton.Click += (_, _) => _infoAction?.Invoke(input.InfoAction);
            row.Controls.Add(infoButton);
        }

        if (!string.IsNullOrWhiteSpace(key))
            _bindings.Add(new FieldBinding(key, FieldKind.Text, textBox));

        host.Controls.Add(row);
    }

    private void AddSnippetSelector(FlowLayoutPanel host, CodeSnippetSection section)
    {
        var row = CreateRow();
        row.Controls.Add(new Label
        {
            AutoSize = true,
            Text = section.AllowMultiple ? "Snippets" : "Snippet",
            Margin = new Padding(0, 5, 6, 0)
        });

        if (section.AllowMultiple)
        {
            string key = SnippetControlNaming.GetListName(section, 0);
            var storedValues = GetInitialListValues(key);
            var resolved = ResolveStoredSelections(storedValues, section.Items);
            var options = BuildOptionItems(section.Items, resolved, includeNone: false);

            if (options.Count == 0)
            {
                row.Controls.Add(new Label
                {
                    AutoSize = true,
                    Text = "No snippets defined.",
                    ForeColor = SystemColors.GrayText,
                    Margin = new Padding(0, 5, 0, 0)
                });
                host.Controls.Add(row);
                return;
            }

            var list = new CheckedListBox
            {
                Width = DefaultSelectorWidth,
                CheckOnClick = true,
                IntegralHeight = false,
                Margin = new Padding(0)
            };

            list.DisplayMember = nameof(OptionItem.Display);
            var selectedIds = new HashSet<string>(resolved, StringComparer.OrdinalIgnoreCase);
            foreach (var option in options)
            {
                int index = list.Items.Add(option);
                if (!string.IsNullOrWhiteSpace(option.Id) && selectedIds.Contains(option.Id))
                    list.SetItemChecked(index, true);
            }

            int itemCount = Math.Max(1, list.Items.Count);
            int height = itemCount * 18 + 12;
            height = Math.Clamp(height, MinSelectorHeight, MaxSelectorHeight);
            list.Height = height;

            row.Controls.Add(list);
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
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = DefaultSelectorWidth,
                Margin = new Padding(0)
            };
            combo.DisplayMember = nameof(OptionItem.Display);
            combo.ValueMember = nameof(OptionItem.Id);

            foreach (var option in options)
                combo.Items.Add(option);

            SelectComboItem(combo, options, resolved);

            row.Controls.Add(combo);
            _bindings.Add(new FieldBinding(key, FieldKind.SingleSelect, combo));
        }

        host.Controls.Add(row);
    }

    private static FlowLayoutPanel CreateRow()
    {
        return new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(12, 4, 3, 0)
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
                            id = combo?.SelectedValue as string ?? combo?.Text ?? string.Empty;

                        id = id.Trim();
                        if (!string.IsNullOrWhiteSpace(id))
                            state.ComboValues[binding.Key] = id;
                        break;
                    }
                case FieldKind.MultiSelect:
                    {
                        var list = binding.Control as CheckedListBox;
                        if (list == null)
                            break;

                        var selections = new List<string>();
                        foreach (var item in list.CheckedItems)
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

    private void ToggleSection(SectionUi section)
    {
        SetSectionCollapsed(section, !section.IsCollapsed);
    }

    private void SetSectionCollapsed(SectionUi section, bool collapsed)
    {
        section.IsCollapsed = collapsed;
        section.Body.Visible = !collapsed;
        section.ToggleButton.Text = collapsed ? "+" : "-";
    }

    private void SetAllSectionsCollapsed(bool collapsed)
    {
        foreach (var section in _sections)
        {
            SetSectionCollapsed(section, collapsed);
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
        public SectionUi(CodeSnippetSection section, FlowLayoutPanel container, FlowLayoutPanel body, Button toggleButton)
        {
            Section = section;
            Container = container;
            Body = body;
            ToggleButton = toggleButton;
        }

        public CodeSnippetSection Section { get; }
        public FlowLayoutPanel Container { get; }
        public FlowLayoutPanel Body { get; }
        public Button ToggleButton { get; }
        public bool IsCollapsed { get; set; }
    }

    private sealed class FieldBinding
    {
        public FieldBinding(string key, FieldKind kind, Control control)
        {
            Key = key ?? string.Empty;
            Kind = kind;
            Control = control ?? throw new ArgumentNullException(nameof(control));
        }

        public string Key { get; }
        public FieldKind Kind { get; }
        public Control Control { get; }
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
