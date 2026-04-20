using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Washmachine.Logging;
using Washmachine.Models;
using Washmachine.Services;
using Washmachine.Views;

namespace Washmachine.Controllers;

/// <summary>
/// Coordinates MainWindow UI events with application services and logging.
/// </summary>
public sealed class MainFormCoordinator
{
    private const string GenericShellcodeTemplatePlaceholder = "GENERICSHELLCODE";
    // Bin2Shell output patterns for array and envelope payloads.
    private static readonly Regex CodeBlobArrayRegex = new(@"unsigned\s+char\s+code_blob\[\]\s*=\s*\{(?<body>.*?)\};", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex CodeBlobTextBlockRegex = new(@"code_blob_text\[\]\s*=\s*(?<body>.*?)\s*;", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex QuotedStringRegex = new("\"(?<segment>.*?)\"", RegexOptions.Compiled | RegexOptions.Singleline);
    private readonly IAppLogger _logger;
    private readonly IAppPaths _paths;
    private readonly ICodeSnippetCatalogService _snippetCatalog;
    private readonly IShellcodeEncodingCatalog _encodingCatalog;
    private readonly IBin2ShellRunner _bin2ShellRunner;
    private readonly ICompilerService _compiler;
    private readonly IClipboardService _clipboard;
    private readonly IUserInteractionService _interaction;
    private IReadOnlyList<CodeTemplateDefinition> _templates = Array.Empty<CodeTemplateDefinition>();
    private TemplateOptionsState _templateOptions = new();
    private string _templateOptionsTemplateId = string.Empty;

    /// <summary>Exposes the current template options state for CLI bridge use.</summary>
    public TemplateOptionsState TemplateOptions => _templateOptions;
    private bool _isTemplateOptionsDialogSuppressed;
    private Bin2ShellWebOutput? _activeWebPayload;
    private IReadOnlyList<ShellcodeEncodingItem> _encoderItems = Array.Empty<ShellcodeEncodingItem>();
    private IReadOnlyList<ShellcodeEncodingItem> _envelopeItems = Array.Empty<ShellcodeEncodingItem>();

    public MainFormCoordinator(
        IAppLogger logger,
        IAppPaths paths,
        ICodeSnippetCatalogService snippetCatalog,
        IShellcodeEncodingCatalog encodingCatalog,
        IBin2ShellRunner bin2ShellRunner,
        ICompilerService compiler,
        IClipboardService clipboard,
        IUserInteractionService interaction)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _snippetCatalog = snippetCatalog ?? throw new ArgumentNullException(nameof(snippetCatalog));
        _encodingCatalog = encodingCatalog ?? throw new ArgumentNullException(nameof(encodingCatalog));
        _bin2ShellRunner = bin2ShellRunner ?? throw new ArgumentNullException(nameof(bin2ShellRunner));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    }

    private MsgBoxResult ShowMessage(
        IMainFormView view,
        string message,
        string title,
        MsgBoxButton buttons,
        MsgBoxIcon icon,
        MsgBoxResult defaultResult = MsgBoxResult.OK)
        => _interaction.ShowMessage(view.WindowHandle, message, title, buttons, icon);

    private Task<string?> SelectFileAsync(
        IMainFormView view,
        string title,
        string filter,
        string initialDirectory)
        => _interaction.SelectFileAsync(view.WindowHandle, title, filter, initialDirectory);

    private Task<string?> PromptTextAsync(
        IMainFormView view,
        string title,
        string message,
        string? placeholder = null,
        string? initialValue = null)
        => _interaction.PromptTextAsync(view.ViewXamlRoot, title, message, placeholder, initialValue);

    private void ShowLargeText(IMainFormView view, string title, string content, string? header = null)
        => _interaction.ShowLargeText(view.WindowHandle, title, content, header);

    private void ShowCopyableText(IMainFormView view, string title, string content, string? header = null)
        => _interaction.ShowCopyableText(view.WindowHandle, title, content, header);

    public async Task InitializeAsync(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var pathIssues = _paths.Validate();
        if (pathIssues.Count > 0)
        {
            var message = string.Join(Environment.NewLine, pathIssues);
            _logger.Error(message);
            ShowMessage(view, message, "Missing Assets", MsgBoxButton.OK, MsgBoxIcon.Error);
            view.SubmitButton.IsEnabled = false;
            return;
        }

        _logger.Debug("All required assets present.");
        view.PlaybookPathTextBlock.Text = _paths.ActivePlaybookPath;

        if (!File.Exists(_paths.Bin2ShellScript))
            _logger.Warn("Bin2Shell not found. Web payload and encoding features will be unavailable.");

        PopulateTemplateCombo(view);
        var selectedTemplate = GetSelectedTemplate(view);
        ResetTemplateOptions(selectedTemplate);
        UpdateTemplateContext(view, selectedTemplate);
        await LoadEncodingCombosAsync(view);
    }

    public async Task SelectShellcodeFile(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        _logger.Debug("Selecting shellcode file...");
        string? selected = await SelectFileAsync(
            view,
            "Select Shellcode File",
            "All files (*.*)|*.*",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        if (string.IsNullOrWhiteSpace(selected))
        {
            _logger.Debug("File selection cancelled.");
            return;
        }

        view.ShellcodeFileTextBox.Text = selected;
        _logger.Info($"Shellcode file: {Path.GetFileName(selected)}");
    }

    public async Task PasteShellcodeFromClipboard(IMainFormView view, TextBox target)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(target);

        _logger.Debug("Paste invoked for text box.");

        if (!_clipboard.ContainsText())
        {
            _logger.Warn("Clipboard does not contain text.");
            ShowMessage(view, "Clipboard does not contain text.", "Clipboard",
                MsgBoxButton.OK, MsgBoxIcon.Warning);
            return;
        }

        string content;
        try
        {
            content = await _clipboard.GetTextAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to read clipboard: {ex.Message}");
            ShowMessage(view, $"Failed to read clipboard: {ex.Message}", "Clipboard",
                MsgBoxButton.OK, MsgBoxIcon.Error);
            return;
        }

        if (!string.IsNullOrWhiteSpace(target.Text))
        {
            var result = ShowMessage(view,
                "Replace existing text with clipboard contents?",
                "Replace Text?",
                MsgBoxButton.YesNo,
                MsgBoxIcon.Question);

            if (result != MsgBoxResult.Yes)
            {
                _logger.Debug("User cancelled clipboard paste.");
                return;
            }
        }

        target.Text = content;
        target.SelectionStart = target.Text.Length;
        target.Focus(FocusState.Programmatic);
        _logger.Debug("Clipboard contents pasted.");
    }

    public async Task HandleSubmitAsync(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (!ValidateShellcodeSource(view, out string? validationError))
        {
            _logger.Error(validationError!);
            ShowMessage(view, validationError!, "Validation Error",
                MsgBoxButton.OK, MsgBoxIcon.Warning);
            return;
        }

        view.SubmitButton.IsEnabled = false;

        try
        {
            _logger.Debug("Validation passed. Collecting UI data...");
            var data = UiDataFactory.FromVisualTree(view.ContentRoot);
            MergeTemplateOptions(data);
            MergeWebPayload(data);
            LogCollectedData(data);

            _logger.Info("Generating source...");
            var result = await _compiler.CompileAsync(data);

            var loggedNotes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var note in result.Notes)
            {
                if (string.IsNullOrWhiteSpace(note))
                    continue;

                if (loggedNotes.Add(note))
                {
                    _logger.Debug(note);
                }
            }
            if (result.Discovery != null)
            {
                await HandleCompilerDiscoveryAsync(view, result.Discovery);
            }

            if (!result.Success)
            {
                _logger.Error("Build failed. Check the log panel for details.");
                return;
            }

            _logger.Ok("Done.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Build failed: {ex.Message}");
        }
        finally
        {
            view.SubmitButton.IsEnabled = true;
        }
    }

    public void ShowShellcodeTip(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        _interaction.ShowShellcodeTip(view.WindowHandle);
    }

    /// <summary>Logs a user interface action at debug level for session traceability.</summary>
    public void LogUiAction(string action) =>
        _logger.Debug($"[ui] {action}");

    public void ShowGuardRailInfo(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        _interaction.ShowGuardRailInfo(view.WindowHandle);
    }

    public async Task SelectShellcodeFileForUrl(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        _logger.Debug("Selecting shellcode file for web payload...");
        string? selected = await SelectFileAsync(
            view,
            "Select Shellcode File",
            "All files (*.*)|*.*",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        if (string.IsNullOrWhiteSpace(selected))
        {
            _logger.Debug("File selection cancelled.");
            return;
        }

        view.ShellcodeUrlFileTextBox.Text = selected;
        _logger.Info($"Shellcode file: {Path.GetFileName(selected)}");
    }

    public async Task GenerateWebPayloadAsync(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        string filePathRaw = view.ShellcodeUrlFileTextBox?.Text ?? string.Empty;
        string filePath = filePathRaw.Trim();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            const string message = "Select a shellcode file before generating a web payload.";
            _logger.Warn(message);
            ShowMessage(view, message, "Web Payload", MsgBoxButton.OK, MsgBoxIcon.Warning);
            return;
        }

        if (!File.Exists(filePath))
        {
            string message = $"Shellcode file not found: {filePath}";
            _logger.Warn(message);
            ShowMessage(view, message, "Web Payload", MsgBoxButton.OK, MsgBoxIcon.Warning);
            return;
        }

        ShellcodeEncodingCatalog catalog;
        try
        {
            catalog = await _encodingCatalog.GetCatalogAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load Bin2Shell catalog: {ex.Message}");
            ShowMessage(view, $"Failed to load encoding catalog:\n{ex.Message}",
                "Web Payload", MsgBoxButton.OK, MsgBoxIcon.Error);
            return;
        }

        var wizardResult = await WebPayloadWizardWindow.ShowAsync(
            view.WindowHandle,
            catalog,
            async (encoderIdx, envelopeIdx, webHelperIdx) =>
            {
                var args = new List<string>();

                if (!string.IsNullOrWhiteSpace(_paths.Bin2ShellAlgos) && File.Exists(_paths.Bin2ShellAlgos))
                {
                    args.Add("-y");
                    args.Add(_paths.Bin2ShellAlgos);
                }

                args.Add("-w");
                args.Add("-e");
                args.Add(encoderIdx.ToString(CultureInfo.InvariantCulture));
                args.Add("-v");
                args.Add(envelopeIdx.ToString(CultureInfo.InvariantCulture));
                args.Add("-wh");
                args.Add(webHelperIdx.ToString(CultureInfo.InvariantCulture));

                args.Add(filePath);

                _logger.Debug($"Running Bin2Shell web mode: encoder={encoderIdx}, envelope={envelopeIdx}, webHelper={webHelperIdx}");
                string output = await _bin2ShellRunner.RunAsync(args, cancellationToken: default);

                if (string.IsNullOrWhiteSpace(output))
                    throw new InvalidOperationException("Bin2Shell returned empty output.");

                return Bin2ShellWebOutputParser.Parse(output);
            });

        if (wizardResult == null)
        {
            _logger.Debug("Web payload wizard cancelled.");
            return;
        }

        _activeWebPayload = wizardResult.WebOutput;
        view.SetPayloadEncodingEnabled(false);

        // Populate the hidden URL textbox so source validation passes.
        view.ShellcodeUrlTextBox.Text = wizardResult.PayloadUrl;

        _logger.Ok($"Web payload ready. URL: {wizardResult.PayloadUrl}");
        PersistWebPayloadHistory(filePath, wizardResult, catalog);
    }

    private void PersistWebPayloadHistory(string sourceFilePath, WebPayloadWizardResult wizardResult, ShellcodeEncodingCatalog catalog)
    {
        var webOutput = wizardResult.WebOutput ?? new Bin2ShellWebOutput();
        var encoder = ResolveEncodingItem(catalog.Encoders, wizardResult.EncoderIndex);
        var envelope = ResolveEncodingItem(catalog.Envelopes, wizardResult.EnvelopeIndex);
        var webHelper = ResolveEncodingItem(catalog.WebHelpers, wizardResult.WebHelperIndex);

        long sourceFileSize = 0;
        try
        {
            sourceFileSize = new FileInfo(sourceFilePath).Length;
        }
        catch (IOException ex)
        {
            _logger.Warn($"Could not read source file size for history: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn($"Could not access source file size for history: {ex.Message}");
        }

        string sourceFileSha256 = string.Empty;
        try
        {
            sourceFileSha256 = ComputeFileSha256(sourceFilePath);
        }
        catch (IOException ex)
        {
            _logger.Warn($"Could not hash source file for history: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn($"Could not access source file for hashing: {ex.Message}");
        }

        int payloadLength = webOutput.PayloadLen > 0
            ? webOutput.PayloadLen
            : EstimatePayloadByteCount(webOutput.Payload);

        var entry = new PayloadHistoryEntry
        {
            GeneratedAtUtc = DateTime.UtcNow,
            SourceFilePath = sourceFilePath,
            SourceFileSizeBytes = sourceFileSize,
            SourceFileSha256 = sourceFileSha256,
            EncoderIndex = wizardResult.EncoderIndex,
            EncoderName = encoder?.Name ?? string.Empty,
            EncoderDescription = encoder?.Description ?? string.Empty,
            EnvelopeIndex = wizardResult.EnvelopeIndex,
            EnvelopeName = envelope?.Name ?? string.Empty,
            EnvelopeDescription = envelope?.Description ?? string.Empty,
            WebHelperIndex = wizardResult.WebHelperIndex,
            WebHelperName = webHelper?.Name ?? string.Empty,
            WebHelperDescription = webHelper?.Description ?? string.Empty,
            PayloadUrl = wizardResult.PayloadUrl ?? string.Empty,
            PayloadLengthBytes = payloadLength,
            PayloadChecksum = webOutput.PayloadChecksum ?? string.Empty,
            Bin2ShellCommandLine = BuildWebPayloadCommandLine(
                sourceFilePath,
                wizardResult.EncoderIndex,
                wizardResult.EnvelopeIndex,
                wizardResult.WebHelperIndex),
            Payload = webOutput.Payload ?? string.Empty,
            CppIncludes = webOutput.CppIncludes ?? string.Empty,
            CppDeclarations = webOutput.CppDeclarations ?? string.Empty,
            CppWebFetch = webOutput.CppWebFetch ?? string.Empty,
            CppPayloadInit = webOutput.CppPayloadInit ?? string.Empty,
            CppDecode = webOutput.CppDecode ?? string.Empty,
            CppPreamble = webOutput.BuildPreamble(),
            CppBody = webOutput.BuildBody()
        };

        try
        {
            PayloadHistoryStore.Append(entry);
            _logger.Debug("Web payload history entry recorded.");
        }
        catch (IOException ex)
        {
            _logger.Warn($"Failed to write payload history: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn($"Payload history path is not writable: {ex.Message}");
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.Warn($"Payload history file is malformed: {ex.Message}");
        }
    }

    private static ShellcodeEncodingItem? ResolveEncodingItem(IReadOnlyList<ShellcodeEncodingItem> items, int index)
    {
        if (items == null || items.Count == 0)
            return null;

        return items.FirstOrDefault(item => item.Index == index);
    }

    private string BuildWebPayloadCommandLine(string sourceFilePath, int encoderIdx, int envelopeIdx, int webHelperIdx)
    {
        var args = new List<string>();

        if (!string.IsNullOrWhiteSpace(_paths.Bin2ShellAlgos) && File.Exists(_paths.Bin2ShellAlgos))
        {
            args.Add("-y");
            args.Add(QuoteCliArg(_paths.Bin2ShellAlgos));
        }

        args.Add("-w");
        args.Add("-e");
        args.Add(encoderIdx.ToString(CultureInfo.InvariantCulture));
        args.Add("-v");
        args.Add(envelopeIdx.ToString(CultureInfo.InvariantCulture));
        args.Add("-wh");
        args.Add(webHelperIdx.ToString(CultureInfo.InvariantCulture));
        args.Add(QuoteCliArg(sourceFilePath));

        return $"python {QuoteCliArg(_paths.Bin2ShellScript)} {string.Join(" ", args)}";
    }

    private static string QuoteCliArg(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "\"\"";

        if (value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            return value;

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string ComputeFileSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private static int EstimatePayloadByteCount(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return 0;

        int count = 0;
        int idx = 0;
        while ((idx = payload.IndexOf("0x", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += 2;
        }

        return count > 0 ? count : payload.Length;
    }

    private void PopulateTemplateCombo(IMainFormView view, string? preferredTemplateId = null)
    {
        ArgumentNullException.ThrowIfNull(view);

        var combo = view.TemplateCombo;
        if (combo == null)
        {
            _logger.Warn("Template combo box is not available on the view; default template will be used.");
            _templates = _snippetCatalog.GetTemplates();
            return;
        }

        bool previousSuppress = _isTemplateOptionsDialogSuppressed;
        _isTemplateOptionsDialogSuppressed = true;
        try
        {
            var allTemplates = _snippetCatalog.GetTemplates()
                .Where(t => t != null)
                .OrderBy(t => t.Display, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (allTemplates.Count == 0)
                throw new InvalidOperationException("No code templates are defined in the snippet catalog.");

            _templates = allTemplates;

            combo.DisplayMemberPath = nameof(TemplateComboItem.Display);
            combo.SelectedValuePath = nameof(TemplateComboItem.Id);
            combo.IsEditable = false;
            combo.Items.Clear();

            foreach (var template in _templates)
                combo.Items.Add(new TemplateComboItem(template.Id, template.Display));

            if (!string.IsNullOrWhiteSpace(preferredTemplateId))
            {
                int selectedIndex = combo.Items
                    .OfType<TemplateComboItem>()
                    .Select((item, index) => new { item, index })
                    .Where(entry => string.Equals(entry.item.Id, preferredTemplateId, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => entry.index)
                    .FirstOrDefault();

                combo.SelectedIndex = selectedIndex;
            }
            else
            {
                combo.SelectedIndex = 0;
            }

            _logger.Debug($"Loaded {_templates.Count} template(s).");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to populate template list: {ex.Message}");
            ShowMessage(
                view,
                $"Unable to load templates:{Environment.NewLine}{ex.Message}",
                "Initialization Error",
                MsgBoxButton.OK,
                MsgBoxIcon.Error);
            _templates = Array.Empty<CodeTemplateDefinition>();
        }
        finally
        {
            _isTemplateOptionsDialogSuppressed = previousSuppress;
        }
    }

    public async Task HandleTemplateChanged(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var selectedTemplate = GetSelectedTemplate(view);
        _logger.Debug($"[ui] Template selected: {selectedTemplate?.Display ?? "(none)"} (id={selectedTemplate?.Id ?? "null"})");
        ResetTemplateOptions(selectedTemplate);
        UpdateTemplateContext(view, selectedTemplate);

        if (selectedTemplate != null && !_isTemplateOptionsDialogSuppressed)
        {
            try
            {
                await OpenTemplateConfig(view, selectedTemplate);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to open template options: {ex}");
                ShowMessage(
                    view,
                    $"Failed to open template options.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    "Template Options",
                    MsgBoxButton.OK,
                    MsgBoxIcon.Error);
            }
        }
    }

    public async Task OpenTemplateConfig(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        try
        {
            var template = GetSelectedTemplate(view);
            await OpenTemplateConfig(view, template);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to open template options: {ex}");
            ShowMessage(
                view,
                $"Failed to open template options.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Template Options",
                MsgBoxButton.OK,
                MsgBoxIcon.Error);
        }
    }

    public async Task ImportTemplateCatalogAsync(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var choice = ShowMessage(
            view,
            "Import snippet catalog from a file or URL?\r\n\r\nYes = File, No = URL.",
            "Import Template Catalog",
            MsgBoxButton.YesNoCancel,
            MsgBoxIcon.Question,
            MsgBoxResult.Yes);

        if (choice == MsgBoxResult.Cancel)
            return;

        if (choice == MsgBoxResult.Yes)
        {
            await ImportTemplateCatalogFromFileAsync(view).ConfigureAwait(true);
        }
        else
        {
            string? rawUrl = await PromptTextAsync(
                view,
                "Import Catalog from URL",
                "Enter the URL that returns YAML content:",
                "https://example.com/vx_api_snippets.yaml",
                null);

            await ImportTemplateCatalogFromUrlAsync(view, rawUrl).ConfigureAwait(true);
        }
    }

    public async Task ImportTemplateCatalogFromFileAsync(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        string? selected = await SelectFileAsync(
            view,
            "Select Snippet Catalog",
            "YAML files (*.yaml;*.yml)|*.yaml;*.yml|All files (*.*)|*.*",
            _paths.AssetsDirectory);

        if (string.IsNullOrWhiteSpace(selected))
        {
            _logger.Warn("Snippet catalog import cancelled.");
            return;
        }

        string content;
        try
        {
            content = File.ReadAllText(selected);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to read snippet catalog file: {ex.Message}");
            ShowMessage(
                view,
                $"Unable to read the selected file.{Environment.NewLine}{ex.Message}",
                "Import Error",
                MsgBoxButton.OK,
                MsgBoxIcon.Error);
            return;
        }

        await ImportTemplateCatalogContentAsync(view, content, selected).ConfigureAwait(true);
    }

    public async Task ImportTemplateCatalogFromUrlAsync(IMainFormView view, string? rawUrl)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            ShowMessage(
                view,
                "Enter a URL to import the catalog.",
                "Import Error",
                MsgBoxButton.OK,
                MsgBoxIcon.Warning);
            return;
        }

        string trimmed = rawUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowMessage(
                view,
                "The URL must start with http:// or https://",
                "Import Error",
                MsgBoxButton.OK,
                MsgBoxIcon.Warning);
            return;
        }

        string content;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            content = await client.GetStringAsync(uri).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to download snippet catalog: {ex.Message}");
            ShowMessage(
                view,
                $"Failed to download the catalog.{Environment.NewLine}{ex.Message}",
                "Import Error",
                MsgBoxButton.OK,
                MsgBoxIcon.Error);
            return;
        }

        await ImportTemplateCatalogContentAsync(view, content, uri.ToString()).ConfigureAwait(true);
    }

    public void RefreshTemplateCatalog(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        string? preferredTemplateId = GetSelectedTemplate(view)?.Id;

        if (!_snippetCatalog.TryReload(out var error))
        {
            _logger.Error($"Failed to reload snippet catalog: {error}");
            ShowMessage(
                view,
                $"Failed to reload the catalog.{Environment.NewLine}{error}",
                "Refresh Failed",
                MsgBoxButton.OK,
                MsgBoxIcon.Error);
            return;
        }

        PopulateTemplateCombo(view, preferredTemplateId);
        var selectedTemplate = GetSelectedTemplate(view);
        ResetTemplateOptions(selectedTemplate);
        UpdateTemplateContext(view, selectedTemplate);
        view.PlaybookPathTextBlock.Text = _paths.ActivePlaybookPath;
        _logger.Ok("Snippet catalog refreshed.");
    }

    public async Task ReloadEncodingCatalogAsync(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        await LoadEncodingCombosAsync(view);
    }

    public void OpenTemplateCatalogLocation(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = _paths.AssetsDirectory,
                UseShellExecute = true
            };
            Process.Start(info);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to open catalog folder: {ex.Message}");
            ShowMessage(
                view,
                $"Unable to open the catalog folder.{Environment.NewLine}{ex.Message}",
                "Open Folder",
                MsgBoxButton.OK,
                MsgBoxIcon.Warning);
        }
    }

    private Task ImportTemplateCatalogContentAsync(IMainFormView view, string content, string sourceLabel)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            ShowMessage(
                view,
                "The imported catalog content is empty.",
                "Import Error",
                MsgBoxButton.OK,
                MsgBoxIcon.Warning);
            return Task.CompletedTask;
        }

        string targetPath = _paths.ActivePlaybookFullPath;
        string? backup = null;

        try
        {
            Directory.CreateDirectory(_paths.AssetsDirectory);
            if (File.Exists(targetPath))
                backup = File.ReadAllText(targetPath);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to backup existing snippet catalog: {ex.Message}");
            backup = null;
        }

        try
        {
            File.WriteAllText(targetPath, content);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to write snippet catalog: {ex.Message}");
            ShowMessage(
                view,
                $"Unable to write the catalog to disk.{Environment.NewLine}{ex.Message}",
                "Import Error",
                MsgBoxButton.OK,
                MsgBoxIcon.Error);
            return Task.CompletedTask;
        }

        if (!_snippetCatalog.TryReload(out var error))
        {
            _logger.Error($"Imported catalog is invalid: {error}");

            if (backup != null)
            {
                try
                {
                    File.WriteAllText(targetPath, backup);
                    _snippetCatalog.TryReload(out _);
                }
                catch (Exception restoreEx)
                {
                    _logger.Warn($"Failed to restore previous catalog: {restoreEx.Message}");
                }
            }

            ShowMessage(
                view,
                $"Imported catalog could not be loaded.{Environment.NewLine}{error}",
                "Import Error",
                MsgBoxButton.OK,
                MsgBoxIcon.Error);
            return Task.CompletedTask;
        }

        _templateOptions = new TemplateOptionsState();
        _templateOptionsTemplateId = string.Empty;

        PopulateTemplateCombo(view);
        var selectedTemplate = GetSelectedTemplate(view);
        ResetTemplateOptions(selectedTemplate);
        UpdateTemplateContext(view, selectedTemplate);

        _logger.Ok($"Snippet catalog imported from {sourceLabel}.");
        ShowMessage(
            view,
            "Snippet catalog imported successfully.",
            "Import Complete",
            MsgBoxButton.OK,
            MsgBoxIcon.Information);
        return Task.CompletedTask;
    }

    private async Task OpenTemplateConfig(IMainFormView view, CodeTemplateDefinition? template)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (template == null)
        {
            ShowMessage(view,
                "Select a template before configuring options.",
                "Template Options",
                MsgBoxButton.OK,
                MsgBoxIcon.Information);
            return;
        }

        ResetTemplateOptions(template);

        var sections = GetTemplateSections(template, out var genericSection, out _);
        PopulateGenericShellcodeCombo(view, genericSection);

        var result = await TemplateOptionsWindow.ShowAsync(
            view.WindowHandle,
            template,
            sections,
            _templateOptions,
            action => HandleTemplateInfoAction(view, action));

        if (result != null)
        {
            _templateOptions = result;
            _logger.Ok($"Template options saved for '{template.Display}'.");
            LogTemplateOptions(result);
        }
    }

    private void LogTemplateOptions(TemplateOptionsState state)
    {
        if (state == null) return;
        foreach (var kv in state.ComboValues)
            _logger.Debug($"  [snippet] {kv.Key} = {kv.Value}");
        foreach (var kv in state.TextValues)
            _logger.Debug($"  [input] {kv.Key} = {kv.Value}");
        foreach (var kv in state.ListValues)
            _logger.Debug($"  [multi] {kv.Key} = [{string.Join(", ", kv.Value)}]");
    }

    private void HandleTemplateInfoAction(IMainFormView view, string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return;

        switch (action)
        {
            case "GuardRailInfo":
                ShowGuardRailInfo(view);
                break;
            default:
                _logger.Warn($"No handler registered for template info action '{action}'.");
                break;
        }
    }

    private CodeTemplateDefinition? GetSelectedTemplate(IMainFormView view)
    {
        if (_templates == null || _templates.Count == 0)
            return null;

        var combo = view?.TemplateCombo;
        string selectedId = string.Empty;

        if (combo == null)
        {
            return _templates.FirstOrDefault();
        }

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

        if (string.IsNullOrWhiteSpace(selectedId))
            return null;

        var match = _templates.FirstOrDefault(t => string.Equals(t.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            return match;

        return _templates.FirstOrDefault();
    }

    private void ResetTemplateOptions(CodeTemplateDefinition? template)
    {
        string templateId = template?.Id ?? string.Empty;
        if (!string.Equals(_templateOptionsTemplateId, templateId, StringComparison.OrdinalIgnoreCase))
        {
            _templateOptionsTemplateId = templateId;
            _templateOptions = new TemplateOptionsState();
        }
    }

    private void MergeTemplateOptions(UiData data)
    {
        if (data == null)
            return;

        foreach (var entry in _templateOptions.TextValues)
        {
            data.TextBoxes[entry.Key] = entry.Value ?? string.Empty;
        }

        foreach (var entry in _templateOptions.ComboValues)
        {
            data.ComboBoxes[entry.Key] = entry.Value ?? string.Empty;
        }

        foreach (var entry in _templateOptions.ListValues)
        {
            if (entry.Value == null || entry.Value.Count == 0)
                continue;

            data.ListBoxes[entry.Key] = new List<string>(entry.Value);
        }
    }

    private void MergeWebPayload(UiData data)
    {
        if (data == null || _activeWebPayload == null)
            return;

        // Inject structured web payload blocks so CompilerService can place them correctly.
        // Body (declarations, init, decode) goes into {{SHELLCODE_SOURCE}} inside main().
        data.TextBoxes["__webPayloadCodeBlock__"] = _activeWebPayload.BuildBody();
        // Preamble (#includes, fetch helper function) goes at file scope before main().
        data.TextBoxes["__webPayloadPreamble__"] = _activeWebPayload.BuildPreamble();
    }

    private void UpdateTemplateContext(IMainFormView view, CodeTemplateDefinition? template)
    {
        ArgumentNullException.ThrowIfNull(view);

        var sections = GetTemplateSections(template, out var genericSection, out bool templateProvided);
        PopulateGenericShellcodeCombo(view, genericSection);

        if (template == null)
        {
            _logger.Debug("Template set to None.");
            return;
        }

        if (!templateProvided)
        {
            _logger.Debug($"Template '{template.Display}' has no snippet placeholders; showing all sections.");
        }
        else if (sections.Count == 0)
        {
            _logger.Debug($"Template '{template.Display}' has no configurable sections.");
        }
        else
        {
            _logger.Info($"Template: {template.Display} ({sections.Count} section(s))");
        }
    }

    private IReadOnlyList<CodeSnippetSection> GetTemplateSections(
        CodeTemplateDefinition? template,
        out CodeSnippetSection? genericSection,
        out bool templateProvided)
    {
        var sections = new List<CodeSnippetSection>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        genericSection = null;
        templateProvided = template != null;

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

                if (_snippetCatalog.TryResolveSection(snippetKey, out var section))
                {
                    if (IsGenericSection(section))
                    {
                        genericSection ??= section;
                        continue;
                    }

                    var sectionKey = SnippetControlNaming.GetSectionKey(section);
                    if (seen.Add(sectionKey))
                        sections.Add(section);
                }
                else
                {
                    _logger.Warn($"Template '{template!.Id}' references missing snippet section '{snippetKey}'.");
                }
            }
        }
        else
        {
            foreach (var section in _snippetCatalog.GetAllSections())
            {
                if (section == null)
                    continue;

                if (IsGenericSection(section))
                {
                    genericSection ??= section;
                    continue;
                }

                var sectionKey = SnippetControlNaming.GetSectionKey(section);
                if (seen.Add(sectionKey))
                    sections.Add(section);
            }

            templateProvided = false;
        }

        return sections;
    }

    private void ShowGeneratedSourcePreview(IMainFormView view, CompilerResult result)
    {
        ArgumentNullException.ThrowIfNull(view);
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

        ShowLargeText(
            view,
            result.Success ? "Generated Source" : "Generated Source (Debug Preview)",
            result.GeneratedSourceCode,
            header);
    }

    private void PopulateGenericShellcodeCombo(IMainFormView view, CodeSnippetSection? section)
    {
        ArgumentNullException.ThrowIfNull(view);

        var combo = view.GenericShellcodeCombo;
        if (combo == null)
            return;

        combo.DisplayMemberPath = nameof(SnippetComboItem.Display);
        combo.SelectedValuePath = nameof(SnippetComboItem.Id);
        combo.IsEditable = false;
        combo.Items.Clear();
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

    private async Task HandleCompilerDiscoveryAsync(IMainFormView view, CompilerToolDiscoveryResult discovery)
    {
        ArgumentNullException.ThrowIfNull(view);
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
            _logger.Info($"Compiler: {Path.GetFileName(discovery.Best.Path)} ({state}).");
            return;
        }

        const string browsePrompt = "Are MSVS Build tools installed and you wanna browse folder for: \"vcvars64.bat\"?";
        var response = ShowMessage(
            view,
            browsePrompt,
            "Visual Studio Build Tools",
            MsgBoxButton.YesNo,
            MsgBoxIcon.Question,
            MsgBoxResult.Yes);

        if (response != MsgBoxResult.Yes)
        {
            ShowMessage(
                view,
                "MSVS Build Tools should be installed.\r\nDownload: https://visualstudio.microsoft.com/downloads/",
                "Visual Studio Build Tools Required",
                MsgBoxButton.OK,
                MsgBoxIcon.Information);
            return;
        }

        string? selected = await SelectFileAsync(
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
            manualResult = await _compiler.RegisterManualCompilerAsync(selected);
        }
        catch (Exception ex)
        {
            _logger.Error($"Manual compiler validation failed: {ex.Message}");
            ShowMessage(
                view,
                $"Failed to validate the selected compiler script: {ex.Message}",
                "Visual Studio Build Tools",
                MsgBoxButton.OK,
                MsgBoxIcon.Error);
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
            ShowMessage(
                view,
                "Selected script could not be validated. MSVS Build Tools should be installed.\r\nDownload: https://visualstudio.microsoft.com/downloads/",
                "Visual Studio Build Tools",
                MsgBoxButton.OK,
                MsgBoxIcon.Warning);
            return;
        }

        string selectedState = manualResult.Best.Validated ? "validated" : "not validated";
        _logger.Ok($"Manual compiler candidate selected: {manualResult.Best.Path} ({selectedState}).");

        if (!manualResult.Best.Validated)
        {
            ShowMessage(
                view,
                "Selected script was not validated successfully. MSVS Build Tools may be incomplete.",
                "Visual Studio Build Tools",
                MsgBoxButton.OK,
                MsgBoxIcon.Warning);
        }
    }

    private async Task LoadEncodingCombosAsync(IMainFormView view)
    {
        try
        {
            var catalog = await _encodingCatalog.GetCatalogAsync();
            _encoderItems = catalog.Encoders.OrderBy(i => i.Index).ToArray();
            _envelopeItems = catalog.Envelopes.OrderBy(i => i.Index).ToArray();

            BindEncodingCombo(view.EncoderCombo, _encoderItems);
            BindEnvelopeCombo(view.EnvelopeCombo, _envelopeItems);
            PopulateAntiEmulationCombo(view, catalog.AntiEmulation);
            UpdateEncodingDescriptions(view);
            _logger.Ok("Bin2Shell catalog loaded.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load Bin2Shell catalog: {ex.Message}");
            ShowMessage(view,
                $"Unable to load Bin2Shell algorithms:{Environment.NewLine}{ex.Message}",
                "Bin2Shell",
                MsgBoxButton.OK,
                MsgBoxIcon.Error);
        }
    }

    private static void BindEncodingCombo(ComboBox? combo, IReadOnlyCollection<ShellcodeEncodingItem> items)
    {
        if (combo == null)
            return;

        combo.Items.Clear();
        combo.DisplayMemberPath = nameof(EncodingComboItem.Name);
        combo.SelectedValuePath = nameof(EncodingComboItem.Index);

        foreach (var item in items.OrderBy(i => i.Index))
        {
            combo.Items.Add(EncodingComboItem.From(item));
        }

        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
    }

    private static void BindEnvelopeCombo(ComboBox? combo, IReadOnlyCollection<ShellcodeEncodingItem> items)
    {
        if (combo == null)
            return;

        combo.Items.Clear();
        combo.DisplayMemberPath = nameof(EncodingComboItem.Name);
        combo.SelectedValuePath = nameof(EncodingComboItem.Index);

        int preferredIndex = -1;
        foreach (var item in items.OrderBy(i => i.Index))
        {
            var display = EncodingComboItem.From(item);
            combo.Items.Add(display);

            if (preferredIndex < 0 &&
                string.Equals(item.Name, "base64", StringComparison.OrdinalIgnoreCase))
            {
                preferredIndex = combo.Items.Count - 1;
            }
        }

        if (preferredIndex >= 0)
        {
            combo.SelectedIndex = preferredIndex;
        }
        else
        {
            combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
        }
    }

    public void UpdateEncodingDescriptions(IMainFormView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var encoderDescription = ResolveSelectedDescription(view.EncoderCombo, _encoderItems);
        var envelopeDescription = ResolveSelectedDescription(view.EnvelopeCombo, _envelopeItems);

        view.EncoderDescriptionTextBlock.Text = string.IsNullOrWhiteSpace(encoderDescription)
            ? "No description available."
            : encoderDescription;

        view.EnvelopeDescriptionTextBlock.Text = string.IsNullOrWhiteSpace(envelopeDescription)
            ? "No description available."
            : envelopeDescription;

        // Log selection changes for session traceability
        var encoderLabel = view.EncoderCombo?.SelectedItem?.ToString() ?? "(none)";
        var envelopeLabel = view.EnvelopeCombo?.SelectedItem?.ToString() ?? "(none)";
        _logger.Debug($"[ui] Encoder: {encoderLabel}  |  Envelope: {envelopeLabel}");
    }

    private static string ResolveSelectedDescription(ComboBox combo, IReadOnlyList<ShellcodeEncodingItem> items)
    {
        if (combo == null || items == null || items.Count == 0)
            return string.Empty;

        if (combo.SelectedItem is EncodingComboItem selectedItem)
            return selectedItem.Description ?? string.Empty;

        if (combo.SelectedValue != null &&
            int.TryParse(combo.SelectedValue.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var selectedIndex))
        {
            return items.FirstOrDefault(item => item.Index == selectedIndex)?.Description ?? string.Empty;
        }

        var selectedName = combo.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(selectedName))
        {
            return items.FirstOrDefault(item => string.Equals(item.Name, selectedName, StringComparison.OrdinalIgnoreCase))?.Description
                ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool TryExtractCodeBlobArray(string output, out string payload)
    {
        payload = string.Empty;
        if (string.IsNullOrWhiteSpace(output))
            return false;

        var match = CodeBlobArrayRegex.Match(output);
        if (!match.Success)
            return false;

        var body = match.Groups["body"].Value;
        var lines = body
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);

        payload = string.Join(Environment.NewLine, lines);
        return payload.Length > 0;
    }

    private static bool TryExtractEnvelopeString(string output, out string payload)
    {
        payload = string.Empty;
        if (string.IsNullOrWhiteSpace(output))
            return false;

        var match = CodeBlobTextBlockRegex.Match(output);
        if (!match.Success)
            return false;

        var body = match.Groups["body"].Value;
        if (string.IsNullOrWhiteSpace(body))
            return false;

        var builder = new StringBuilder();
        foreach (Match segment in QuotedStringRegex.Matches(body))
        {
            builder.Append(segment.Groups["segment"].Value);
        }

        payload = builder.ToString();
        return payload.Length > 0;
    }

    private void PopulateAntiEmulationCombo(IMainFormView view, IReadOnlyCollection<AntiEmulationOption> options)
    {
        var combo = FindAntiEmulationCombo(view);
        if (combo == null)
        {
            if (options.Count > 0)
            {
                _logger.Warn("Anti-emulation combo 'bin2shellOptions' not found; options will not be shown.");
            }

            return;
        }

        combo.Items.Clear();
        combo.Items.Add(string.Empty);

        foreach (var option in options.OrderBy(o => o.Index))
        {
            combo.Items.Add(option.DisplayText);
        }

        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
    }

    private static ComboBox? FindAntiEmulationCombo(IMainFormView view)
    {
        if (view?.ContentRoot == null)
            return null;

        string[] candidateNames =
        {
            "bin2shellOptions",
            "bin2ShellOptions",
            "bin2shellOptionCombo",
            "bin2ShellOptionCombo",
            "bin2shellAntiCombo",
            "bin2ShellAntiCombo",
            "bin2shellAntiOptions",
            "bin2ShellAntiOptions",
            "bin2shellAntiEmulation",
            "bin2ShellAntiEmulation",
            "bin2shellSelection",
            "bin2ShellSelection"
        };

        foreach (var name in candidateNames)
        {
            var combo = FindByName<ComboBox>(view.ContentRoot, name);
            if (combo != null)
                return combo;
        }

        return null;
    }

    private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root == null || string.IsNullOrWhiteSpace(name))
            return null;

        var stack = new Stack<DependencyObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is T typed && string.Equals(typed.Name, name, StringComparison.Ordinal))
                return typed;

            int count = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                if (child != null)
                    stack.Push(child);
            }
        }

        return null;
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

        _logger.Debug("UI snapshot -> begin");

        foreach (var entry in data.TextBoxes.OrderBy(k => k.Key))
        {
            string raw = entry.Value ?? string.Empty;
            _logger.Debug($"TextBox '{entry.Key}': len={raw.Length}, value='{Ellipsize(raw, 100)}'");
        }

        foreach (var entry in data.ComboBoxes.OrderBy(k => k.Key))
        {
            string raw = entry.Value ?? string.Empty;
            _logger.Debug($"ComboBox '{entry.Key}': value='{Ellipsize(raw, 100)}'");
        }

        foreach (var entry in data.ListBoxes.OrderBy(k => k.Key))
        {
            var selections = entry.Value ?? new List<string>();
            _logger.Debug($"ListBox '{entry.Key}': selectedCount={selections.Count}");
            foreach (var item in selections)
            {
                _logger.Debug($"  - '{Ellipsize(item ?? string.Empty, 100)}'");
            }
        }

        _logger.Debug("UI snapshot -> end");
    }

    private static string GetSelectedSnippetId(ComboBox? combo)
    {
        if (combo?.SelectedItem is SnippetComboItem item)
            return item.Id ?? string.Empty;

        if (combo?.SelectedValue is string raw)
            return raw ?? string.Empty;

        return string.Empty;
    }

    private static bool IsGenericSection(CodeSnippetSection section)
    {
        if (section == null)
            return false;

        string key = GenericShellcodeTemplatePlaceholder;
        return SectionMatchesKey(section, key) ||
               section.Display.Contains("generic", StringComparison.OrdinalIgnoreCase) ||
               section.Header.Contains("generic", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SectionMatchesKey(CodeSnippetSection section, string key)
    {
        string keyNorm = SnippetKeyNormalizer.Normalize(key);
        string headerNorm = SnippetKeyNormalizer.Normalize(section.Header);
        string templateNorm = SnippetKeyNormalizer.Normalize(section.Template);

        return SnippetKeyNormalizer.Equalish(keyNorm, headerNorm) ||
               SnippetKeyNormalizer.Equalish(keyNorm, templateNorm);
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

        public override string ToString() => Display;
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

    private sealed class EncodingComboItem
    {
        private EncodingComboItem(int index, string name, string description)
        {
            Index = index;
            Name = string.IsNullOrWhiteSpace(name) ? index.ToString(CultureInfo.InvariantCulture) : name;
            Description = description ?? string.Empty;
        }

        public int Index { get; }
        public string Name { get; }
        public string Description { get; }

        public override string ToString() => Name;

        public static EncodingComboItem From(ShellcodeEncodingItem item)
            => new(item.Index, item.Name, item.Description);
    }
}






