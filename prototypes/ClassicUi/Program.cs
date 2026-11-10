namespace ClassicUi;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new PreviewWindow());
    }
}

internal sealed class PreviewWindow : Form
{
    private readonly TextBox name = new() { Dock = DockStyle.Fill, Text = "Example" };
    private readonly ComboBox profile = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label status = new() { AutoSize = true, Text = "Ready. This is a standalone demonstration." };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, AccessibleName = "Preview progress" };

    public PreviewWindow()
    {
        Text = "Classic UI preview";
        MinimumSize = new Size(480, 330);
        ClientSize = new Size(560, 370);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        layout.Controls.Add(new Label { AutoSize = true, Text = "Configure a sample task", Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 12) });
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.Controls.Add(new Label { Text = "&Name:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        fields.Controls.Add(name, 1, 0);
        fields.Controls.Add(new Label { Text = "&Profile:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        fields.Controls.Add(profile, 1, 1);
        name.AccessibleName = "Name";
        profile.AccessibleName = "Profile";
        profile.Items.AddRange(new object[] { "Standard", "Detailed" });
        profile.SelectedIndex = 0;
        layout.Controls.Add(fields);

        var explanation = new Label { AutoSize = true, Dock = DockStyle.Top, Text = "This preview uses sample settings only. No files are read or changed.", Margin = new Padding(0, 12, 0, 0) };
        layout.Controls.Add(explanation);
        var statusArea = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 };
        statusArea.Controls.Add(status);
        statusArea.Controls.Add(progress);
        layout.Controls.Add(statusArea);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
        var close = new Button { Text = "&Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        var preview = new Button { Text = "&Preview", AutoSize = true };
        close.Click += (_, _) => Close();
        preview.Click += (_, _) => ShowPreview();
        buttons.Controls.Add(close);
        buttons.Controls.Add(preview);
        layout.Controls.Add(buttons);
        AcceptButton = preview;
        CancelButton = close;
    }

    private void ShowPreview()
    {
        var value = name.Text.Trim();
        if (value.Length == 0)
        {
            status.Text = "Enter a name before previewing.";
            progress.Value = 0;
            name.Focus();
            return;
        }
        progress.Value = 100;
        status.Text = $"Preview ready: {value} ({profile.SelectedItem}).";
    }
}
