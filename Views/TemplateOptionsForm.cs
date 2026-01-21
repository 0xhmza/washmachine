using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Washmachine.Models;

namespace Washmachine.Views;

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

public sealed class TemplateOptionsForm : Form
{
    private readonly DataGridView _grid;
    private readonly IReadOnlyList<OptionRow> _rows;
    private readonly TemplateOptionsState _initialState;
    private readonly int _maxFieldCount;

    public TemplateOptionsForm(
        CodeTemplateDefinition template,
        IReadOnlyList<CodeSnippetSection> sections,
        TemplateOptionsState? existingState)
    {
        if (template == null) throw new ArgumentNullException(nameof(template));
        if (sections == null) throw new ArgumentNullException(nameof(sections));

        _initialState = existingState?.Clone() ?? new TemplateOptionsState();
        _rows = BuildRows(sections);
        _maxFieldCount = _rows.Count == 0 ? 0 : _rows.Max(r => r.Fields.Count);

        Text = $"Template Options - {template.Display}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(920, 520);
        Font = new Font("Segoe UI", 9f);
        BackColor = SystemColors.Window;

        _grid = BuildGrid();

        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(0, 0, 12, 10)
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Size = new Size(90, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Size = new Size(90, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };

        void LayoutButtons()
        {
            cancelButton.Location = new Point(
                bottomPanel.ClientSize.Width - cancelButton.Width,
                bottomPanel.ClientSize.Height - cancelButton.Height);
            okButton.Location = new Point(
                cancelButton.Left - okButton.Width - 10,
                cancelButton.Top);
        }

        bottomPanel.Controls.Add(okButton);
        bottomPanel.Controls.Add(cancelButton);
        bottomPanel.Resize += (_, _) => LayoutButtons();
        LayoutButtons();

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.Add(_grid);
        Controls.Add(bottomPanel);

        PopulateGrid();
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

    private DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EditMode = DataGridViewEditMode.EditOnEnter,
            GridColor = SystemColors.ControlLight,
            MultiSelect = false,
            RowHeadersVisible = false,
            ScrollBars = ScrollBars.Both,
            SelectionMode = DataGridViewSelectionMode.CellSelect
        };

        int columnWidth = GetColumnWidth(grid.Font);

        var optionColumn = new DataGridViewTextBoxColumn
        {
            Name = "OptionName",
            HeaderText = "Option",
            ReadOnly = true,
            Width = columnWidth,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        grid.Columns.Add(optionColumn);

        for (int i = 0; i < _maxFieldCount; i++)
        {
            var nameColumn = new DataGridViewTextBoxColumn
            {
                Name = $"InputName{i + 1}",
                HeaderText = $"Input Name {i + 1}",
                ReadOnly = true,
                Width = columnWidth,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            var valueColumn = new DataGridViewTextBoxColumn
            {
                Name = $"InputValue{i + 1}",
                HeaderText = $"Input {i + 1}",
                ReadOnly = false,
                Width = columnWidth,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };

            grid.Columns.Add(nameColumn);
            grid.Columns.Add(valueColumn);
        }

        return grid;
    }

    private void PopulateGrid()
    {
        _grid.Rows.Clear();
        if (_rows.Count == 0)
            return;

        _grid.Rows.Add(_rows.Count);

        for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            var row = _rows[rowIndex];
            var gridRow = _grid.Rows[rowIndex];
            gridRow.Cells[0].Value = row.OptionName;

            for (int fieldIndex = 0; fieldIndex < _maxFieldCount; fieldIndex++)
            {
                int nameCol = 1 + fieldIndex * 2;
                int valueCol = nameCol + 1;

                if (fieldIndex >= row.Fields.Count)
                {
                    gridRow.Cells[valueCol].ReadOnly = true;
                    continue;
                }

                var field = row.Fields[fieldIndex];
                gridRow.Cells[nameCol].Value = field.Label;

                switch (field.Kind)
                {
                    case FieldKind.Text:
                        gridRow.Cells[valueCol].Value = GetTextValue(field);
                        break;
                    case FieldKind.SingleSelect:
                        gridRow.Cells[valueCol] = CreateComboCell(field, GetComboValue(field));
                        break;
                    case FieldKind.MultiSelect:
                        gridRow.Cells[valueCol].Value = GetMultiDisplayValue(field);
                        gridRow.Cells[valueCol].ToolTipText = "Enter values separated by commas or new lines.";
                        break;
                    default:
                        gridRow.Cells[valueCol].ReadOnly = true;
                        break;
                }
            }
        }
    }

    private TemplateOptionsState BuildResultState()
    {
        var state = new TemplateOptionsState();

        for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            var row = _rows[rowIndex];
            var gridRow = _grid.Rows[rowIndex];

            for (int fieldIndex = 0; fieldIndex < row.Fields.Count; fieldIndex++)
            {
                int valueCol = 2 + fieldIndex * 2;
                var field = row.Fields[fieldIndex];
                var cell = gridRow.Cells[valueCol];

                switch (field.Kind)
                {
                    case FieldKind.Text:
                        var text = (cell.Value?.ToString() ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                            state.TextValues[field.Key] = text;
                        break;
                    case FieldKind.SingleSelect:
                        var selectedId = ExtractComboValue(cell.Value);
                        if (!string.IsNullOrWhiteSpace(selectedId))
                            state.ComboValues[field.Key] = selectedId;
                        break;
                    case FieldKind.MultiSelect:
                        var raw = cell.Value?.ToString() ?? string.Empty;
                        var selections = ParseMultiSelection(raw, field.Items);
                        if (selections.Count > 0)
                            state.ListValues[field.Key] = selections;
                        break;
                }
            }
        }

        return state;
    }

    private string GetTextValue(OptionField field)
        => _initialState.TextValues.TryGetValue(field.Key, out var raw) ? raw : string.Empty;

    private string GetComboValue(OptionField field)
        => _initialState.ComboValues.TryGetValue(field.Key, out var raw) ? raw : string.Empty;

    private string GetMultiDisplayValue(OptionField field)
    {
        if (!_initialState.ListValues.TryGetValue(field.Key, out var values) || values.Count == 0)
            return string.Empty;

        var displayById = field.Items
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Display, StringComparer.OrdinalIgnoreCase);

        var display = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => displayById.TryGetValue(value, out var mapped) ? mapped : value);

        return string.Join(", ", display);
    }

    private static DataGridViewComboBoxCell CreateComboCell(OptionField field, string selectedId)
    {
        var options = new List<ComboOption> { ComboOption.None };
        foreach (var item in field.Items)
        {
            if (item == null)
                continue;
            options.Add(new ComboOption(item.Id, item.Display));
        }

        var cell = new DataGridViewComboBoxCell
        {
            DataSource = options,
            DisplayMember = nameof(ComboOption.Display),
            ValueMember = nameof(ComboOption.Id),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Standard
        };

        if (options.Any(option => string.Equals(option.Id, selectedId, StringComparison.OrdinalIgnoreCase)))
            cell.Value = selectedId;
        else
            cell.Value = string.Empty;

        return cell;
    }

    private static string ExtractComboValue(object? value)
    {
        return value switch
        {
            ComboOption option => option.Id ?? string.Empty,
            string raw => raw,
            _ => string.Empty
        };
    }

    private static List<string> ParseMultiSelection(string raw, IReadOnlyList<CodeSnippetItem> items)
    {
        var selections = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
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

        var tokens = raw.Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in tokens)
        {
            var trimmed = token.Trim();
            if (trimmed.Length == 0)
                continue;

            if (byId.TryGetValue(trimmed, out var matched) && seen.Add(matched.Id))
            {
                selections.Add(matched.Id);
                continue;
            }

            if (byDisplay.TryGetValue(trimmed, out matched) && seen.Add(matched.Id))
            {
                selections.Add(matched.Id);
                continue;
            }

            if (seen.Add(trimmed))
                selections.Add(trimmed);
        }

        return selections;
    }

    private static IReadOnlyList<OptionRow> BuildRows(IReadOnlyList<CodeSnippetSection> sections)
    {
        var rows = new List<OptionRow>();

        foreach (var section in sections)
        {
            if (section == null)
                continue;

            var fields = new List<OptionField>();

            foreach (var input in section.Inputs.Where(i => i != null && i.Placement == SnippetInputPlacement.BeforeSelector))
            {
                fields.Add(new OptionField(BuildInputLabel(input), FieldKind.Text, input.Id, Array.Empty<CodeSnippetItem>()));
            }

            if (section.AllowMultiple)
            {
                fields.Add(new OptionField("Snippets (comma-separated)", FieldKind.MultiSelect,
                    SnippetControlNaming.GetListName(section, 0), section.Items));
            }
            else
            {
                fields.Add(new OptionField("Snippet", FieldKind.SingleSelect,
                    SnippetControlNaming.GetComboName(section, 0), section.Items));
            }

            foreach (var input in section.Inputs.Where(i => i != null && i.Placement == SnippetInputPlacement.AfterSelector))
            {
                fields.Add(new OptionField(BuildInputLabel(input), FieldKind.Text, input.Id, Array.Empty<CodeSnippetItem>()));
            }

            rows.Add(new OptionRow(section.Display, fields));
        }

        return rows;
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

    private static int GetColumnWidth(Font font)
    {
        var size = TextRenderer.MeasureText(new string('M', 20), font);
        return size.Width + 12;
    }

    private sealed class OptionRow
    {
        public OptionRow(string optionName, IReadOnlyList<OptionField> fields)
        {
            OptionName = string.IsNullOrWhiteSpace(optionName) ? "Option" : optionName;
            Fields = fields ?? Array.Empty<OptionField>();
        }

        public string OptionName { get; }
        public IReadOnlyList<OptionField> Fields { get; }
    }

    private sealed class OptionField
    {
        public OptionField(string label, FieldKind kind, string key, IReadOnlyList<CodeSnippetItem> items)
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Input" : label;
            Kind = kind;
            Key = key ?? string.Empty;
            Items = items ?? Array.Empty<CodeSnippetItem>();
        }

        public string Label { get; }
        public FieldKind Kind { get; }
        public string Key { get; }
        public IReadOnlyList<CodeSnippetItem> Items { get; }
    }

    private sealed class ComboOption
    {
        public static ComboOption None { get; } = new(string.Empty, "None");

        public ComboOption(string id, string display)
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
