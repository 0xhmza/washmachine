using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

using ShieldGuard.Models;
using ShieldGuard.Services;
using ShieldGuard.Logging;

namespace ShieldGuard;

public sealed partial class MainForm : Form
{
    private readonly IAppLogger _logger;
    private readonly AppPaths _paths;
    private readonly PeAnalyzerService _analyzer;
    private readonly PeLicenseCaveService _caveService;

    private readonly TabControl _tabs = new();
    private readonly RichTextBox _log = new() { ReadOnly = true, Font = new Font("Consolas", 9) };
    private readonly ToolStripStatusLabel _status = new();

    public MainForm()
    {
        _paths = new AppPaths();
        _logger = new TextBoxLogger(_log);
        _analyzer = new PeAnalyzerService(_logger);
        _caveService = new PeLicenseCaveService(_paths, _logger);

        Text = "ShieldGuard - Software Protection";
        try { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Icon", "shieldguard.ico"), 32, 32); } catch { }
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var statusStrip = new StatusStrip();
        _status.Text = "Ready";
        statusStrip.Items.Add(_status);

        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.Add(BuildProtectTab());
        _tabs.TabPages.Add(BuildAnalyzeTab());
        _tabs.TabPages.Add(BuildInjectTab());
        _tabs.TabPages.Add(BuildStripTab());
        _tabs.TabPages.Add(BuildCatalogTab());
        _tabs.TabPages.Add(BuildSettingsTab());

        var logPage = new TabPage("Log") { Dock = DockStyle.Fill };
        _log.Dock = DockStyle.Fill;
        logPage.Controls.Add(_log);
        _tabs.TabPages.Add(logPage);

        Controls.Add(_tabs);
        Controls.Add(statusStrip);
    }

    #region Tabs

    private TabPage BuildProtectTab()
    {
        var page = new TabPage("Protect / License");
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 7 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var txtIn = LabeledRow(table, 0, "Target PE:", out var btnIn);
        btnIn.Click += (_, _) => PickFile(txtIn, "PE executable (*.exe)|*.exe|All files (*.*)|*.*");

        var txtStub = LabeledRow(table, 1, "License stub (.bin):", out var btnStub);
        btnStub.Click += (_, _) => PickFile(txtStub, "License stub (*.bin)|*.bin|All files (*.*)|*.*");

        var txtOut = LabeledRow(table, 2, "Output PE:", out var btnOut);
        btnOut.Click += (_, _) => PickSave(txtOut, "PE executable (*.exe)|*.exe");

        var cmbMethod = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbMethod.Items.AddRange(new object[] { "Code Cave", "New Section", "Section Extension", "Text Padding", "TLS Callback" });
        cmbMethod.SelectedIndex = 0;
        table.Controls.Add(new Label { Text = "Cave method:", AutoSize = true, Dock = DockStyle.Fill }, 0, 3);
        table.Controls.Add(cmbMethod, 1, 3);

        var btnBuild = new Button { Text = "Build Protected PE", Dock = DockStyle.Fill, Height = 40 };
        table.Controls.Add(new Label(), 0, 4);
        table.Controls.Add(btnBuild, 1, 4);
        table.SetColumnSpan(btnBuild, 2);

        var hint = new Label { Text = "Injects a license check into a PE code cave and hardens the binary against reverse engineering.", AutoSize = true };
        table.Controls.Add(hint, 0, 5);
        table.SetColumnSpan(hint, 3);

        btnBuild.Click += async (_, _) =>
        {
            try
            {
                if (!File.Exists(txtIn.Text)) { MessageBox.Show(this, "Select a target PE.", "ShieldGuard"); return; }
                if (!File.Exists(txtStub.Text)) { MessageBox.Show(this, "Select a license stub.", "ShieldGuard"); return; }

                var options = new PeLicenseCaveOptions
                {
                    TargetPePath = txtIn.Text,
                    LicenseStubPath = txtStub.Text,
                    OutputPath = string.IsNullOrWhiteSpace(txtOut.Text)
                        ? Path.Combine(Path.GetDirectoryName(txtIn.Text)!, Path.GetFileNameWithoutExtension(txtIn.Text) + ".protected.exe")
                        : txtOut.Text,
                    Method = cmbMethod.SelectedIndex switch
                    {
                        0 => LicenseMethod.LicenseCave,
                        1 => LicenseMethod.NewSection,
                        2 => LicenseMethod.SectionExtension,
                        3 => LicenseMethod.TextSectionPadding,
                        _ => LicenseMethod.LicenseTlsCallback,
                    },
                    RemoveSignature = true,
                    PatchSubsystemToGui = true,
                    PatchExitCalls = true,
                    PreserveOriginalEntry = true,
                };
                _status.Text = "Injecting license cave...";
                var result = await _caveService.LicenseCaveAsync(options);
                if (result.Success)
                {
                    _logger.Ok($"Protected PE written: {result.OutputPath} ({result.LicenseStubSize}-byte stub)");
                    MessageBox.Show(this, $"Protection built successfully.\n{result.OutputPath}", "ShieldGuard");
                }
                else
                {
                    _logger.Error($"Protection failed: {result.ErrorMessage}");
                    MessageBox.Show(this, $"Protection failed:\n{result.ErrorMessage}", "ShieldGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                MessageBox.Show(this, ex.Message, "ShieldGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { _status.Text = "Ready"; }
        };

        page.Controls.Add(table);
        return page;
    }

    private TabPage BuildAnalyzeTab()
    {
        var page = new TabPage("Analyze / Inspect");
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var txt = LabeledRow(table, 0, "PE file:", out var btn);
        btn.Click += (_, _) => PickFile(txt, "PE executable (*.exe;*.dll)|*.exe;*.dll|All files (*.*)|*.*");

        var btnAnalyze = new Button { Text = "Analyze PE", Dock = DockStyle.Fill, Height = 34 };
        table.Controls.Add(btnAnalyze, 0, 1);
        table.SetColumnSpan(btnAnalyze, 3);

        var output = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9), Dock = DockStyle.Fill };
        table.Controls.Add(output, 0, 2);
        table.SetColumnSpan(output, 3);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        btnAnalyze.Click += async (_, _) =>
        {
            try
            {
                if (!File.Exists(txt.Text)) { MessageBox.Show(this, "Select a PE file.", "ShieldGuard"); return; }
                _status.Text = "Analyzing...";
                var info = await _caveService.AnalyzePeAsync(txt.Text);
                output.Text = $"File: {info.FilePath}\n" +
                              $"Arch: {(info.Is64Bit ? "x64" : "x86")}  Type: {(info.IsDll ? "DLL" : "EXE")}  .NET: {info.IsDotNet}\n" +
                              $"Entry point: 0x{info.EntryPoint:X}  ImageBase: 0x{info.ImageBase:X}\n" +
                              $"ASLR: {info.HasAslr}  Signed: {info.HasSignature}\n" +
                              $"Sections: {info.Sections.Count}  File size: {info.FileSize:N0} bytes\n\n";
                foreach (var s in info.Sections)
                    output.Text += $"  {s.Name,-10} VA=0x{s.VirtualAddress:X6} VS=0x{s.VirtualSize:X6} RS=0x{s.RawSize:X6} {(s.IsExecutable ? "X" : "-")}{(s.IsReadable ? "R" : "-")}{(s.IsWritable ? "W" : "-")}\n";
                _status.Text = "Analysis complete.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                MessageBox.Show(this, ex.Message, "ShieldGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { _status.Text = "Ready"; }
        };

        page.Controls.Add(table);
        return page;
    }

    private TabPage BuildInjectTab()
    {
        var page = new TabPage("Inject / License Cave");
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 8 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var txtPe = LabeledRow(table, 0, "Target PE:", out var btnPe);
        btnPe.Click += (_, _) => PickFile(txtPe, "PE executable (*.exe)|*.exe|All files (*.*)|*.*");

        var txtStub = LabeledRow(table, 1, "License stub (.bin):", out var btnStub);
        btnStub.Click += (_, _) => PickFile(txtStub, "License stub (*.bin)|*.bin|All files (*.*)|*.*");

        var txtOut = LabeledRow(table, 2, "Output PE:", out var _);

        var numCave = new NumericUpDown { Minimum = 0, Maximum = 10_000_000, Value = 64, Dock = DockStyle.Fill };
        table.Controls.Add(new Label { Text = "Min cave (bytes):", AutoSize = true, Dock = DockStyle.Fill }, 0, 3);
        table.Controls.Add(numCave, 1, 3);

        var chkSig = new CheckBox { Text = "Remove signature", Checked = true, AutoSize = true, Dock = DockStyle.Fill };
        var chkGui = new CheckBox { Text = "GUI subsystem (no console)", Checked = true, AutoSize = true, Dock = DockStyle.Fill };
        table.Controls.Add(chkSig, 0, 4);
        table.Controls.Add(chkGui, 0, 5);

        var btnInject = new Button { Text = "Inject License Cave", Dock = DockStyle.Fill, Height = 34 };
        table.Controls.Add(btnInject, 0, 6);
        table.SetColumnSpan(btnInject, 3);

        var output = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9), Dock = DockStyle.Fill };
        table.Controls.Add(output, 0, 7);
        table.SetColumnSpan(output, 3);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        btnInject.Click += async (_, _) =>
        {
            try
            {
                if (!File.Exists(txtPe.Text) || !File.Exists(txtStub.Text)) { MessageBox.Show(this, "Select target PE and license stub.", "ShieldGuard"); return; }
                _status.Text = "Scanning code caves and injecting...";
                var options = new PeLicenseCaveOptions
                {
                    TargetPePath = txtPe.Text,
                    LicenseStubPath = txtStub.Text,
                    OutputPath = string.IsNullOrWhiteSpace(txtOut.Text)
                        ? Path.Combine(Path.GetDirectoryName(txtPe.Text)!, Path.GetFileNameWithoutExtension(txtPe.Text) + ".guarded.exe")
                        : txtOut.Text,
                    Method = LicenseMethod.LicenseCave,
                    MinCaveSize = (int)numCave.Value,
                    RemoveSignature = chkSig.Checked,
                    PatchSubsystemToGui = chkGui.Checked,
                    PatchExitCalls = true,
                };
                var result = await _caveService.LicenseCaveAsync(options);
                output.Clear();
                if (result.Success)
                {
                    output.Text = $"License cave injected.\nOutput: {result.OutputPath}\nStub RVA: 0x{result.LicenseStubAddress:X}\nStub size: {result.LicenseStubSize} bytes\nCarrier size: {result.CarrierSize} bytes\n";
                    foreach (var w in result.Warnings) output.Text += $"WARN: {w}\n";
                    foreach (var s in result.Steps) output.Text += $"  {s}\n";
                    _logger.Ok($"License cave injected into {result.OutputPath}");
                }
                else
                {
                    output.Text = $"Injection failed: {result.ErrorMessage}\n";
                    foreach (var w in result.Warnings) output.Text += $"WARN: {w}\n";
                    _logger.Error($"Injection failed: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                MessageBox.Show(this, ex.Message, "ShieldGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { _status.Text = "Ready"; }
        };

        page.Controls.Add(table);
        return page;
    }

    private TabPage BuildStripTab()
    {
        var page = new TabPage("Strip / Sections");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var lbl = new Label
        {
            Text = "PE section stripping is available through the CLI:\n  shieldguard.exe --cli-mode strip --pe <file>\nUse the Analyze tab to inspect sections before stripping.",
            AutoSize = true,
            MaximumSize = new Size(700, 0)
        };
        layout.Controls.Add(lbl);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildCatalogTab()
    {
        var page = new TabPage("Catalog");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var btn = new Button { Text = "Show encoding catalog", Height = 34, Dock = DockStyle.Fill };
        var output = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9), Dock = DockStyle.Fill };
        output.Text = "Templates and snippets are defined in Assets\\default.yaml.\nRun 'shieldguard.exe --cli-mode show templates' for the full playbook listing.\n";

        btn.Click += async (_, _) =>
        {
            try
            {
                _status.Text = "Loading catalog...";
                var runner = new Bin2ShellRunner(_paths);
                var cat = new LicenseStubEncodingCatalogService(runner, _paths);
                var catalog = await cat.GetCatalogAsync();
                output.Text = $"Encoders: {catalog.Encoders.Count}\nCompressors: {catalog.Compressors.Count}\nEnvelopes: {catalog.Envelopes.Count}\n";
                foreach (var e in catalog.Encoders) output.Text += $"  {e.DisplayText}\n";
                _status.Text = "Catalog loaded.";
            }
            catch (Exception ex)
            {
                _logger.Error($"Catalog: {ex.Message}");
                output.Text += $"\nCatalog unavailable (bin2shell/python may not be installed): {ex.Message}\n";
            }
            finally { _status.Text = "Ready"; }
        };

        layout.Controls.Add(btn, 0, 0);
        layout.Controls.Add(output, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildSettingsTab()
    {
        var page = new TabPage("Settings");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var lbl = new Label
        {
            Text = "ShieldGuard - Software protection toolkit.\nTemplates and snippets: Assets\\default.yaml\nLLVM obfuscation passes: Assets\\llvm-passes\nCLI: shieldguard.exe --cli-mode\nGUI: Win32 (Windows Forms). No web, no WinUI.",
            AutoSize = true,
            MaximumSize = new Size(700, 0)
        };
        layout.Controls.Add(lbl);
        page.Controls.Add(layout);
        return page;
    }

    #endregion

    private static TextBox LabeledRow(TableLayoutPanel table, int row, string labelText, out Button browse)
    {
        browse = new Button { Text = "Browse...", AutoSize = true, Dock = DockStyle.Fill };
        var txt = new TextBox { Dock = DockStyle.Fill };
        table.Controls.Add(new Label { Text = labelText, AutoSize = true, Dock = DockStyle.Fill }, 0, row);
        table.Controls.Add(txt, 1, row);
        table.Controls.Add(browse, 2, row);
        return txt;
    }

    private void PickFile(TextBox txt, string filter)
    {
        using var dlg = new OpenFileDialog { Filter = filter, Multiselect = false };
        if (dlg.ShowDialog(this) == System.Windows.Forms.DialogResult.OK) txt.Text = dlg.FileName;
    }

    private void PickSave(TextBox txt, string filter)
    {
        using var dlg = new SaveFileDialog { Filter = filter };
        if (dlg.ShowDialog(this) == System.Windows.Forms.DialogResult.OK) txt.Text = dlg.FileName;
    }

    private sealed class TextBoxLogger : IAppLogger
    {
        private readonly RichTextBox _box;
        public TextBoxLogger(RichTextBox box) => _box = box;
        private void Append(string prefix, string msg)
        {
            if (_box.InvokeRequired) { _box.Invoke(new Action(() => Append(prefix, msg))); return; }
            _box.AppendText($"[{DateTime.Now:HH:mm:ss}] {prefix} {msg}{Environment.NewLine}");
            _box.ScrollToCaret();
        }
        public void Info(string m) => Append("INFO", m);
        public void Warn(string m) => Append("WARN", m);
        public void Error(string m) => Append("ERROR", m);
        public void Ok(string m) => Append("OK", m);
        public void Debug(string m) { }
    }
}
