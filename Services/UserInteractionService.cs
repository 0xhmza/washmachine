using System;
using System.Drawing;
using System.Windows.Forms;

namespace Washmachine.Services;

public sealed class UserInteractionService : IUserInteractionService
{
    public DialogResult ShowMessage(
        IWin32Window owner,
        string message,
        string title,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        => MessageBox.Show(owner, message, title, buttons, icon, defaultButton);

    public string? SelectFile(
        IWin32Window owner,
        string title,
        string filter,
        string initialDirectory)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : initialDirectory,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };

        return dialog.ShowDialog(owner) == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    public void ShowShellcodeTip(IWin32Window owner)
    {
        const string tipText = "Please paste shellcode as hexadecimal byte escapes.\r\n\r\n" +
                               "• Use \\x followed by exactly two hex digits per byte.\r\n" +
                               "• No spaces, commas, or 0x prefixes.\r\n" +
                               "• Line breaks are okay; format is validated before continuing.\r\n\r\n" +
                               "Example:";

        const string example = "\\x48\\xB8\\x44\\x44\\x44\\x44\\x44\\x44\\x44\\x44\\x50\\x48\\xB8\\x55\\x55\\x55\\x55\\x55\\x55\\x55\\x55\\x50\\x48\\x31\\xC9\\x48\\x89\\xE2\\x49\\x89\\xE0\\x49\\x83\\xC0\\x08\\x4D\\x31\\xC9\\x48\\xB8\\x33\\x33\\x33\\x33\\x33\\x33\\x33\\x33\\x48\\x83\\xEC\\x28\\xFF\\xD0\\x48\\x83\\xC4\\x38\\x48\\xB8\\xEF\\xBE\\xAD\\xDE\\x00\\x00\\x00\\x00\\xEB\\xFE";

        using var dialog = CreateFixedDialog("Shellcode Tip", new Size(560, 360));

        var header = new Label
        {
            AutoSize = true,
            Text = "✨ Shellcode Input Format",
            Font = new Font("Segoe UI Semibold", 12f),
            Location = new Point(16, 16)
        };

        var info = new Label
        {
            AutoSize = false,
            Location = new Point(16, 48),
            Size = new Size(dialog.ClientSize.Width - 32, 140),
            Text = tipText
        };

        var exampleBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(16, 190),
            Size = new Size(dialog.ClientSize.Width - 32, 120),
            Font = new Font("Consolas", 9f),
            Text = example
        };

        var okButton = CreateCloseButton(dialog);

        dialog.AcceptButton = okButton;
        dialog.Controls.Add(header);
        dialog.Controls.Add(info);
        dialog.Controls.Add(exampleBox);
        dialog.Controls.Add(okButton);

        dialog.ShowDialog(owner);
    }

    public void ShowGuardRailInfo(IWin32Window owner)
    {
        using var dialog = CreateFixedDialog("Environment Condition Format", new Size(560, 420));

        var header = new Label
        {
            AutoSize = true,
            Text = "✨ Environment Condition Format",
            Font = new Font("Segoe UI Semibold", 12f),
            Location = new Point(16, 16)
        };

        var info = new Label
        {
            AutoSize = false,
            Location = new Point(16, 48),
            Size = new Size(dialog.ClientSize.Width - 32, 180),
            Text = "Enter environment conditions in the form NAME#operator#value.\r\n\r\n" +
                   "Operators include equals, contains, biggerthan, and smallerthan.\r\n" +
                   "Separate multiple conditions with commas."
        };

        var exampleLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 250),
            Text = "Examples:"
        };

        var exampleBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(16, 270),
            Size = new Size(dialog.ClientSize.Width - 32, 100),
            Font = new Font("Consolas", 9f),
            Text = "\"PROCESSOR_LEVEL#equals#6\", \"PATH#contains#System32\"\r\n" +
                   "NUMBER_OF_PROCESSORS#biggerthan#4, USERDOMAIN#equals#ACME\r\n" +
                   "PROCESSOR_LEVEL#smallerthan#10"
        };

        var okButton = CreateCloseButton(dialog);

        dialog.AcceptButton = okButton;
        dialog.Controls.Add(header);
        dialog.Controls.Add(info);
        dialog.Controls.Add(exampleLabel);
        dialog.Controls.Add(exampleBox);
        dialog.Controls.Add(okButton);

        dialog.ShowDialog(owner);
    }

    private static Form CreateFixedDialog(string title, Size size)
        => new()
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = size,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            BackColor = SystemColors.Window,
            Font = new Font("Segoe UI", 9f)
        };

    private static Button CreateCloseButton(Form dialog)
        => new()
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Size = new Size(90, 30),
            Location = new Point(dialog.ClientSize.Width - 16 - 90, dialog.ClientSize.Height - 16 - 30)
        };
}
