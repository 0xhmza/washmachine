using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Washmachine.Logging;
using Washmachine.Models;
using Washmachine.Services;
using Washmachine.Views;

namespace Washmachine.Controllers;

public sealed class MainFormCoordinator
{
    private readonly IAppLogger _logger;
    private readonly IAppPaths _paths;
    private readonly IHeaderListProvider _headerLists;
    private readonly IShellcodeEncodingCatalog _encodingCatalog;
    private readonly ICompilerService _compiler;
    private readonly IClipboardService _clipboard;
    private readonly IUserInteractionService _interaction;

    public MainFormCoordinator(
        IAppLogger logger,
        IAppPaths paths,
        IHeaderListProvider headerLists,
        IShellcodeEncodingCatalog encodingCatalog,
        ICompilerService compiler,
        IClipboardService clipboard,
        IUserInteractionService interaction)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _headerLists = headerLists ?? throw new ArgumentNullException(nameof(headerLists));
        _encodingCatalog = encodingCatalog ?? throw new ArgumentNullException(nameof(encodingCatalog));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    }

    public async Task InitializeAsync(IMainFormView view)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));

        var pathIssues = _paths.Validate();
        if (pathIssues.Count > 0)
        {
            var message = string.Join(Environment.NewLine, pathIssues);
            _logger.Error(message);
            _interaction.ShowMessage(view, message, "Missing Assets", MessageBoxButtons.OK, MessageBoxIcon.Error);
            view.SubmitButton.Enabled = false;
            return;
        }

        PopulateHeaderControls(view);
        await LoadEncodingCombosAsync(view).ConfigureAwait(true);
    }

    public void SelectShellcodeFile(IMainFormView view)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));

        _logger.Info("Selecting shellcode file...");
        string? selected = _interaction.SelectFile(
            view,
            "Select Shellcode File",
            "All files (*.*)|*.*",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        if (string.IsNullOrWhiteSpace(selected))
        {
            _logger.Warn("File selection cancelled by user.");
            return;
        }

        view.ShellcodeFileTextBox.Text = selected;
        _logger.Ok($"Shellcode file selected: {selected}");
    }

    public void PasteShellcodeFromClipboard(IMainFormView view, TextBox target)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));
        if (target == null) throw new ArgumentNullException(nameof(target));

        _logger.Info("Paste invoked for text box.");

        if (!_clipboard.ContainsText())
        {
            _logger.Warn("Clipboard does not contain text.");
            _interaction.ShowMessage(view, "Clipboard does not contain text.", "Clipboard",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string content;
        try
        {
            content = _clipboard.GetText();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to read clipboard: {ex.Message}");
            _interaction.ShowMessage(view, $"Failed to read clipboard: {ex.Message}", "Clipboard",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!string.IsNullOrWhiteSpace(target.Text))
        {
            var result = _interaction.ShowMessage(view,
                "Replace existing text with clipboard contents?",
                "Replace Text?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (result != DialogResult.Yes)
            {
                _logger.Info("User cancelled clipboard paste.");
                return;
            }
        }

        target.Text = content;
        target.SelectionStart = target.TextLength;
        target.SelectionLength = 0;
        target.Focus();
        _logger.Ok("Clipboard content pasted.");
    }

    public void ShowShellcodeTip(IMainFormView view)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));
        _logger.Info("Displaying shellcode format tip.");
        _interaction.ShowShellcodeTip(view);
        _logger.Ok("Shellcode tip dialog closed.");
    }

    public void ShowGuardRailInfo(IMainFormView view)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));
        _interaction.ShowGuardRailInfo(view);
        _logger.Ok("Guard rails format dialog closed.");
    }

    public async Task HandleSubmitAsync(IMainFormView view)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));

        if (!ValidateShellcodeSource(view, out string? validationError))
        {
            _logger.Error(validationError!);
            _interaction.ShowMessage(view, validationError!, "Validation Error",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        view.SubmitButton.Enabled = false;

        try
        {
            _logger.Ok("Validation passed. Collecting UI data...");
            var data = new UiData(view.RootControl);
            LogCollectedData(data);

            _logger.Info("Starting compilation process...");
            var result = await _compiler.CompileAsync(data).ConfigureAwait(true);

            foreach (var note in result.Notes)
            {
                _logger.Info(note);
            }

            if (!result.Success)
            {
                _interaction.ShowMessage(view,
                    "Compilation failed. Check the log for details.",
                    "Compile",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            _logger.Ok("Compilation pipeline completed.");
            _interaction.ShowMessage(view,
                "Project files updated successfully.",
                "Compile",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error during compilation: {ex.Message}");
            _interaction.ShowMessage(view,
                $"Unexpected error: {ex.Message}",
                "Compile",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            view.SubmitButton.Enabled = true;
        }
    }

    private void PopulateHeaderControls(IMainFormView view)
    {
        try
        {
            _headerLists.PopulateListFromHeaderSection(view.AntiDebugList, "ANTI-DEBUGGING");

            PopulateCombo(view.GuardrailCombo, "GUARDRAILS");
            PopulateCombo(view.ProcessInjectionCombo, "PROCESS INJECTION");
            PopulateCombo(view.ShellcodeExecutionCombo, "SHELLCODE EXECUTION");
            PopulateCombo(view.UacBypassCombo, "UAC BYPASSES");
            PopulateCombo(view.GenericShellcodeCombo, "GENERIC SHELLCODE PAYLOADS FOR TESTINGS");

            _logger.Info("Tip: Choose the empty entry for components you wish to skip.");
            _logger.Ok("Header lists populated successfully.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to populate header lists: {ex.Message}");
            _interaction.ShowMessage(view,
                $"Unable to populate header lists:{Environment.NewLine}{ex.Message}",
                "Initialization Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        void PopulateCombo(ComboBox combo, string sectionName)
        {
            _headerLists.PopulateComboFromHeaderSection(combo, sectionName);
            combo.Items.Add(string.Empty);
        }
    }

    private async Task LoadEncodingCombosAsync(IMainFormView view)
    {
        try
        {
            var catalog = await _encodingCatalog.GetCatalogAsync().ConfigureAwait(true);
            BindEncodingCombo(view.EncoderCombo, catalog.Encoders);
            BindEncodingCombo(view.CompressorCombo, catalog.Compressors);
            BindEncodingCombo(view.EnvelopeCombo, catalog.Envelopes);
            _logger.Ok("Bin2Shell catalog loaded.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load Bin2Shell catalog: {ex.Message}");
            _interaction.ShowMessage(view,
                $"Unable to load Bin2Shell algorithms:{Environment.NewLine}{ex.Message}",
                "Bin2Shell",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void BindEncodingCombo(ComboBox combo, IReadOnlyCollection<ShellcodeEncodingItem> items)
    {
        combo.BeginUpdate();
        try
        {
            combo.Items.Clear();
            combo.Items.Add(string.Empty);

            foreach (var item in items.OrderBy(i => i.Index))
            {
                combo.Items.Add(item.DisplayText);
            }

            combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
        }
        finally
        {
            combo.EndUpdate();
        }
    }

    private static bool ValidateShellcodeSource(IMainFormView view, out string? errorMessage)
    {
        bool hasFile = !string.IsNullOrWhiteSpace(view.ShellcodeFileTextBox.Text);
        bool hasRaw = !string.IsNullOrWhiteSpace(view.ShellcodeRawTextBox.Text);
        bool hasUrl = !string.IsNullOrWhiteSpace(view.ShellcodeUrlTextBox.Text);
        bool hasCombo = view.GenericShellcodeCombo.SelectedItem is string comboText &&
                        !string.IsNullOrWhiteSpace(comboText);

        int selectedCount = new[] { hasFile, hasRaw, hasUrl, hasCombo }.Count(x => x);

        const string msgNoSource = "Please provide one shellcode source (File, RAW, URL, or Generic).";
        const string msgMultipleSources = "Multiple shellcode sources provided. Please select only one.";

        if (selectedCount == 0)
        {
            errorMessage = msgNoSource;
            return false;
        }

        if (selectedCount > 1)
        {
            errorMessage = msgMultipleSources;
            return false;
        }

        errorMessage = null;
        return true;
    }

    private void LogCollectedData(UiData data)
    {
        if (data == null)
        {
            _logger.Warn("UiData snapshot is null.");
            return;
        }

        static string Ellipsize(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;

            int keep = maxLength - 2;
            int head = keep / 2;
            int tail = keep - head;
            return value.Substring(0, head) + ".." + value.Substring(value.Length - tail);
        }

        _logger.Info("UI snapshot — begin");

        foreach (var entry in data.TextBoxes.OrderBy(k => k.Key))
        {
            string raw = entry.Value ?? string.Empty;
            _logger.Info($"TextBox '{entry.Key}': len={raw.Length}, value='{Ellipsize(raw, 100)}'");
        }

        foreach (var entry in data.ComboBoxes.OrderBy(k => k.Key))
        {
            string raw = entry.Value ?? string.Empty;
            _logger.Info($"ComboBox '{entry.Key}': value='{Ellipsize(raw, 100)}'");
        }

        foreach (var entry in data.ListBoxes.OrderBy(k => k.Key))
        {
            var selections = entry.Value ?? new List<string>();
            _logger.Info($"ListBox '{entry.Key}': selectedCount={selections.Count}");
            foreach (var item in selections)
            {
                _logger.Info($"  - '{Ellipsize(item ?? string.Empty, 100)}'");
            }
        }

        _logger.Info("UI snapshot — end");
    }
}
