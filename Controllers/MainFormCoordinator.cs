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
    private const string TemplateGenericShellcode = "GENERICSHELLCODE";
    private readonly IAppLogger _logger;
    private readonly IAppPaths _paths;
    private readonly ICodeSnippetCatalogService _snippetCatalog;
    private readonly IShellcodeEncodingCatalog _encodingCatalog;
    private readonly ICompilerService _compiler;
    private readonly IClipboardService _clipboard;
    private readonly IUserInteractionService _interaction;
    private IReadOnlyList<CodeTemplateDefinition> _templates = Array.Empty<CodeTemplateDefinition>();

    public MainFormCoordinator(
        IAppLogger logger,
        IAppPaths paths,
        ICodeSnippetCatalogService snippetCatalog,
        IShellcodeEncodingCatalog encodingCatalog,
        ICompilerService compiler,
        IClipboardService clipboard,
        IUserInteractionService interaction)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _snippetCatalog = snippetCatalog ?? throw new ArgumentNullException(nameof(snippetCatalog));
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

        PopulateTemplateCombo(view);
        PopulateSnippetControls(view, GetSelectedTemplate(view));
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
        _logger.Ok("Clipboard contents pasted.");
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

            _logger.Info("Generating source from selected snippets...");
            var result = await _compiler.CompileAsync(data).ConfigureAwait(true);

            var loggedNotes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var note in result.Notes)
            {
                if (string.IsNullOrWhiteSpace(note))
                    continue;

                if (loggedNotes.Add(note))
                {
                    _logger.Info(note);
                }
            }
            if (result.Discovery != null)
            {
                await HandleCompilerDiscoveryAsync(view, result.Discovery).ConfigureAwait(true);
            }

            ShowGeneratedSourcePreview(view, result);

            if (!result.Success)
            {
                _interaction.ShowMessage(view,
                    "Generation failed. Check the log for details.",
                    "Generate",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            _logger.Ok("Snippet generation completed.");

            string? header = null;
            if (!string.IsNullOrWhiteSpace(result.GeneratedSourcePath))
            {
                header = $"Source saved to: {result.GeneratedSourcePath}";
            }

            string preview = result.GeneratedSourceCode ?? string.Empty;

            _interaction.ShowLargeText(
                view,
                "Generated Source",
                preview,
                header);
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error during generation: {ex.Message}");
            _interaction.ShowMessage(view,
                $"Unexpected error: {ex.Message}",
                "Generate",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            view.SubmitButton.Enabled = true;
        }
    }

    public void ShowShellcodeTip(IMainFormView view)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));

        _interaction.ShowShellcodeTip(view);
    }

    public void ShowGuardRailInfo(IMainFormView view)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));

        _interaction.ShowGuardRailInfo(view);
    }

    private void PopulateTemplateCombo(IMainFormView view)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));

        var combo = view.TemplateCombo;
        if (combo == null)
        {
            _logger.Warn("Template combo box is not available on the view; default template will be used.");
            _templates = _snippetCatalog.GetTemplates();
            return;
        }

        try
        {
            var allTemplates = _snippetCatalog.GetTemplates()
                .Where(t => t != null)
                .OrderBy(t => t.Display, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (allTemplates.Count == 0)
                throw new InvalidOperationException("No code templates are defined in the snippet catalog.");

            _templates = allTemplates;

            combo.BeginUpdate();
            try
            {
                combo.DisplayMember = nameof(TemplateComboItem.Display);
                combo.ValueMember = nameof(TemplateComboItem.Id);
                combo.DropDownStyle = ComboBoxStyle.DropDownList;
                combo.Items.Clear();

                foreach (var template in _templates)
                    combo.Items.Add(new TemplateComboItem(template.Id, template.Display));

                if (combo.Items.Count > 0 && combo.SelectedIndex < 0)
                    combo.SelectedIndex = 0;
            }
            finally
            {
                combo.EndUpdate();
            }

            _logger.Ok($"Loaded {_templates.Count} template(s) into chooser.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to populate template list: {ex.Message}");
            _interaction.ShowMessage(
                view,
                $"Unable to load templates:{Environment.NewLine}{ex.Message}",
                "Initialization Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _templates = Array.Empty<CodeTemplateDefinition>();
        }
    }

    public void HandleTemplateChanged(IMainFormView view)
    {
        PopulateSnippetControls(view, GetSelectedTemplate(view));
    }

    private CodeTemplateDefinition? GetSelectedTemplate(IMainFormView view)
    {
        if (_templates == null || _templates.Count == 0)
            return null;

        var combo = view?.TemplateCombo;
        string selectedId = string.Empty;

        if (combo != null)
        {
            if (combo.SelectedItem is TemplateComboItem item)
            {
                selectedId = item.Id ?? string.Empty;
            }
            else if (combo.SelectedValue is string rawValue)
            {
                selectedId = rawValue ?? string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(combo.Text))
            {
                selectedId = combo.Text;
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            var match = _templates.FirstOrDefault(t => string.Equals(t.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }

        return _templates.FirstOrDefault();
    }

    private void PopulateSnippetControls(IMainFormView view, CodeTemplateDefinition? template)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));
        if (view.SnippetPickerPanel == null)
            throw new InvalidOperationException("Snippet picker panel is not available on the view.");

        var panel = view.SnippetPickerPanel;
        panel.SuspendLayout();
        try
        {
            panel.Controls.Clear();
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            panel.AutoScroll = true;

            CodeSnippetSection? genericSection = null;
            bool templateProvided = template != null;

            var snippetPlaceholders = template?.Placeholders
                .Where(p => p != null && p.Kind == TemplatePlaceholderKind.Snippet && !string.IsNullOrWhiteSpace(p.SnippetTemplateKey))
                .ToList();

            if (snippetPlaceholders != null && snippetPlaceholders.Count > 0)
            {
                foreach (var placeholder in snippetPlaceholders)
                {
                    var snippetKey = placeholder.SnippetTemplateKey;
                    if (string.IsNullOrWhiteSpace(snippetKey))
                        continue;

                    if (!_snippetCatalog.TryGetSectionByTemplate(snippetKey, out var section))
                    {
                        _logger.Warn($"Template '{template!.Id}' references missing snippet section '{snippetKey}'.");
                        continue;
                    }

                    if (string.Equals(section.Template, TemplateGenericShellcode, StringComparison.OrdinalIgnoreCase))
                    {
                        genericSection ??= section;
                        continue;
                    }

                    var ui = new SnippetSectionUi(section);
                    AddSnippetSectionControls(view, panel, ui);
                }
            }
            else
            {
                foreach (var section in _snippetCatalog.GetAllSections())
                {
                    if (section == null)
                        continue;

                    if (string.Equals(section.Template, TemplateGenericShellcode, StringComparison.OrdinalIgnoreCase))
                    {
                        genericSection ??= section;
                        continue;
                    }

                    var ui = new SnippetSectionUi(section);
                    AddSnippetSectionControls(view, panel, ui);
                }

                templateProvided = false;
            }

            PopulateGenericShellcodeCombo(view, genericSection);

            _logger.Info("Tip: Use 'None' whenever you want to skip a snippet section.");
            if (templateProvided && template != null)
                _logger.Ok($"Snippet picker populated for template '{template.Display}'.");
            else
                _logger.Ok("Snippet picker populated with all available sections.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to populate snippet picker: {ex.Message}");
            _interaction.ShowMessage(
                view,
                $"Unable to populate snippet options:{Environment.NewLine}{ex.Message}",
                "Initialization Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            panel.ResumeLayout(true);
        }
    }

    private void ShowGeneratedSourcePreview(IMainFormView view, CompilerResult result)
    {
        if (view == null)
            throw new ArgumentNullException(nameof(view));
        if (result == null)
            return;

        if (string.IsNullOrWhiteSpace(result.GeneratedSourceCode))
            return;

        string? header = null;
        if (!string.IsNullOrWhiteSpace(result.GeneratedSourcePath))
        {
            header = $"Source saved to: {result.GeneratedSourcePath}";
        }
        else if (!result.Success)
        {
            header = "Generation failed; preview shown for debugging.";
        }

        _interaction.ShowLargeText(
            view,
            result.Success ? "Generated Source" : "Generated Source (Debug Preview)",
            result.GeneratedSourceCode,
            header);
    }

    private void AddSnippetSectionControls(IMainFormView view, FlowLayoutPanel host, SnippetSectionUi ui)
    {
        if (host == null) throw new ArgumentNullException(nameof(host));
        if (ui == null) throw new ArgumentNullException(nameof(ui));

        var label = new Label
        {
            AutoSize = true,
            Text = ui.Section.Display,
            Margin = new Padding(3, host.Controls.Count == 0 ? 0 : 12, 3, 0)
        };
        host.Controls.Add(label);

        var beforeInputs = ui.Section.Inputs
            .Where(input => input.Placement == SnippetInputPlacement.BeforeSelector)
            .ToList();
        AddInputRows(view, host, beforeInputs);

        host.Controls.Add(CreateSelectorRow(ui));

        var afterInputs = ui.Section.Inputs
            .Where(input => input.Placement == SnippetInputPlacement.AfterSelector)
            .ToList();
        AddInputRows(view, host, afterInputs);
    }

    private void PopulateGenericShellcodeCombo(IMainFormView view, CodeSnippetSection? section)
    {
        if (view == null)
            throw new ArgumentNullException(nameof(view));

        var combo = view.GenericShellcodeCombo;
        if (combo == null)
            return;

        combo.BeginUpdate();
        try
        {
            combo.DisplayMember = nameof(SnippetComboItem.Display);
            combo.ValueMember = nameof(SnippetComboItem.Id);
            combo.Items.Clear();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Items.Add(SnippetComboItem.None);

            if (section != null)
            {
                foreach (var item in section.Items)
                {
                    combo.Items.Add(new SnippetComboItem(item.Id, item.Display));
                }
            }

            combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
        }
        finally
        {
            combo.EndUpdate();
        }
    }

    private void AddInputRows(IMainFormView view, FlowLayoutPanel host, IEnumerable<CodeSnippetInput> inputs)
    {
        if (inputs == null)
            return;

        foreach (var input in inputs)
        {
            if (input == null)
                continue;
            host.Controls.Add(CreateInputRow(view, input));
        }
    }

    private Control CreateSelectorRow(SnippetSectionUi ui)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(24, 6, 3, 0)
        };

        Control selector;
        int selectorIndex = ui.GetNextSelectorIndex();

        if (ui.Section.AllowMultiple)
        {
            selector = CreateSnippetList(ui, selectorIndex);
        }
        else
        {
            selector = CreateSnippetCombo(ui, selectorIndex);
        }

        selector.Margin = new Padding(0);
        row.Controls.Add(selector);
        return row;
    }

    private ComboBox CreateSnippetCombo(SnippetSectionUi ui, int selectorIndex)
    {
        var combo = new ComboBox
        {
            Name = SnippetControlNaming.GetComboName(ui.Section, selectorIndex),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 320,
            Margin = new Padding(0, 0, 6, 0)
        };

        combo.DisplayMember = nameof(SnippetComboItem.Display);
        combo.ValueMember = nameof(SnippetComboItem.Id);

        combo.BeginUpdate();
        combo.Items.Add(SnippetComboItem.None);
        foreach (var item in ui.Section.Items)
        {
            combo.Items.Add(new SnippetComboItem(item.Id, item.Display));
        }
        combo.EndUpdate();
        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;

        return combo;
    }

    private ListBox CreateSnippetList(SnippetSectionUi ui, int selectorIndex)
    {
        var list = new ListBox
        {
            Name = SnippetControlNaming.GetListName(ui.Section, selectorIndex),
            SelectionMode = SelectionMode.MultiExtended,
            IntegralHeight = false,
            Width = 360,
            Margin = new Padding(0)
        };

        int itemCount = Math.Max(1, ui.Section.Items.Count);
        int preferredHeight = itemCount * 28 + 16;
        preferredHeight = Math.Clamp(preferredHeight, 120, 320);
        list.Height = preferredHeight;

        list.DisplayMember = nameof(SnippetComboItem.Display);

        foreach (var item in ui.Section.Items)
        {
            list.Items.Add(new SnippetComboItem(item.Id, item.Display));
        }

        return list;
    }

    private Control CreateInputRow(IMainFormView view, CodeSnippetInput input)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(24, 6, 3, 0)
        };

        string labelText = input.Label;
        if (input.Required && !string.IsNullOrWhiteSpace(labelText))
        {
            labelText += " *";
        }

        if (!string.IsNullOrWhiteSpace(labelText))
        {
            row.Controls.Add(new Label
            {
                AutoSize = true,
                Text = labelText,
                Margin = new Padding(0, 5, 6, 0)
            });
        }

        Control editor = input.Type switch
        {
            SnippetInputType.TextBox => CreateTextBoxInput(input),
            _ => CreateTextBoxInput(input)
        };

        row.Controls.Add(editor);

        if (!string.IsNullOrWhiteSpace(input.InfoAction))
        {
            var button = new Button
            {
                AutoSize = true,
                Text = string.IsNullOrWhiteSpace(input.InfoButtonLabel) ? "Info" : input.InfoButtonLabel,
                Margin = new Padding(8, 0, 0, 0)
            };
            button.Click += (_, _) => HandleInputInfoAction(view, input.InfoAction);
            row.Controls.Add(button);
        }

        return row;
    }

    private static Control CreateTextBoxInput(CodeSnippetInput input)
    {
        var textBox = new TextBox
        {
            Name = string.IsNullOrWhiteSpace(input.Id) ? Guid.NewGuid().ToString("N") : input.Id,
            Width = input.Width.HasValue && input.Width.Value > 0 ? input.Width.Value : 240,
            Margin = new Padding(0, 0, 0, 0)
        };
        return textBox;
    }

    private void HandleInputInfoAction(IMainFormView view, string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return;

        switch (action)
        {
            case "GuardRailInfo":
                ShowGuardRailInfo(view);
                break;
            default:
                _logger.Warn($"No handler registered for snippet input info action '{action}'.");
                break;
        }
    }

    private async Task HandleCompilerDiscoveryAsync(IMainFormView view, CompilerToolDiscoveryResult discovery)
    {
        if (view == null)
            throw new ArgumentNullException(nameof(view));
        if (discovery == null)
            return;

        if (discovery.Errors.Count > 0)
        {
            foreach (var error in discovery.Errors)
            {
                _logger.Warn(error);
            }
        }

        if (discovery.Best != null)
        {
            string state = discovery.Best.Validated ? "validated" : "not validated";
            _logger.Info($"Compiler candidate available: {discovery.Best.Path} ({state}).");
            return;
        }

        const string browsePrompt = "Are MSVS Build tools installed and you wanna browse folder for: \"vcvars64.bat\"?";
        var response = _interaction.ShowMessage(
            view,
            browsePrompt,
            "Visual Studio Build Tools",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (response != DialogResult.Yes)
        {
            _interaction.ShowMessage(
                view,
                "MSVS Build Tools should be installed.\r\nDownload: https://visualstudio.microsoft.com/downloads/",
                "Visual Studio Build Tools Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string? selected = _interaction.SelectFile(
            view,
            "Locate vcvars64.bat",
            "Batch files (*.bat)|*.bat|All files (*.*)|*.*",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

        if (string.IsNullOrWhiteSpace(selected))
        {
            _logger.Warn("Manual compiler script selection cancelled by user.");
            return;
        }

        CompilerToolDiscoveryResult manualResult;
        try
        {
            manualResult = await _compiler.RegisterManualCompilerAsync(selected).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"Manual compiler validation failed: {ex.Message}");
            _interaction.ShowMessage(
                view,
                $"Failed to validate the selected compiler script: {ex.Message}",
                "Visual Studio Build Tools",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (manualResult.Errors.Count > 0)
        {
            foreach (var error in manualResult.Errors)
            {
                _logger.Warn(error);
            }
        }

        if (manualResult.Best == null)
        {
            _interaction.ShowMessage(
                view,
                "Selected script could not be validated. MSVS Build Tools should be installed.\r\nDownload: https://visualstudio.microsoft.com/downloads/",
                "Visual Studio Build Tools",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        string selectedState = manualResult.Best.Validated ? "validated" : "not validated";
        _logger.Ok($"Manual compiler candidate selected: {manualResult.Best.Path} ({selectedState}).");

        if (!manualResult.Best.Validated)
        {
            _interaction.ShowMessage(
                view,
                "Selected script was not validated successfully. MSVS Build Tools may be incomplete.",
                "Visual Studio Build Tools",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task LoadEncodingCombosAsync(IMainFormView view)
    {
        try
        {
            var catalog = await _encodingCatalog.GetCatalogAsync().ConfigureAwait(true);
            BindEncodingCombo(view.EncoderCombo, catalog.Encoders);
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

    private static void BindEncodingCombo(ComboBox? combo, IReadOnlyCollection<ShellcodeEncodingItem> items)
    {
        if (combo == null)
            return;

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

    private bool ValidateShellcodeSource(IMainFormView view, out string? errorMessage)
    {
        bool hasFile = !string.IsNullOrWhiteSpace(view.ShellcodeFileTextBox.Text);
        bool hasRaw = !string.IsNullOrWhiteSpace(view.ShellcodeRawTextBox.Text);
        bool hasUrl = !string.IsNullOrWhiteSpace(view.ShellcodeUrlTextBox.Text);
        bool hasGeneric = !string.IsNullOrWhiteSpace(GetSelectedSnippetId(view.GenericShellcodeCombo));

        int selectedCount = new[] { hasFile, hasRaw, hasUrl, hasGeneric }.Count(x => x);

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

        _logger.Info("UI snapshot -> begin");

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

        _logger.Info("UI snapshot -> end");
    }

    private static string GetSelectedSnippetId(ComboBox? combo)
    {
        if (combo?.SelectedItem is SnippetComboItem item)
            return item.Id ?? string.Empty;

        if (combo?.SelectedValue is string raw)
            return raw ?? string.Empty;

        return string.Empty;
    }

    private sealed class SnippetSectionUi
    {
        public SnippetSectionUi(CodeSnippetSection section)
        {
            Section = section ?? throw new ArgumentNullException(nameof(section));
        }

        public CodeSnippetSection Section { get; }
        private int _nextSelectorIndex;

        public int GetNextSelectorIndex() => _nextSelectorIndex++;
    }

    private sealed class TemplateComboItem
    {
        public TemplateComboItem(string id, string display)
        {
            Id = id ?? string.Empty;
            Display = string.IsNullOrWhiteSpace(display) ? Id : display;
        }

        public string Id { get; }
        public string Display { get; }

        public override string ToString() => Id;
    }

    private sealed class SnippetComboItem
    {
        public static readonly SnippetComboItem None = new(string.Empty, "None");

        public SnippetComboItem(string id, string display)
        {
            Id = id ?? string.Empty;
            Display = string.IsNullOrWhiteSpace(display)
                ? (string.IsNullOrWhiteSpace(Id) ? "None" : Id)
                : display;
        }

        public string Id { get; }
        public string Display { get; }

        public override string ToString() => Id;
    }
}








