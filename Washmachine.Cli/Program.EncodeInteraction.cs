namespace Washmachine.Cli;

using System.Globalization;
using System.Text;
using Spectre.Console;
using Washmachine.Cli.Ui;
using Washmachine.Logging;
using Washmachine.Models;
using Washmachine.Services;

public static partial class Program
{
    private const string EncodeSessionCompletionCommand = "encode-session";

    private static readonly string[] EncodeSessionCommands =
    [
        "show",
        "set",
        "unset",
        "get",
        "add",
        "remove",
        "help",
        "reset",
        "run",
        "build",
        "cat",
        "exit",
        "quit",
    ];

    private static readonly EncodeOptionSpec[] EncodeOptionSpecs =
    [
        new(
            "PAYLOAD_SOURCE",
            Required: true,
            DefaultValue: null,
            Description: "Exactly one payload source to compile.",
            Details: "Set one source using file:<path>, hex:<hex>, or url:<http(s)://...>. The CLI never guesses or mixes multiple sources.",
            Expected: "file:<path> | hex:<hex> | url:<http(s)://...>",
            Example: @"file:C:\payloads\payload.bin"),
        new(
            "TEMPLATE",
            Required: false,
            DefaultValue: "minimal",
            Description: "Template ID from the active playbook.",
            Details: "Controls which placeholder blocks, defaults, and snippet sections are used for the generated loader.",
            Expected: "A valid template ID from the active catalog",
            Example: "minimal"),
        new(
            "SHELLCODE_EXECUTION",
            Required: false,
            DefaultValue: null,
            Description: "Shellcode execution technique.",
            Details: "Selects the execution method injected by the loader template. Default (not set) uses the template's built-in default.",
            Expected: "Technique name or ID from 'show execution'",
            Example: "create_thread"),
        new(
            "ENCODER",
            Required: false,
            DefaultValue: "0",
            Description: "Bin2Shell encoder index.",
            Details: "Use 0 for none, or a live encoder index from the local Bin2Shell catalog once provisioned.",
            Expected: "0 or a non-negative encoder index",
            Example: "1"),
        new(
            "ENVELOPE",
            Required: false,
            DefaultValue: "0",
            Description: "Bin2Shell envelope index.",
            Details: "Use 0 for none, or a live envelope index from the local Bin2Shell catalog once provisioned.",
            Expected: "0 or a non-negative envelope index",
            Example: "1"),
        new(
            "SHIKATA_GA_NAI",
            Required: false,
            DefaultValue: "false",
            Description: "Enable SGN preprocessing.",
            Details: "When true, the encode pipeline provisions and applies Shikata Ga Nai before the normal Bin2Shell stage.",
            Expected: "true | false",
            Example: "true"),
        new(
            "SHIKATA_ENCODE_COUNT",
            Required: false,
            DefaultValue: "1",
            Description: "SGN iteration count.",
            Details: "Positive integer iteration count for SGN. Setting this also enables SHIKATA_GA_NAI.",
            Expected: "Positive integer",
            Example: "2"),
        new(
            "SHIKATA_MAX_BYTES",
            Required: false,
            DefaultValue: "50",
            Description: "Maximum SGN decoder bytes.",
            Details: "Positive integer limit for SGN decoder-obfuscation bytes. Setting this also enables SHIKATA_GA_NAI.",
            Expected: "Positive integer",
            Example: "64"),
        new(
            "SHIKATA_PLACEMENT",
            Required: false,
            DefaultValue: "pre",
            Description: "When SGN runs relative to Bin2Shell.",
            Details: "Controls whether SGN is applied before or after the Bin2Shell transform.",
            Expected: "pre | post",
            Example: "pre"),
        new(
            "SNIPPETS",
            Required: false,
            DefaultValue: null,
            Description: "Snippet section overrides.",
            Details: "Use section=id[,id...] pairs separated by semicolons. Multi-select sections accept comma-separated IDs.",
            Expected: "section=id[,id...];section2=id",
            Example: "antidebugging=IsDebuggerPresent;antianalysis=HideThread"),
        new(
            "TEXT_INPUTS",
            Required: false,
            DefaultValue: null,
            Description: "Template and snippet text overrides.",
            Details: "Use input=value pairs separated by semicolons. Input keys must exist in the active playbook.",
            Expected: "input=value;other=value",
            Example: "PsInjPsNameTextBox=explorer.exe"),
        new(
            "CLONE_FROM",
            Required: false,
            DefaultValue: null,
            Description: "Donor executable used for post-compile cloning.",
            Details: "When set, post-compile finishing can clone icon, metadata, and resources from the donor executable.",
            Expected: "Existing .exe path",
            Example: @"C:\samples\donor.exe"),
        new(
            "CLONE_RESOURCES",
            Required: false,
            DefaultValue: "auto",
            Description: "General resource cloning behavior.",
            Details: "auto follows the encode pipeline's default behavior when CLONE_FROM is present. true forces cloning on, false forces it off.",
            Expected: "auto | true | false",
            Example: "false"),
        new(
            "CLONE_ICON",
            Required: false,
            DefaultValue: "auto",
            Description: "Icon cloning behavior.",
            Details: "auto follows the encode pipeline's default behavior when CLONE_FROM is present. true forces cloning on, false forces it off.",
            Expected: "auto | true | false",
            Example: "auto"),
        new(
            "CLONE_METADATA",
            Required: false,
            DefaultValue: "false",
            Description: "Enable PE metadata cloning from a donor file.",
            Details: "When true, reveals CLONE_FROM, CLONE_RESOURCES, and CLONE_ICON options for configuring what gets cloned.",
            Expected: "true | false",
            Example: "true"),
        new(
            "PAD_NOPS",
            Required: false,
            DefaultValue: null,
            Description: "Extra NOP bytes appended after compile.",
            Details: "Use a positive integer byte count to inflate the final executable size after a successful compile.",
            Expected: "Positive integer",
            Example: "1048576"),
        new(
            "VERBOSE",
            Required: false,
            DefaultValue: "false",
            Description: "Show detailed encode logs.",
            Details: "Enables verbose CLI logging for provisioning, catalog loading, compilation, and post-compile steps.",
            Expected: "true | false",
            Example: "true"),
        new(
            "JSON",
            Required: false,
            DefaultValue: "false",
            Description: "Emit machine-friendly JSON on build.",
            Details: "When true, the encode pipeline prints JSON instead of the styled success/failure report.",
            Expected: "true | false",
            Example: "true"),
        new(
            "COMPILATION_BACKEND",
            Required: false,
            DefaultValue: "Deterministic",
            Description: "Compilation backend selection.",
            Details: "Deterministic uses the standard toolchain (cl.exe / g++). LlvmObfuscated uses bundled clang++ with IR-level obfuscation passes.",
            Expected: "Deterministic | LlvmObfuscated",
            Example: "LlvmObfuscated"),
        new(
            "LLVM_PASSES",
            Required: false,
            DefaultValue: null,
            Description: "LLVM IR obfuscation pass IDs (only when backend=LlvmObfuscated).",
            Details: "Comma-separated list of pass IDs to apply. Each pass.dll must be built first from Assets/llvm-passes/<id>/CMakeLists.txt.",
            Expected: "control-flow-flattening | instruction-substitution | string-obfuscation | bogus-control-flow",
            Example: "control-flow-flattening,instruction-substitution"),
    ];

    private static readonly IReadOnlyDictionary<string, EncodeOptionSpec> EncodeOptionSpecsByName =
        EncodeOptionSpecs.ToDictionary(spec => spec.Name, spec => spec, StringComparer.OrdinalIgnoreCase);

    private static async Task<int> HandleEncodeCommandAsync(string[] args)
    {
        var context = await CreateEncodeSessionContextAsync();

        if (args.Length == 0)
        {
            if (!_isRepl) { PrintEncodeUsage(); return 1; }
            return await RunEncodeInteractiveSessionAsync(context, new EncodeSessionState(), announceFallback: false);
        }

        var parse = ParseEncodeOneLiner(context, args);
        if (parse.FatalMessage is not null)
        {
            WriteStatus(StatusPrefix.Failure, parse.FatalMessage);
            return 1;
        }

        if (parse.State.Json && parse.RequiresInteractiveFallback)
        {
            foreach (var message in parse.Messages)
                WriteStatus(StatusPrefix.Failure, message);
            return 1;
        }

        if (parse.RequiresInteractiveFallback)
        {
            foreach (var message in parse.Messages)
                WriteStatus(StatusPrefix.Failure, message);

            if (parse.MissingRequired.Count > 0)
            {
                WriteStatus(StatusPrefix.Warning,
                    $"Missing required parameter(s): {string.Join(", ", parse.MissingRequired.Select(spec => spec.Name))}");
            }

            if (!CanEnterInteractiveShell())
                return 1;

            WriteStatus(StatusPrefix.Info, "Entering interactive mode for remaining parameters.");
            return await RunEncodeInteractiveSessionAsync(context, parse.State, announceFallback: true);
        }

        if (!parse.State.Json)
        {
            WriteStatus(StatusPrefix.Info, "Parsed parameters:");
            RenderEncodeOptionsTable(context, parse.State);
            WriteStatus(StatusPrefix.Info, "All required parameters valid. Building...");
        }

        return await ExecuteEncodePipelineAsync(context, parse.State);
    }

    private static async Task<EncodeSessionContext> CreateEncodeSessionContextAsync()
    {
        var paths = new AppPaths();
        var snippetService = new YamlCodeSnippetCatalogService(paths);
        ShellcodeEncodingCatalog? encodingCatalog = null;
        string? catalogWarning = null;

        if (IsProvisioned())
        {
            try
            {
                var runner = new Bin2ShellRunner(paths);
                var catalogService = new ShellcodeEncodingCatalogService(runner, paths);
                encodingCatalog = await catalogService.GetCatalogAsync();
            }
            catch (Exception ex)
            {
                catalogWarning = $"Could not load the live encoder catalog: {ex.Message}";
            }
        }
        else
        {
            catalogWarning = "Bin2Shell is not provisioned yet. Encoder and envelope values will be re-checked during build.";
        }

        var knownTextInputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in snippetService.GetAllSections())
        {
            foreach (var input in section.Inputs)
            {
                if (!string.IsNullOrWhiteSpace(input.Id))
                    knownTextInputs.Add(input.Id);
            }

            foreach (var item in section.Items)
            {
                foreach (var input in item.Inputs)
                {
                    var scopedKey = CompilerService.BuildScopedInputKey(section.Template, item.Id, input.Id);
                    knownTextInputs.Add(scopedKey);
                }
            }
        }

        return new EncodeSessionContext(paths, snippetService, encodingCatalog, catalogWarning, knownTextInputs);
    }

    private static async Task<int> RunEncodeInteractiveSessionAsync(
        EncodeSessionContext context,
        EncodeSessionState initialState,
        bool announceFallback)
    {
        var state = initialState.Clone();

        if (!announceFallback && !string.IsNullOrWhiteSpace(context.EncodingCatalogWarning))
            RenderEncodeOptionsTable(context, state);
        else if (announceFallback)
            RenderEncodeOptionsTable(context, state);
        else
            RenderEncodeOptionsTable(context, state);

        if (!string.IsNullOrWhiteSpace(context.EncodingCatalogWarning))
            WriteStatus(StatusPrefix.Warning, context.EncodingCatalogWarning);

        WriteStatus(StatusPrefix.Info, "Type 'help' for session commands, 'help <OPTION>' for one option, 'show options' to reprint the table.");

        while (true)
        {
            AnsiConsole.WriteLine();

            string? input;
            try
            {
                input = ReadLineWithEditor(
                    $"[{UiColors.Header}]washmachine[/] [{UiColors.Accent}]encode[/] [{UiColors.Accent}]>[/] ",
                    "encode-session",
                    EncodeSessionCompletionCommand);
            }
            catch (InvalidOperationException)
            {
                break;
            }

            if (input is null)
                break;

            var line = input.Trim();
            if (line.Length == 0)
                continue;

            var tokens = TokenizeLine(line);
            if (tokens.Length == 0)
                continue;

            var command = tokens[0].ToLowerInvariant();
            if (IsHelpToken(command))
            {
                HandleEncodeHelp(context, tokens);
                continue;
            }
            try
            {
                switch (command)
                {
                    case "show" when tokens.Length >= 2 && tokens[1].Equals("options", StringComparison.OrdinalIgnoreCase):
                        RenderEncodeOptionsTable(context, state);
                        break;

                    case "show":
                        await RunShowAsync(tokens.Skip(1).ToArray());
                        break;

                    case "clear" or "cls":
                        try { AnsiConsole.Clear(); } catch { /* non-interactive */ }
                        break;

                    case "add":
                        await HandleEncodeAddAsync(context, state, tokens);
                        break;

                    case "remove":
                        HandleEncodeRemoveSnippet(context, state, tokens);
                        break;

                    case "cat":
                        await HandleEncodeCatAsync(context, state, tokens);
                        break;

                    case "set":
                        await HandleEncodeSetAsync(context, state, tokens);
                        break;

                    case "unset":
                        HandleEncodeUnset(state, tokens);
                        break;

                    case "get":
                        HandleEncodeGet(state, tokens);
                        break;

                    case "help":
                        HandleEncodeHelp(context, tokens);
                        break;

                    case "reset":
                        state = new EncodeSessionState();
                        WriteStatus(StatusPrefix.Success, "All encode options reset to defaults.");
                        break;

                    case "run" or "build":
                    {
                        var validation = ValidateEncodeStateForBuild(context, state);
                        if (validation.MissingRequired.Count > 0 || validation.Invalid.Count > 0)
                        {
                            PrintEncodeBuildValidation(validation);
                            break;
                        }

                        if (!state.Json)
                        {
                            WriteStatus(StatusPrefix.Info, "Final configuration:");
                            RenderEncodeOptionsTable(context, state);
                            WriteStatus(StatusPrefix.Info, "Building...");
                        }

                        try
                        {
                            await ExecuteEncodePipelineAsync(context, state);
                        }
                        catch (Exception ex)
                        {
                            WriteStatus(StatusPrefix.Failure, $"Fatal error during build: {ex.Message}");
                        }

                        break;
                    }

                    case "exit" or "quit":
                        return 0;

                    default:
                        WriteStatus(StatusPrefix.Failure, SuggestSessionCommand(tokens[0]));
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                WriteStatus(StatusPrefix.Info, "Operation cancelled.");
            }
            catch (Exception ex)
            {
                WriteStatus(StatusPrefix.Failure, $"Unexpected error: {ex.Message}");
            }
        }

        return 0;
    }

    private static async Task HandleEncodeSetAsync(EncodeSessionContext context, EncodeSessionState state, string[] tokens)
    {
        tokens = NormalizeSetTokens(tokens, out _);

        if (tokens.Length < 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: set <OPTION> <VALUE>   (also accepted: set <OPTION>=<VALUE>)");
            return;
        }

        // Help shortcut: 'set OPT --help' / 'set OPT -h' route to per-option help.
        if (tokens.Length >= 3 && IsHelpToken(tokens[2]))
        {
            HandleEncodeHelp(context, new[] { "help", tokens[1] });
            return;
        }

        if (!TryResolveEncodeOption(tokens[1], out var spec, out var resolutionError))
        {
            // Fall back to snippet text input keys
            string rawKey = tokens[1].Trim();
            if (context.KnownTextInputs.Contains(rawKey))
            {
                if (tokens.Length == 2)
                {
                    WriteStatus(StatusPrefix.Info, $"{rawKey} is a snippet text input (optional).");
                    WriteStatus(StatusPrefix.Info, $"Example: set {rawKey} myvalue");
                    return;
                }
                string textValue = string.Join(' ', tokens.Skip(2));
                state.TextInputs[rawKey] = textValue;
                WriteStatus(StatusPrefix.Success, $"{rawKey} => {textValue}");
                return;
            }
            WriteStatus(StatusPrefix.Failure, resolutionError!);
            return;
        }

        if (tokens.Length == 2)
        {
            // Interactive picker for catalog-backed options
            if (spec!.Name is "ENCODER" or "ENVELOPE" && context.EncodingCatalog is not null)
            {
                await HandleEncodeCatalogPickerAsync(context, state, spec);
                return;
            }
            if (spec!.Name == "SNIPPETS")
            {
                await HandleEncodeSnippetAsync(context, state, null);
                return;
            }
            if (spec!.Name == "PAYLOAD_SOURCE")
            {
                await HandleEncodePayloadSourceInteractivePicker(context, state);
                return;
            }
            if (spec!.Name == "TEMPLATE")
            {
                await HandleEncodeTemplatePicker(context, state);
                return;
            }
            if (spec!.Name == "SHELLCODE_EXECUTION")
            {
                await HandleEncodeShellcodeExecutionAsync(context, state, null);
                return;
            }
            PrintEncodeOptionRequiredInfo(spec!);
            await ShowEncodeOptionValuesAsync(context, spec!);
            return;
        }

        // SNIPPETS with value: redirect to unified snippet handler
        if (spec!.Name == "SNIPPETS")
        {
            string snippetArg = string.Join(' ', tokens.Skip(2)).Trim();
            await HandleEncodeSnippetAsync(context, state, snippetArg);
            return;
        }

        // SHELLCODE_EXECUTION with value: redirect to dedicated handler
        if (spec!.Name == "SHELLCODE_EXECUTION")
        {
            string execArg = string.Join(' ', tokens.Skip(2)).Trim();
            await HandleEncodeShellcodeExecutionAsync(context, state, execArg);
            return;
        }

        string value = string.Join(' ', tokens.Skip(2));
        if (!TryApplyEncodeOptionValue(context, state, spec!, value, appendCollectionValues: false, allowReplacingPayloadSource: true, out var message))
        {
            WriteStatus(StatusPrefix.Failure, $"{spec!.Name}: {message}. Expected: {GetExpectedValueText(context, spec)}. Example: {spec.Example}");
            return;
        }

        WriteStatus(StatusPrefix.Success, $"{spec!.Name} => {GetCurrentOptionValue(state, spec)}");
        RenderEncodeOptionsTable(context, state);
        await Task.CompletedTask;
    }

    private static async Task HandleEncodeCatalogPickerAsync(EncodeSessionContext context, EncodeSessionState state, EncodeOptionSpec spec)
    {
        bool isEncoder = spec.Name == "ENCODER";
        var catalog = isEncoder ? context.EncodingCatalog!.Encoders : context.EncodingCatalog!.Envelopes;
        await RunShowAsync([isEncoder ? "encoders" : "envelopes"]);
        AnsiConsole.WriteLine();
        string rawChoice = AnsiConsole.Ask<string>($"[{UiColors.Accent}]Choose {spec.Name} (ID or name, 0 for none):[/]");

        // Try numeric ID first
        if (int.TryParse(rawChoice, NumberStyles.Integer, CultureInfo.InvariantCulture, out int chosenId))
        {
            if (chosenId == 0 || catalog.Any(e => e.Index == chosenId))
            {
                if (isEncoder) state.Encoder = chosenId;
                else state.Envelope = chosenId;
                WriteStatus(StatusPrefix.Success, $"{spec.Name} => {GetCurrentOptionValue(state, spec)}");
                RenderEncodeOptionsTable(context, state);
                return;
            }
            WriteStatus(StatusPrefix.Failure, $"{spec.Name}: ID {chosenId} not found in catalog.");
            return;
        }

        // Try name match
        var exactMatch = catalog.FirstOrDefault(e => string.Equals(e.DisplayText, rawChoice, StringComparison.OrdinalIgnoreCase));
        if (exactMatch is not null)
        {
            if (isEncoder) state.Encoder = exactMatch.Index;
            else state.Envelope = exactMatch.Index;
            WriteStatus(StatusPrefix.Success, $"{spec.Name} => {GetCurrentOptionValue(state, spec)}");
            RenderEncodeOptionsTable(context, state);
            return;
        }

        var partialMatches = catalog
            .Where(e => e.DisplayText.Contains(rawChoice, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (partialMatches.Length == 1)
        {
            if (isEncoder) state.Encoder = partialMatches[0].Index;
            else state.Envelope = partialMatches[0].Index;
            WriteStatus(StatusPrefix.Success, $"{spec.Name} => {GetCurrentOptionValue(state, spec)}");
            RenderEncodeOptionsTable(context, state);
            return;
        }
        if (partialMatches.Length > 1)
        {
            WriteStatus(StatusPrefix.Failure, $"Ambiguous choice '{rawChoice}'. Matches: {string.Join(", ", partialMatches.Select(e => e.DisplayText))}");
            return;
        }
        WriteStatus(StatusPrefix.Failure, $"No {spec.Name} found matching '{rawChoice}'.");
    }

    private static void PrintEncodeOptionRequiredInfo(EncodeOptionSpec spec)
    {
        if (spec.Required)
            WriteStatus(StatusPrefix.Warning, $"{spec.Name} is required.");
        else
        {
            string defVal = spec.DefaultValue is null ? "not set" : spec.DefaultValue;
            WriteStatus(StatusPrefix.Info, $"{spec.Name} is optional (default: {defVal}).");
        }
    }

    private static void HandleEncodeUnset(EncodeSessionState state, string[] tokens)
    {
        if (tokens.Length != 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: unset <OPTION>.");
            return;
        }

        if (!TryResolveEncodeOption(tokens[1], out var spec, out var resolutionError))
        {
            WriteStatus(StatusPrefix.Failure, resolutionError!);
            return;
        }

        ResetEncodeOption(state, spec!.Name);
        WriteStatus(StatusPrefix.Success, $"{spec.Name} => {GetCurrentOptionValue(state, spec)}");
    }

    private static void HandleEncodeGet(EncodeSessionState state, string[] tokens)
    {
        if (tokens.Length != 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: get <OPTION>.");
            return;
        }

        if (!TryResolveEncodeOption(tokens[1], out var spec, out var resolutionError))
        {
            WriteStatus(StatusPrefix.Failure, resolutionError!);
            return;
        }

        WriteStatus(StatusPrefix.Info, $"{spec!.Name} => {GetCurrentOptionValue(state, spec)}");
    }

    private static async Task ShowEncodeOptionValuesAsync(EncodeSessionContext context, EncodeOptionSpec spec)
    {
        switch (spec.Name)
        {
            case "TEMPLATE":
                await RunShowAsync(["templates"]);
                WriteStatus(StatusPrefix.Info, "Use one of the template names above with: set TEMPLATE <name>");
                return;

            case "SHELLCODE_EXECUTION":
                await RunShowAsync(["execution"]);
                WriteStatus(StatusPrefix.Info, "Use: set SHELLCODE_EXECUTION <technique_name_or_id>  or  set SHELLCODE_EXECUTION (interactive)");
                return;

            case "ENCODER":
                await RunShowAsync(["encoders"]);
                WriteStatus(StatusPrefix.Info, "Use one of the numeric encoder IDs above with: set ENCODER <id>");
                return;

            case "ENVELOPE":
                await RunShowAsync(["envelopes"]);
                WriteStatus(StatusPrefix.Info, "Use one of the numeric envelope IDs above with: set ENVELOPE <id>");
                return;

            case "SNIPPETS":
                await RunShowAsync(["modules"]);
                WriteStatus(StatusPrefix.Info, "Use: add snippet module/<category>/<id>  or  add snippet <global_id>  or  add snippet (interactive)");
                return;

            case "PAYLOAD_SOURCE":
                RenderSessionChoiceCatalog("Possible values for PAYLOAD_SOURCE",
                [
                    new SessionValueChoice("file:<path>", "Read raw shellcode bytes from a local .bin file."),
                    new SessionValueChoice("hex:<hex>", "Use inline raw hex bytes."),
                    new SessionValueChoice("url:<http(s)://...>", "Fetch payload bytes at runtime from a URL."),
                ], "Use: set PAYLOAD_SOURCE <value>");
                return;

            case "SHIKATA_GA_NAI":
            case "VERBOSE":
            case "JSON":
                RenderSessionChoiceCatalog($"Possible values for {spec.Name}", BuildBooleanChoices("Enable the option.", "Disable the option.").ToArray(), $"Use: set {spec.Name} <true|false>");
                return;

            case "CLONE_RESOURCES":
            case "CLONE_ICON":
                RenderSessionChoiceCatalog($"Possible values for {spec.Name}", BuildTriStateChoices("Follow the default encode behavior.", "Force the option on.", "Force the option off.").ToArray(), $"Use: set {spec.Name} <auto|true|false>");
                return;

            case "CLONE_METADATA":
                RenderSessionChoiceCatalog("Possible values for CLONE_METADATA", BuildBooleanChoices("Enable cloning — reveals CLONE_FROM, CLONE_RESOURCES, CLONE_ICON.", "Disable cloning (default).").ToArray(), "Use: set CLONE_METADATA <true|false>");
                return;

            case "SHIKATA_PLACEMENT":
                RenderSessionChoiceCatalog("Possible values for SHIKATA_PLACEMENT",
                [
                    new SessionValueChoice("pre", "Run SGN before the normal Bin2Shell stage."),
                    new SessionValueChoice("post", "Run SGN after the normal Bin2Shell stage."),
                ], "Use: set SHIKATA_PLACEMENT <pre|post>");
                return;

            default:
                WriteStatus(StatusPrefix.Info, $"{spec.Name} expects: {GetExpectedValueText(context, spec)}");
                WriteStatus(StatusPrefix.Info, $"Example: {spec.Example}");
                return;
        }
    }

    private static void HandleEncodeHelp(EncodeSessionContext context, string[] tokens)
    {
        if (tokens.Length == 1)
        {
            WriteStatus(StatusPrefix.Info, "Encode parameters:");
            foreach (var optionSpec in EncodeOptionSpecs)
            {
                Console.WriteLine(optionSpec.Name);
                Console.WriteLine($"  current:  {GetDefaultHelpValue(optionSpec)}");
                Console.WriteLine($"  required: {(optionSpec.Required ? "yes" : "no")}");
                Console.WriteLine($"  format:   {GetExpectedValueText(context, optionSpec)}");
                Console.WriteLine($"  example:  {optionSpec.Example}");
                Console.WriteLine($"  desc:     {optionSpec.Details}");
                Console.WriteLine();
            }

            Console.WriteLine("Commands: show options | show <catalog> | set <OPTION> [VALUE] | unset <OPTION> | get <OPTION> | help <OPTION> | reset | run | exit");
            return;
        }

        if (tokens.Length != 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: help <OPTION>.");
            return;
        }

        if (!TryResolveEncodeOption(tokens[1], out var spec, out var resolutionError))
        {
            WriteStatus(StatusPrefix.Failure, resolutionError!);
            return;
        }

        Console.WriteLine(spec!.Name);
        Console.WriteLine($"  required: {(spec.Required ? "yes" : "no")}");
        Console.WriteLine($"  default:  {GetDefaultHelpValue(spec)}");
        Console.WriteLine($"  format:   {GetExpectedValueText(context, spec)}");
        Console.WriteLine($"  example:  {spec.Example}");
        Console.WriteLine($"  desc:     {spec.Details}");
    }

    private static void PrintEncodeBuildValidation(EncodeBuildValidation validation)
    {
        if (validation.Invalid.Count > 0)
        {
            foreach (var invalid in validation.Invalid)
                WriteStatus(StatusPrefix.Failure, invalid);
        }

        if (validation.MissingRequired.Count == 0)
            return;

        WriteStatus(StatusPrefix.Failure, "Missing required parameter(s):");
        foreach (var spec in validation.MissingRequired)
            Console.WriteLine($"  {spec.Name,-20} {spec.Description}");
    }

    private static EncodeParseOutcome ParseEncodeOneLiner(EncodeSessionContext context, string[] args)
    {
        var state = new EncodeSessionState();
        var messages = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-Shellcode" or "--shellcode" or "-s":
                    if (!TryReadFlagValue(args, ref i, out var fileValue))
                    {
                        messages.Add(BuildExpectedMessage("PAYLOAD_SOURCE", "(missing)", "missing value after -Shellcode", context));
                        break;
                    }

                    if (!TryApplyPayloadSource(context, state, $"file:{fileValue}", allowReplacingPayloadSource: false, out var fileError))
                        messages.Add(BuildExpectedMessage("PAYLOAD_SOURCE", fileValue!, fileError!, context));
                    break;

                case "-ShellcodeHex" or "--shellcode-hex":
                    if (!TryReadFlagValue(args, ref i, out var hexValue))
                    {
                        messages.Add(BuildExpectedMessage("PAYLOAD_SOURCE", "(missing)", "missing value after -ShellcodeHex", context));
                        break;
                    }

                    if (!TryApplyPayloadSource(context, state, $"hex:{hexValue}", allowReplacingPayloadSource: false, out var hexError))
                        messages.Add(BuildExpectedMessage("PAYLOAD_SOURCE", hexValue!, hexError!, context));
                    break;

                case "-ShellcodeUrl" or "--shellcode-url" or "-u":
                    if (!TryReadFlagValue(args, ref i, out var urlValue))
                    {
                        messages.Add(BuildExpectedMessage("PAYLOAD_SOURCE", "(missing)", "missing value after -ShellcodeUrl", context));
                        break;
                    }

                    if (!TryApplyPayloadSource(context, state, $"url:{urlValue}", allowReplacingPayloadSource: false, out var urlError))
                        messages.Add(BuildExpectedMessage("PAYLOAD_SOURCE", urlValue!, urlError!, context));
                    break;

                case "-Template" or "--template" or "-t":
                    ParseNamedOption(context, state, args, ref i, "TEMPLATE", messages);
                    break;

                case "-Encoder" or "--encoder" or "-e":
                    ParseNamedOption(context, state, args, ref i, "ENCODER", messages);
                    break;

                case "-Envelope" or "--envelope" or "-v":
                    ParseNamedOption(context, state, args, ref i, "ENVELOPE", messages);
                    break;

                case "-Sgn" or "-ShikataGaNai" or "--shikata-ga-nai" or "--sgn":
                    if (!TryApplyEncodeOptionValue(context, state, EncodeOptionSpecsByName["SHIKATA_GA_NAI"], bool.TrueString, false, true, out var sgnError))
                        messages.Add(BuildExpectedMessage("SHIKATA_GA_NAI", "true", sgnError!, context));
                    break;

                case "-SgnCount" or "--shikata-enc":
                    ParseNamedOption(context, state, args, ref i, "SHIKATA_ENCODE_COUNT", messages);
                    break;

                case "-SgnMax" or "--shikata-max":
                    ParseNamedOption(context, state, args, ref i, "SHIKATA_MAX_BYTES", messages);
                    break;

                case "-SgnPlacement" or "--sgn-placement":
                    ParseNamedOption(context, state, args, ref i, "SHIKATA_PLACEMENT", messages);
                    break;

                case "-CloneFrom" or "--clone-from":
                    ParseNamedOption(context, state, args, ref i, "CLONE_FROM", messages);
                    break;

                case "-CloneResources" or "--clone-resources":
                    if (!TryApplyEncodeOptionValue(context, state, EncodeOptionSpecsByName["CLONE_RESOURCES"], "true", false, true, out var cloneResourcesError))
                        messages.Add(BuildExpectedMessage("CLONE_RESOURCES", "true", cloneResourcesError!, context));
                    break;

                case "-NoCloneResources" or "--no-clone-resources":
                    if (!TryApplyEncodeOptionValue(context, state, EncodeOptionSpecsByName["CLONE_RESOURCES"], "false", false, true, out var noCloneResourcesError))
                        messages.Add(BuildExpectedMessage("CLONE_RESOURCES", "false", noCloneResourcesError!, context));
                    break;

                case "-CloneIcon" or "--clone-icon":
                    if (!TryApplyEncodeOptionValue(context, state, EncodeOptionSpecsByName["CLONE_ICON"], "true", false, true, out var cloneIconError))
                        messages.Add(BuildExpectedMessage("CLONE_ICON", "true", cloneIconError!, context));
                    break;

                case "-NoCloneIcon" or "--no-clone-icon":
                    if (!TryApplyEncodeOptionValue(context, state, EncodeOptionSpecsByName["CLONE_ICON"], "false", false, true, out var noCloneIconError))
                        messages.Add(BuildExpectedMessage("CLONE_ICON", "false", noCloneIconError!, context));
                    break;

                case "-CloneMetadata" or "--clone-metadata":
                    if (!TryApplyEncodeOptionValue(context, state, EncodeOptionSpecsByName["CLONE_METADATA"], "true", false, true, out var cloneMetadataError))
                        messages.Add(BuildExpectedMessage("CLONE_METADATA", "true", cloneMetadataError!, context));
                    break;

                case "-NoCloneMetadata" or "--no-clone-metadata":
                    if (!TryApplyEncodeOptionValue(context, state, EncodeOptionSpecsByName["CLONE_METADATA"], "false", false, true, out var noCloneMetadataError))
                        messages.Add(BuildExpectedMessage("CLONE_METADATA", "false", noCloneMetadataError!, context));
                    break;

                case "-PadNops" or "--pad-nops":
                    ParseNamedOption(context, state, args, ref i, "PAD_NOPS", messages);
                    break;

                case "-Snippet" or "--snippet":
                    ParseNamedOption(context, state, args, ref i, "SNIPPETS", messages, appendCollectionValues: true);
                    break;

                case "-Text" or "--text":
                    ParseNamedOption(context, state, args, ref i, "TEXT_INPUTS", messages, appendCollectionValues: true);
                    break;

                case "-Verbose" or "--verbose":
                    if (!TryApplyEncodeOptionValue(context, state, EncodeOptionSpecsByName["VERBOSE"], "true", false, true, out var verboseError))
                        messages.Add(BuildExpectedMessage("VERBOSE", "true", verboseError!, context));
                    break;

                case "-Json" or "--json":
                    if (!TryApplyEncodeOptionValue(context, state, EncodeOptionSpecsByName["JSON"], "true", false, true, out var jsonError))
                        messages.Add(BuildExpectedMessage("JSON", "true", jsonError!, context));
                    break;

                case "-Backend" or "--backend":
                    if (!TryReadFlagValue(args, ref i, out var backendValue))
                    {
                        messages.Add("Missing value after -Backend. Expected: Deterministic | LlvmObfuscated");
                        break;
                    }
                    if (!backendValue!.Equals("Deterministic", StringComparison.OrdinalIgnoreCase) &&
                        !backendValue.Equals("LlvmObfuscated", StringComparison.OrdinalIgnoreCase))
                    {
                        messages.Add($"Invalid -Backend value '{backendValue}'. Expected: Deterministic | LlvmObfuscated");
                        break;
                    }
                    state.CompilationBackend = backendValue;
                    break;

                case "-LlvmPass" or "--llvm-pass":
                    if (!TryReadFlagValue(args, ref i, out var passValue))
                    {
                        messages.Add("Missing value after -LlvmPass. Expected: a pass ID such as 'control-flow-flattening'.");
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(passValue) && !state.LlvmPasses.Contains(passValue!, StringComparer.OrdinalIgnoreCase))
                        state.LlvmPasses.Add(passValue!);
                    break;

                default:
                    return new EncodeParseOutcome(state, messages, Array.Empty<EncodeOptionSpec>(), $"Unknown option: {arg}", RequiresInteractiveFallback: false);
            }
        }

        var validation = ValidateEncodeStateForBuild(context, state);
        bool requiresFallback = messages.Count > 0 || validation.MissingRequired.Count > 0 || validation.Invalid.Count > 0;
        return new EncodeParseOutcome(state, messages, validation.MissingRequired, null, requiresFallback);
    }

    private static void ParseNamedOption(
        EncodeSessionContext context,
        EncodeSessionState state,
        string[] args,
        ref int index,
        string optionName,
        ICollection<string> messages,
        bool appendCollectionValues = false)
    {
        if (!TryReadFlagValue(args, ref index, out var value))
        {
            messages.Add(BuildExpectedMessage(optionName, "(missing)", $"missing value after {args[index]}", context));
            return;
        }

        if (!TryApplyEncodeOptionValue(
                context,
                state,
                EncodeOptionSpecsByName[optionName],
                value!,
                appendCollectionValues,
                allowReplacingPayloadSource: true,
                out var error))
        {
            messages.Add(BuildExpectedMessage(optionName, value!, error!, context));
        }
    }

    private static bool TryReadFlagValue(string[] args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Length)
            return false;

        value = args[++index];
        return true;
    }

    private static string BuildExpectedMessage(string optionName, string receivedValue, string reason, EncodeSessionContext context)
    {
        var spec = EncodeOptionSpecsByName[optionName];
        string detail = reason.StartsWith("invalid value", StringComparison.OrdinalIgnoreCase)
            ? reason
            : $"invalid value '{receivedValue}'. {reason}";
        return $"{spec.Name}: {detail}. Expected: {GetExpectedValueText(context, spec)}. Example: {spec.Example}";
    }

    private static EncodeBuildValidation ValidateEncodeStateForBuild(EncodeSessionContext context, EncodeSessionState state)
    {
        var missing = new List<EncodeOptionSpec>();
        var invalid = new List<string>();

        if (state.PayloadSource is null)
            missing.Add(EncodeOptionSpecsByName["PAYLOAD_SOURCE"]);

        if (!context.SnippetService.TryGetTemplate(state.Template, out _))
            invalid.Add($"TEMPLATE: unknown value '{state.Template}'. Expected: {GetExpectedValueText(context, EncodeOptionSpecsByName["TEMPLATE"])}. Example: minimal");

        if (!ValidateCatalogIndex(state.Encoder, context.EncodingCatalog?.Encoders))
            invalid.Add($"ENCODER: invalid value '{state.Encoder}'. Expected: {GetExpectedValueText(context, EncodeOptionSpecsByName["ENCODER"])}. Example: 1");

        if (!ValidateCatalogIndex(state.Envelope, context.EncodingCatalog?.Envelopes))
            invalid.Add($"ENVELOPE: invalid value '{state.Envelope}'. Expected: {GetExpectedValueText(context, EncodeOptionSpecsByName["ENVELOPE"])}. Example: 1");

        if (!string.IsNullOrWhiteSpace(state.CloneFrom) && !File.Exists(state.CloneFrom))
            invalid.Add($@"CLONE_FROM: invalid value '{state.CloneFrom}'. Expected: existing .exe path. Example: C:\samples\donor.exe");

        if (string.IsNullOrWhiteSpace(state.CloneFrom) &&
            (state.CloneResources == EncodeTriState.Enabled || state.CloneIcon == EncodeTriState.Enabled || state.CloneMetadata == EncodeTriState.Enabled))
        {
            invalid.Add("CLONE_FROM: required when CLONE_METADATA, CLONE_RESOURCES, or CLONE_ICON is enabled. Example: C:\\samples\\donor.exe");
        }

        return new EncodeBuildValidation(missing, invalid);
    }

    private static async Task<int> ExecuteEncodePipelineAsync(EncodeSessionContext context, EncodeSessionState state)
    {
        string? shellcodeFilePath = state.PayloadSource?.Kind == EncodePayloadSourceKind.File ? state.PayloadSource.Value : null;
        string? shellcodeHex = state.PayloadSource?.Kind == EncodePayloadSourceKind.Hex ? state.PayloadSource.Value : null;
        string? shellcodeUrl = state.PayloadSource?.Kind == EncodePayloadSourceKind.Url ? state.PayloadSource.Value : null;

        var logger = new ConsoleLogger();
        if (state.Verbose)
            logger.VerboseEnabled = true;

        var provisioner = new RequirementProvisioner(context.Paths, logger);
        await provisioner.EnsureRequirementsAsync(
            new ConsoleProgressReporter(),
            includeOptionalTools: true);

        var runner = new Bin2ShellRunner(context.Paths);
        var toolLocator = new CompilerToolLocator(logger);
        var compiler = new CompilerService(context.Paths, runner, context.SnippetService, toolLocator, logger);

        var textBoxes = new Dictionary<string, string>
        {
            [UiDataKeys.ShellcodeFile] = shellcodeFilePath ?? string.Empty,
            [UiDataKeys.ShellcodeRaw] = shellcodeHex ?? string.Empty,
            [UiDataKeys.ShellcodeUrl] = shellcodeUrl ?? string.Empty,
            [UiDataKeys.ShellcodeUrlFile] = string.Empty,
            [UiDataKeys.ShikataGaNaiEnabled] = state.ShikataGaNai ? bool.TrueString : bool.FalseString,
            [UiDataKeys.ShikataGaNaiEncodeCount] = state.ShikataEncodeCount.ToString(CultureInfo.InvariantCulture),
            [UiDataKeys.ShikataGaNaiMaxBytes] = state.ShikataMaxBytes.ToString(CultureInfo.InvariantCulture),
            [UiDataKeys.ShikataGaNaiPlacement] = state.ShikataPlacement,
        };

        var comboBoxes = new Dictionary<string, string>
        {
            [UiDataKeys.Template] = state.Template,
            [UiDataKeys.CompilationBackend] = state.CompilationBackend,
        };

        var encodingCatalogService = new ShellcodeEncodingCatalogService(runner, context.Paths);
        try
        {
            var catalog = await encodingCatalogService.GetCatalogAsync();
            context.EncodingCatalog = catalog;
            context.EncodingCatalogWarning = null;
            comboBoxes[UiDataKeys.Encoder] = ResolveEncodingDisplayText(state.Encoder, catalog.Encoders);
            comboBoxes[UiDataKeys.Envelope] = ResolveEncodingDisplayText(state.Envelope, catalog.Envelopes);
        }
        catch (Exception ex)
        {
            logger.Warn($"Could not load encoding catalog: {ex.Message}");
            comboBoxes[UiDataKeys.Encoder] = state.Encoder == 0 ? "0 - none" : $"{state.Encoder} - custom";
            comboBoxes[UiDataKeys.Envelope] = state.Envelope == 0 ? "0 - none" : $"{state.Envelope} - custom";
        }

        var listBoxes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // LLVM obfuscation passes
        if (state.LlvmPasses.Count > 0)
            listBoxes[UiDataKeys.LlvmObfuscationPasses] = state.LlvmPasses.ToList();

        foreach (var kv in state.Snippets)
        {
            if (kv.Key.StartsWith("snippetCombo_", StringComparison.OrdinalIgnoreCase))
            {
                comboBoxes[kv.Key] = kv.Value.FirstOrDefault() ?? string.Empty;
            }
            else if (kv.Key.StartsWith("snippetList_", StringComparison.OrdinalIgnoreCase))
            {
                listBoxes[kv.Key] = kv.Value.ToList();
            }
            else if (TryResolveSnippetSection(context, kv.Key, out var section, out _))
            {
                if (section!.AllowMultiple)
                {
                    string listName = SnippetControlNaming.GetListName(section, 0);
                    listBoxes[listName] = kv.Value.ToList();
                }
                else
                {
                    string comboName = SnippetControlNaming.GetComboName(section, 0);
                    comboBoxes[comboName] = kv.Value.FirstOrDefault() ?? string.Empty;
                }
            }
        }

        if (context.SnippetService.TryGetTemplate(comboBoxes[UiDataKeys.Template], out var template))
        {
            foreach (var placeholder in template.Placeholders.Where(p => p.Kind == TemplatePlaceholderKind.Snippet))
            {
                if (!TryResolveSnippetSection(context, placeholder.SnippetTemplateKey, out var section, out _))
                    continue;

                if (section!.AllowMultiple)
                {
                    string listName = SnippetControlNaming.GetListName(section, 0);
                    if (!listBoxes.ContainsKey(listName))
                    {
                        var defaultItem = section.Items.FirstOrDefault(item => item.IsDefault) ?? section.Items.FirstOrDefault();
                        if (defaultItem is not null)
                            listBoxes[listName] = [defaultItem.Id];
                    }
                }
                else
                {
                    string comboName = SnippetControlNaming.GetComboName(section, 0);
                    if (!comboBoxes.ContainsKey(comboName))
                    {
                        var defaultItem = section.Items.FirstOrDefault(item => item.IsDefault) ?? section.Items.FirstOrDefault();
                        if (defaultItem is not null)
                            comboBoxes[comboName] = defaultItem.Id;
                    }
                }
            }
        }

        foreach (var kv in state.TextInputs)
        {
            textBoxes[kv.Key] = kv.Value;
            logger.Debug($"Merged text input: {kv.Key}={kv.Value}");
        }

        foreach (var section in context.SnippetService.GetAllSections())
        {
            foreach (var input in section.Inputs)
            {
                if (!string.IsNullOrWhiteSpace(input.DefaultValue) && !textBoxes.ContainsKey(input.Id))
                    textBoxes[input.Id] = input.DefaultValue;
            }

            foreach (var item in section.Items)
            {
                foreach (var input in item.Inputs)
                {
                    var scopedKey = CompilerService.BuildScopedInputKey(section.Template, item.Id, input.Id);
                    if (!string.IsNullOrWhiteSpace(input.DefaultValue) && !textBoxes.ContainsKey(scopedKey))
                        textBoxes[scopedKey] = input.DefaultValue;
                }
            }
        }

        var data = new UiData(textBoxes, comboBoxes, listBoxes);

        try
        {
            var result = state.Json
                ? await compiler.CompileAsync(data)
                : await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan"))
                    .StartAsync("Encoding...", async _ => await compiler.CompileAsync(data));

            var postCompileNotes = new List<string>();
            bool wantsPostCompile = !string.IsNullOrWhiteSpace(state.CloneFrom) || state.PadNops.HasValue;
            if (wantsPostCompile &&
                result.Success &&
                !string.IsNullOrWhiteSpace(result.OutputExePath) &&
                File.Exists(result.OutputExePath))
            {
                var postCompile = new PePostCompileService(logger);
                var options = new PostCompileOptions(
                    state.CloneFrom,
                    ResolveTriState(state.CloneResources, state.CloneFrom),
                    ResolveTriState(state.CloneIcon, state.CloneFrom),
                    ResolveTriState(state.CloneMetadata, state.CloneFrom),
                    state.PadNops ?? 0);
                postCompileNotes.AddRange(postCompile.Apply(result.OutputExePath, options));
            }

            if (state.Json)
            {
                var output = new
                {
                    result.Success,
                    result.OutputExePath,
                    result.GeneratedSourcePath,
                    Notes = result.Notes,
                    PostCompileNotes = postCompileNotes,
                    CompilerPath = result.Discovery?.Best?.Path,
                    ConversionSuccess = result.ConversionResult.Success,
                    ConversionError = result.ConversionResult.Error,
                };
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(output, JsonPrint));
            }
            else
            {
                foreach (var note in result.Notes)
                    logger.Info(note);
                foreach (var note in postCompileNotes)
                    logger.Info(note);

                if (result.Success)
                {
                    AnsiConsole.Write(new Panel("[green3_1]Encoding succeeded.[/]")
                        .BorderColor(Color.Green)
                        .Border(BoxBorder.Rounded));
                }
                else
                {
                    AnsiConsole.Write(new Panel($"[red]Encoding failed: {Markup.Escape(result.ConversionResult.Error ?? "unknown")}[/]")
                        .BorderColor(Color.Red)
                        .Border(BoxBorder.Rounded));
                }
            }

            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            logger.Error($"Fatal: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveEncodingDisplayText(int index, IReadOnlyList<ShellcodeEncodingItem> items)
    {
        if (index == 0)
            return "0 - none";

        var item = items.FirstOrDefault(candidate => candidate.Index == index);
        return item?.DisplayText ?? $"{index} - custom";
    }

    private static bool ResolveTriState(EncodeTriState state, string? cloneFrom)
    {
        return state switch
        {
            EncodeTriState.Enabled => true,
            EncodeTriState.Disabled => false,
            _ => !string.IsNullOrWhiteSpace(cloneFrom),
        };
    }

    private static bool TryResolveEncodeOption(string input, out EncodeOptionSpec? spec, out string? error)
    {
        spec = null;
        error = null;

        string normalized = NormalizeOptionName(input);
        if (EncodeOptionSpecsByName.TryGetValue(normalized, out spec))
            return true;

        var matches = EncodeOptionSpecs
            .Where(candidate => candidate.Name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 1)
        {
            spec = matches[0];
            return true;
        }

        if (matches.Length > 1)
        {
            error = $"Ambiguous option: {input}. Matches: {string.Join(", ", matches.Select(match => match.Name))}.";
            return false;
        }

        error = $"Unknown option: {input}. Type 'show options' to see available options.";
        return false;
    }

    private static string NormalizeOptionName(string value)
        => value.Trim().Replace('-', '_').Replace(' ', '_').ToUpperInvariant();

    private static bool TryApplyEncodeOptionValue(
        EncodeSessionContext context,
        EncodeSessionState state,
        EncodeOptionSpec spec,
        string rawValue,
        bool appendCollectionValues,
        bool allowReplacingPayloadSource,
        out string? error)
    {
        error = null;
        switch (spec.Name)
        {
            case "PAYLOAD_SOURCE":
                return TryApplyPayloadSource(context, state, rawValue, allowReplacingPayloadSource, out error);

            case "TEMPLATE":
            {
                var templateId = rawValue.Trim();
                if (!context.SnippetService.TryGetTemplate(templateId, out _))
                {
                    error = $"invalid value '{rawValue}'. Expected one of: {string.Join(", ", context.SnippetService.GetTemplates().Select(template => template.Id))}";
                    return false;
                }

                state.Template = templateId;
                return true;
            }

            case "ENCODER":
                return TryApplyCatalogIndex(rawValue, context.EncodingCatalog?.Encoders, value => state.Encoder = value, out error);

            case "ENVELOPE":
                return TryApplyCatalogIndex(rawValue, context.EncodingCatalog?.Envelopes, value => state.Envelope = value, out error);

            case "SHIKATA_GA_NAI":
                if (!TryParseBoolean(rawValue, out var sgnEnabled))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }

                state.ShikataGaNai = sgnEnabled;
                return true;

            case "SHIKATA_ENCODE_COUNT":
                if (!TryParsePositiveInt(rawValue, out var sgnCount))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }

                state.ShikataEncodeCount = sgnCount;
                state.ShikataGaNai = true;
                return true;

            case "SHIKATA_MAX_BYTES":
                if (!TryParsePositiveInt(rawValue, out var maxBytes))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }

                state.ShikataMaxBytes = maxBytes;
                state.ShikataGaNai = true;
                return true;

            case "SHIKATA_PLACEMENT":
            {
                var placement = rawValue.Trim().ToLowerInvariant();
                if (placement is not ("pre" or "post"))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }

                state.ShikataPlacement = placement;
                return true;
            }

            case "SNIPPETS":
            {
                if (!TryParseSnippetOverrides(context, rawValue, appendCollectionValues ? state.Snippets : null, out var snippets, out error))
                    return false;

                state.Snippets = snippets;
                return true;
            }

            case "TEXT_INPUTS":
            {
                if (!TryParseTextOverrides(context, rawValue, appendCollectionValues ? state.TextInputs : null, out var textInputs, out error))
                    return false;

                state.TextInputs = textInputs;
                return true;
            }

            case "CLONE_FROM":
            {
                var path = rawValue.Trim().Trim('"');
                if (!File.Exists(path))
                {
                    error = $"invalid value '{rawValue}'. File not found";
                    return false;
                }

                state.CloneFrom = path;
                return true;
            }

            case "CLONE_RESOURCES":
                return TryApplyTriState(rawValue, value => state.CloneResources = value, out error);

            case "CLONE_ICON":
                return TryApplyTriState(rawValue, value => state.CloneIcon = value, out error);

            case "CLONE_METADATA":
                if (!TryParseBoolean(rawValue, out var cloneMeta))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }
                state.CloneMetadata = cloneMeta ? EncodeTriState.Enabled : EncodeTriState.Disabled;
                return true;

            case "PAD_NOPS":
                if (!TryParsePositiveLong(rawValue, out var padNops))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }

                state.PadNops = padNops;
                return true;

            case "VERBOSE":
                if (!TryParseBoolean(rawValue, out var verbose))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }

                state.Verbose = verbose;
                return true;

            case "JSON":
                if (!TryParseBoolean(rawValue, out var json))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }

                state.Json = json;
                return true;

            default:
                error = $"invalid value '{rawValue}'";
                return false;
        }
    }

    private static bool TryApplyPayloadSource(
        EncodeSessionContext context,
        EncodeSessionState state,
        string rawValue,
        bool allowReplacingPayloadSource,
        out string? error)
    {
        error = null;

        int separator = rawValue.IndexOf(':');
        if (separator <= 0 || separator == rawValue.Length - 1)
        {
            error = "expected file:<path>, hex:<hex>, or url:<http(s)://...>";
            return false;
        }

        string kind = rawValue[..separator].Trim().ToLowerInvariant();
        string value = rawValue[(separator + 1)..].Trim();
        if (state.PayloadSource is not null && !allowReplacingPayloadSource)
        {
            error = "multiple payload sources were supplied";
            return false;
        }

        switch (kind)
        {
            case "file":
            {
                string path = value.Trim('"');
                if (!File.Exists(path))
                {
                    error = "file does not exist";
                    return false;
                }

                state.PayloadSource = new EncodePayloadSource(EncodePayloadSourceKind.File, path);
                return true;
            }

            case "hex":
                if (!LooksLikeHexPayload(value))
                {
                    error = "value does not appear to be valid hex";
                    return false;
                }

                state.PayloadSource = new EncodePayloadSource(EncodePayloadSourceKind.Hex, value);
                return true;

            case "url":
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https"))
                {
                    error = "value is not a valid http(s) URL";
                    return false;
                }

                state.PayloadSource = new EncodePayloadSource(EncodePayloadSourceKind.Url, value);
                return true;

            default:
                error = "expected file:<path>, hex:<hex>, or url:<http(s)://...>";
                return false;
        }
    }

    private static bool LooksLikeHexPayload(string value)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(value, "\\\\x([0-9A-Fa-f]{2})");
        if (matches.Count > 0)
            return true;

        string hexOnly = new(value.Where(Uri.IsHexDigit).ToArray());
        return hexOnly.Length > 0 && hexOnly.Length % 2 == 0;
    }

    private static bool TryApplyCatalogIndex(
        string rawValue,
        IReadOnlyList<ShellcodeEncodingItem>? catalog,
        Action<int> assign,
        out string? error)
    {
        error = null;
        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 0)
        {
            error = $"invalid value '{rawValue}'";
            return false;
        }

        if (!ValidateCatalogIndex(index, catalog))
        {
            error = $"invalid value '{rawValue}'";
            return false;
        }

        assign(index);
        return true;
    }

    private static bool ValidateCatalogIndex(int index, IReadOnlyList<ShellcodeEncodingItem>? catalog)
    {
        if (index < 0)
            return false;
        if (catalog is null)
            return true;
        return index == 0 || catalog.Any(item => item.Index == index);
    }

    private static bool TryApplyTriState(string rawValue, Action<EncodeTriState> assign, out string? error)
    {
        error = null;
        if (!TryParseTriState(rawValue, out var parsed))
        {
            error = $"invalid value '{rawValue}'";
            return false;
        }

        assign(parsed);
        return true;
    }

    private static bool TryParseBoolean(string rawValue, out bool value)
    {
        switch (rawValue.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "on":
                value = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "off":
                value = false;
                return true;
            default:
                value = default;
                return false;
        }
    }

    private static bool TryParseTriState(string rawValue, out EncodeTriState value)
    {
        string normalized = rawValue.Trim().ToLowerInvariant();
        if (normalized == "auto")
        {
            value = EncodeTriState.Auto;
            return true;
        }

        if (TryParseBoolean(normalized, out var boolean))
        {
            value = boolean ? EncodeTriState.Enabled : EncodeTriState.Disabled;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryParsePositiveInt(string rawValue, out int value)
    {
        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private static bool TryParsePositiveLong(string rawValue, out long value)
    {
        return long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private static bool TryParseSnippetOverrides(
        EncodeSessionContext context,
        string rawValue,
        IReadOnlyDictionary<string, List<string>>? existing,
        out Dictionary<string, List<string>> parsed,
        out string? error)
    {
        parsed = existing is null
            ? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            : existing.ToDictionary(entry => entry.Key, entry => entry.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        error = null;

        foreach (var entry in rawValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('=', 2);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                error = "expected section=id[,id...] pairs";
                return false;
            }

            string sectionKey = parts[0].Trim();
            if (!TryResolveSnippetSection(context, sectionKey, out var section, out var sectionError))
            {
                error = sectionError;
                return false;
            }

            var ids = parts[1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ids.Count == 0)
            {
                error = $"section '{sectionKey}' is missing snippet IDs";
                return false;
            }

            if (!section!.AllowMultiple && ids.Count != 1)
            {
                error = $"section '{sectionKey}' accepts only one snippet ID";
                return false;
            }

            foreach (var id in ids)
            {
                if (!section.TryGetItem(id, out _))
                {
                    error = $"section '{sectionKey}' does not contain snippet '{id}'";
                    return false;
                }
            }

            if (section.AllowMultiple && parsed.TryGetValue(section.Template, out var existingIds))
            {
                existingIds.AddRange(ids.Where(id => !existingIds.Contains(id, StringComparer.OrdinalIgnoreCase)));
            }
            else
            {
                parsed[section.Template] = ids;
            }
        }

        return true;
    }

    private static bool TryParseTextOverrides(
        EncodeSessionContext context,
        string rawValue,
        IReadOnlyDictionary<string, string>? existing,
        out Dictionary<string, string> parsed,
        out string? error)
    {
        parsed = existing is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        error = null;

        foreach (var entry in rawValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('=', 2);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
            {
                error = "expected input=value pairs";
                return false;
            }

            string key = parts[0].Trim();
            if (!context.KnownTextInputs.Contains(key))
            {
                error = $"unknown text input '{key}'";
                return false;
            }

            parsed[key] = parts[1];
        }

        return true;
    }

    private static bool TryResolveSnippetSection(
        EncodeSessionContext context,
        string key,
        out CodeSnippetSection? section,
        out string? error)
    {
        section = null;
        error = null;

        if (string.IsNullOrWhiteSpace(key))
        {
            error = "section name is required";
            return false;
        }

        var sections = context.SnippetService.GetAllSections();
        section = sections.FirstOrDefault(candidate =>
            string.Equals(candidate.Template, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Header, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Display, key, StringComparison.OrdinalIgnoreCase));
        if (section is not null)
            return true;

        string normalized = SnippetKeyNormalizer.Normalize(key);
        var matches = sections
            .Where(candidate =>
                SnippetKeyNormalizer.Normalize(candidate.Template) == normalized ||
                SnippetKeyNormalizer.Normalize(candidate.Header) == normalized ||
                SnippetKeyNormalizer.Normalize(candidate.Display) == normalized)
            .ToArray();

        if (matches.Length == 1)
        {
            section = matches[0];
            return true;
        }

        if (matches.Length > 1)
        {
            error = $"snippet section '{key}' is ambiguous. Matches: {string.Join(", ", matches.Select(match => match.Template))}";
            return false;
        }

        error = $"unknown snippet section '{key}'";
        return false;
    }

    private static void ResetEncodeOption(EncodeSessionState state, string optionName)
    {
        switch (optionName)
        {
            case "PAYLOAD_SOURCE":
                state.PayloadSource = null;
                break;
            case "TEMPLATE":
                state.Template = "minimal";
                break;
            case "ENCODER":
                state.Encoder = 0;
                break;
            case "ENVELOPE":
                state.Envelope = 0;
                break;
            case "SHIKATA_GA_NAI":
                state.ShikataGaNai = false;
                break;
            case "SHIKATA_ENCODE_COUNT":
                state.ShikataEncodeCount = 1;
                break;
            case "SHIKATA_MAX_BYTES":
                state.ShikataMaxBytes = 50;
                break;
            case "SHIKATA_PLACEMENT":
                state.ShikataPlacement = "pre";
                break;
            case "SNIPPETS":
                foreach (var key in state.Snippets.Keys.ToArray())
                {
                    if (!string.Equals(key, "shellcodeexecution", StringComparison.OrdinalIgnoreCase))
                        state.Snippets.Remove(key);
                }
                state.SnippetOrder.Clear();
                break;
            case "SHELLCODE_EXECUTION":
                state.Snippets.Remove("shellcodeexecution");
                break;
            case "TEXT_INPUTS":
                state.TextInputs = new(StringComparer.OrdinalIgnoreCase);
                break;
            case "CLONE_FROM":
                state.CloneFrom = null;
                break;
            case "CLONE_RESOURCES":
                state.CloneResources = EncodeTriState.Auto;
                break;
            case "CLONE_ICON":
                state.CloneIcon = EncodeTriState.Auto;
                break;
            case "CLONE_METADATA":
                state.CloneMetadata = EncodeTriState.Auto;
                break;
            case "PAD_NOPS":
                state.PadNops = null;
                break;
            case "VERBOSE":
                state.Verbose = false;
                break;
            case "JSON":
                state.Json = false;
                break;
        }
    }

    private static string GetCurrentOptionValue(EncodeSessionState state, EncodeOptionSpec spec)
    {
        return spec.Name switch
        {
            "PAYLOAD_SOURCE" => state.PayloadSource is null
                ? "(not set)"
                : $"{state.PayloadSource.Kind.ToString().ToLowerInvariant()}:{state.PayloadSource.Value}",
            "TEMPLATE" => state.Template,
            "SHELLCODE_EXECUTION" => state.Snippets.TryGetValue("shellcodeexecution", out var execIds) && execIds.Count > 0
                ? NormalizePathSegment(execIds[0])
                : "(default)",
            "ENCODER" => state.Encoder.ToString(CultureInfo.InvariantCulture),
            "ENVELOPE" => state.Envelope.ToString(CultureInfo.InvariantCulture),
            "SHIKATA_GA_NAI" => state.ShikataGaNai ? "true" : "false",
            "SHIKATA_ENCODE_COUNT" => state.ShikataEncodeCount.ToString(CultureInfo.InvariantCulture),
            "SHIKATA_MAX_BYTES" => state.ShikataMaxBytes.ToString(CultureInfo.InvariantCulture),
            "SHIKATA_PLACEMENT" => state.ShikataPlacement,
            "SNIPPETS" => state.SnippetOrder.Count == 0
                ? "(not set)"
                : string.Join("; ", state.SnippetOrder.Select(o => $"{o.SectionKey}={o.ItemId}")),
            "TEXT_INPUTS" => state.TextInputs.Count == 0
                ? "(not set)"
                : string.Join("; ", state.TextInputs.Select(entry => $"{entry.Key}={entry.Value}")),
            "CLONE_FROM" => string.IsNullOrWhiteSpace(state.CloneFrom) ? "(not set)" : state.CloneFrom,
            "CLONE_RESOURCES" => GetTriStateDisplay(state.CloneResources),
            "CLONE_ICON" => GetTriStateDisplay(state.CloneIcon),
            "CLONE_METADATA" => state.CloneMetadata == EncodeTriState.Enabled ? "true" : "false",
            "PAD_NOPS" => state.PadNops?.ToString(CultureInfo.InvariantCulture) ?? "(not set)",
            "VERBOSE" => state.Verbose ? "true" : "false",
            "JSON" => state.Json ? "true" : "false",
            "COMPILATION_BACKEND" => state.CompilationBackend,
            "LLVM_PASSES" => state.LlvmPasses.Count == 0 ? "(none)" : string.Join(", ", state.LlvmPasses),
            _ => "(not set)",
        };
    }

    private static string GetTriStateDisplay(EncodeTriState value)
        => value switch
        {
            EncodeTriState.Enabled => "true",
            EncodeTriState.Disabled => "false",
            _ => "auto",
        };

    private static string GetExpectedValueText(EncodeSessionContext context, EncodeOptionSpec spec)
    {
        return spec.Name switch
        {
            "TEMPLATE" => string.Join(", ", context.SnippetService.GetTemplates().Select(template => template.Id)),
            "ENCODER" => context.EncodingCatalog is null
                ? "0 or a non-negative encoder index"
                : string.Join(", ", new[] { "0" }.Concat(context.EncodingCatalog.Encoders.Select(item => item.Index.ToString(CultureInfo.InvariantCulture)))),
            "ENVELOPE" => context.EncodingCatalog is null
                ? "0 or a non-negative envelope index"
                : string.Join(", ", new[] { "0" }.Concat(context.EncodingCatalog.Envelopes.Select(item => item.Index.ToString(CultureInfo.InvariantCulture)))),
            _ => spec.Expected,
        };
    }

    private static string GetDefaultHelpValue(EncodeOptionSpec spec)
        => string.IsNullOrWhiteSpace(spec.DefaultValue) ? "(not set)" : spec.DefaultValue;

    private static IEnumerable<string> GetEncodeSessionCompletionMatches(string[] argTokens, int currentArgIndex, string currentPrefix)
    {
        if (currentArgIndex == 0)
            return FilterCompletionMatches(EncodeSessionCommands, currentPrefix);

        string verb = argTokens[0].ToLowerInvariant();
        return verb switch
        {
            "show" => GetSessionShowCompletionMatches(argTokens, currentArgIndex, currentPrefix),
            "set" when currentArgIndex == 1 => FilterCompletionMatches(EncodeOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "unset" when currentArgIndex == 1 => FilterCompletionMatches(EncodeOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "get" when currentArgIndex == 1 => FilterCompletionMatches(EncodeOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "help" when currentArgIndex == 1 => FilterCompletionMatches(EncodeOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "set" when currentArgIndex == 2 && TryResolveEncodeOption(argTokens[1], out var spec, out _) => GetEncodeSessionValueCandidates(spec!),
            _ => Array.Empty<string>(),
        };
    }

    private static IEnumerable<string> GetEncodeSessionValueCandidates(EncodeOptionSpec spec)
    {
        return spec.Name switch
        {
            "PAYLOAD_SOURCE" => ["file:", "hex:", "url:"],
            "TEMPLATE" => Array.Empty<string>(),
            "SHELLCODE_EXECUTION" => Array.Empty<string>(),
            "ENCODER" => ["0"],
            "ENVELOPE" => ["0"],
            "SHIKATA_GA_NAI" or "VERBOSE" or "JSON" => ["true", "false"],
            "SHIKATA_PLACEMENT" => ["pre", "post"],
            "CLONE_RESOURCES" or "CLONE_ICON" or "CLONE_METADATA" => ["auto", "true", "false"],
            _ => Array.Empty<string>(),
        };
    }

    private static void RenderEncodeOptionsTable(EncodeSessionContext context, EncodeSessionState state)
    {
        var rows = new List<SessionOptionRow>();
        int rowId = 0;

        void AddRow(string optionName, string? displayName = null)
        {
            var spec = EncodeOptionSpecsByName[optionName];
            rows.Add(new SessionOptionRow(
                rowId++,
                displayName ?? spec.Name,
                GetCurrentOptionValue(state, spec),
                spec.Required,
                spec.Description,
                GetEncodeOptionPossibleValues(optionName)));
        }

        void AddSubRow(string optionName)
        {
            var spec = EncodeOptionSpecsByName[optionName];
            rows.Add(new SessionOptionRow(
                rowId++,
                "  " + spec.Name,
                GetCurrentOptionValue(state, spec),
                spec.Required,
                spec.Description,
                GetEncodeOptionPossibleValues(optionName)));
        }

        // 1. PAYLOAD SOURCE
        AddRow("PAYLOAD_SOURCE");

        // 2. TEMPLATE
        AddRow("TEMPLATE");

        // 3. SHELLCODE_EXECUTION
        AddRow("SHELLCODE_EXECUTION");

        // 4. ENCODER
        AddRow("ENCODER");

        // 5. ENVELOPE
        AddRow("ENVELOPE");

        // 6. SHIKATA_GA_NAI + sub-options when enabled
        AddRow("SHIKATA_GA_NAI");
        if (state.ShikataGaNai)
        {
            AddSubRow("SHIKATA_ENCODE_COUNT");
            AddSubRow("SHIKATA_MAX_BYTES");
            AddSubRow("SHIKATA_PLACEMENT");
        }

        // 7. SNIPPETS + snippet sub-rows (filtered to non-execution snippets via SnippetOrder)
        {
            var snippetsSpec = EncodeOptionSpecsByName["SNIPPETS"];
            bool hasSnippets = state.SnippetOrder.Count > 0;
            string snippetsDisplay = hasSnippets ? ">>>" : "(not set)";
            rows.Add(new SessionOptionRow(
                rowId++,
                "SNIPPETS",
                snippetsDisplay,
                snippetsSpec.Required,
                snippetsSpec.Description,
                "add snippet <name>"));

            for (int sessionId = 0; sessionId < state.SnippetOrder.Count; sessionId++)
            {
                var (sectionKey, itemId) = state.SnippetOrder[sessionId];
                if (!TryResolveSnippetSection(context, sectionKey, out var section, out _))
                    continue;
                if (!section!.TryGetItem(itemId, out var item))
                    continue;

                // Snippet name sub-row: [sessionId] normalized-id as name, human Display as description
                rows.Add(new SessionOptionRow(
                    0,
                    $"  \u2514\u2500 [{sessionId}] {NormalizePathSegment(item!.Id)}",
                    "added",
                    false,
                    item.Display,
                    $"remove {sessionId}",
                    IsSubRow: true));

                // Snippet input rows
                foreach (var input in item.Inputs)
                {
                    var scopedKey = CompilerService.BuildScopedInputKey(section.Template, item.Id, input.Id);
                    string inputValue = state.TextInputs.TryGetValue(scopedKey, out var tv)
                        ? tv
                        : (string.IsNullOrWhiteSpace(input.DefaultValue) ? "(not set)" : input.DefaultValue);
                    string reqLabel = input.Required ? "Yes (Snippet)" : "No";
                    string reqColor = input.Required ? UiColors.Warning : UiColors.Muted;

                    rows.Add(new SessionOptionRow(
                        rowId++,
                        "    " + (string.IsNullOrWhiteSpace(input.Label) ? input.Id : input.Label),
                        inputValue,
                        input.Required,
                        $"set {scopedKey} <value>",
                        "text value",
                        reqLabel,
                        reqColor));
                }
            }
        }

        // 8. CLONE_METADATA + sub-options when enabled
        AddRow("CLONE_METADATA");
        if (state.CloneMetadata == EncodeTriState.Enabled)
        {
            AddSubRow("CLONE_FROM");
            AddSubRow("CLONE_RESOURCES");
            AddSubRow("CLONE_ICON");
        }

        // 9. VERBOSE
        AddRow("VERBOSE");

        RenderSessionOptionCatalog(
            "Encode options",
            rows,
            "Commands: show options | set <OPTION> [VALUE] | add snippet <path|id> | remove <snippet_id|name> | cat template [name] | cat snippet <session_id> | show <catalog> | run | exit");
    }

    private static string GetEncodeOptionPossibleValues(string optionName) => optionName switch
    {
        "PAYLOAD_SOURCE" => "file: | hex: | url:",
        "TEMPLATE" => "show templates",
        "SHELLCODE_EXECUTION" => "show execution",
        "ENCODER" => "show encoders",
        "ENVELOPE" => "show envelopes",
        "SHIKATA_GA_NAI" => "boolean",
        "SHIKATA_ENCODE_COUNT" => "positive int",
        "SHIKATA_MAX_BYTES" => "positive int",
        "SHIKATA_PLACEMENT" => "pre | post",
        "SNIPPETS" => "add snippet <path|id>",
        "CLONE_METADATA" => "boolean",
        "CLONE_FROM" => "path to .exe",
        "CLONE_RESOURCES" => "auto | true | false",
        "CLONE_ICON" => "auto | true | false",
        "VERBOSE" => "boolean",
        "JSON" => "boolean",
        _ => "",
    };

    // ── Snippet add/resolve helpers ──────────────────────────────────────────

    private static async Task HandleEncodeAddAsync(EncodeSessionContext context, EncodeSessionState state, string[] tokens)
    {
        if (tokens.Length < 2 || !string.Equals(tokens[1], "snippet", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"[{UiColors.Error}]Usage:[/] [{UiColors.Accent}]add snippet[/] [{UiColors.Muted}]{Markup.Escape("[module/<category>/<id> | <global_id>]")}[/]");
            AnsiConsole.MarkupLine($"[{UiColors.Muted}]Example: add snippet module/guardrails/domain_membership[/]");
            AnsiConsole.MarkupLine($"[{UiColors.Muted}]Example: add snippet 12[/]");
            AnsiConsole.MarkupLine($"[{UiColors.Muted}]Example: add snippet   (interactive picker)[/]");
            return;
        }

        string? searchArg = tokens.Length >= 3 ? string.Join(' ', tokens.Skip(2)).Trim() : null;
        await HandleEncodeSnippetAsync(context, state, string.IsNullOrWhiteSpace(searchArg) ? null : searchArg);
    }

    private static async Task HandleEncodeSnippetAsync(EncodeSessionContext context, EncodeSessionState state, string? searchArg)
    {
        // No argument → interactive picker
        if (string.IsNullOrWhiteSpace(searchArg))
        {
            await HandleEncodeSnippetInteractivePicker(context, state);
            return;
        }

        // Numeric global ID
        if (int.TryParse(searchArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int globalId))
        {
            var index = BuildGlobalSnippetIndex(context.SnippetService);
            var entry = index.FirstOrDefault(x => x.GlobalId == globalId);
            if (entry == default)
            {
                WriteStatus(StatusPrefix.Failure, $"No snippet with global ID {globalId}. Use 'show modules' to see the full list with IDs.");
                return;
            }
            await ApplySnippetToStateAsync(context, state, entry.Section, entry.Item);
            return;
        }

        // Module path: module/{cat}/{id} or {cat}/{id}
        if (searchArg.Contains('/'))
        {
            if (TryResolveSnippetByModulePath(context.SnippetService, searchArg, out var pathSection, out var pathItem))
            {
                await ApplySnippetToStateAsync(context, state, pathSection!, pathItem!);
                return;
            }
            WriteStatus(StatusPrefix.Failure, $"No module found at path '{searchArg}'.");
            WriteStatus(StatusPrefix.Info, "Use 'show modules' to see all paths, or type 'add snippet' for interactive picker.");
            return;
        }

        // Text search (fallback)
        var allSections = context.SnippetService.GetAllSections()
            .Where(s => !IsSyntheticSnippetSection(s))
            .ToList();
        var matches = new List<(CodeSnippetSection Section, CodeSnippetItem Item)>();
        foreach (var section in allSections)
        {
            if (section.TryGetItem(searchArg, out var byId))
            {
                matches.Add((section, byId!));
                continue;
            }
            var byDisplay = section.Items.FirstOrDefault(i =>
                string.Equals(i.Display, searchArg, StringComparison.OrdinalIgnoreCase));
            if (byDisplay is not null)
                matches.Add((section, byDisplay));
        }
        if (matches.Count == 0)
        {
            foreach (var section in allSections)
                foreach (var item in section.Items)
                    if (item.Display.Contains(searchArg, StringComparison.OrdinalIgnoreCase) ||
                        item.Id.Contains(searchArg, StringComparison.OrdinalIgnoreCase))
                        matches.Add((section, item));
        }

        if (matches.Count == 0)
        {
            WriteStatus(StatusPrefix.Failure, $"No snippet matching '{searchArg}'.");
            WriteStatus(StatusPrefix.Info, "Use 'show modules' or 'add snippet' (interactive) to find available snippets.");
            return;
        }

        if (matches.Count > 1)
        {
            WriteStatus(StatusPrefix.Warning, $"Ambiguous match for '{searchArg}'. Did you mean:");
            foreach (var (sec, itm) in matches)
                AnsiConsole.MarkupLine($"  [{UiColors.Value}]{Markup.Escape(BuildModulePath(sec, itm))}[/]  [{UiColors.Muted}]{Markup.Escape(itm.Display)}[/]");
            WriteStatus(StatusPrefix.Info, "Use the full path (e.g. module/guardrails/domain_membership) or global ID.");
            return;
        }

        await ApplySnippetToStateAsync(context, state, matches[0].Section, matches[0].Item);
    }

    private static async Task HandleEncodeSnippetInteractivePicker(EncodeSessionContext context, EncodeSessionState state)
    {
        var allSections = context.SnippetService.GetAllSections()
            .Where(s => !IsSyntheticSnippetSection(s))
            .ToList();
        if (allSections.Count == 0)
        {
            WriteStatus(StatusPrefix.Failure, "No snippet modules available.");
            return;
        }

        // Build category list (human-readable)
        var categoryGroups = allSections
            .GroupBy(s => GetSectionCategory(s), StringComparer.OrdinalIgnoreCase)
            .Select(g => new { RawKey = g.Key, Label = SnippetCategoryToHuman(g.Key), Sections = g.ToList() })
            .OrderBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AnsiConsole.WriteLine();
        string chosenLabel = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[{UiColors.Header}]Select a module category:[/]")
                .PageSize(14)
                .HighlightStyle(new Style(UiColors.ParseHex(UiColors.Accent)))
                .AddChoices(categoryGroups.Select(g => g.Label)));

        var group = categoryGroups.First(g => g.Label == chosenLabel);

        foreach (var section in group.Sections)
        {
            var rawItems = section.Items.Where(i => !IsSyntheticModuleItem(i)).ToList();
            if (rawItems.Count == 0)
                continue;

            // Markup-escape keys so Spectre.Console won't choke on display strings with special chars.
            // The escaped keys are what the prompt returns — we use the map to resolve back to the item.
            var displayMap = new Dictionary<string, CodeSnippetItem>(StringComparer.Ordinal);
            foreach (var it in rawItems)
                displayMap.TryAdd(Markup.Escape(it.Display), it);

            AnsiConsole.WriteLine();

            if (section.AllowMultiple)
            {
                var selected = AnsiConsole.Prompt(
                    new MultiSelectionPrompt<string>()
                        .Title($"[{UiColors.Header}]Select snippets[/] [{UiColors.Muted}]({Markup.Escape(section.Header)}) — space to toggle, enter to confirm:[/]")
                        .PageSize(16)
                        .NotRequired()
                        .InstructionsText($"[{UiColors.Muted}](Press [blue]<space>[/] to toggle, [green]<enter>[/] to confirm)[/]")
                        .AddChoices(displayMap.Keys));

                if (selected.Count == 0)
                {
                    WriteStatus(StatusPrefix.Info, "No snippets selected.");
                    continue;
                }

                foreach (var display in selected)
                {
                    if (!displayMap.TryGetValue(display, out var item))
                        continue;
                    await ApplySnippetToStateAsync(context, state, section, item);
                }
            }
            else
            {
                var singleChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"[{UiColors.Header}]Select a snippet[/] [{UiColors.Muted}]({Markup.Escape(section.Header)}):[/]")
                        .PageSize(16)
                        .HighlightStyle(new Style(UiColors.ParseHex(UiColors.Accent)))
                        .AddChoices(displayMap.Keys));

                if (!displayMap.TryGetValue(singleChoice, out var item))
                {
                    WriteStatus(StatusPrefix.Failure, "Could not resolve selected snippet. Try 'add snippet <name>' directly.");
                    continue;
                }
                await ApplySnippetToStateAsync(context, state, section, item);
            }
        }
    }

    private static async Task HandleEncodePayloadSourceInteractivePicker(EncodeSessionContext context, EncodeSessionState state)
    {
        AnsiConsole.WriteLine();

        const string optFile = "file:  — local .bin file";
        const string optHex  = "hex:   — inline raw hex bytes";
        const string optUrl  = "url:   — fetch from URL at runtime";

        string sourceType = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[{UiColors.Header}]Select PAYLOAD_SOURCE type:[/]")
                .HighlightStyle(new Style(UiColors.ParseHex(UiColors.Accent)))
                .AddChoices(optFile, optHex, optUrl));

        string prefix = sourceType.StartsWith("file") ? "file:" :
                        sourceType.StartsWith("hex")  ? "hex:"  : "url:";

        string prompt = prefix switch
        {
            "file:" => $"[{UiColors.Accent}]Path to .bin file (e.g. C:\\payloads\\shell.bin):[/]",
            "hex:"  => $"[{UiColors.Accent}]Hex bytes (e.g. fc4883e4f0...):[/]",
            _       => $"[{UiColors.Accent}]URL (e.g. https://example.com/shell.bin):[/]",
        };

        string rawValue = AnsiConsole.Ask<string>(prompt);
        string full = $"{prefix}{rawValue.Trim()}";

        if (!TryApplyPayloadSource(context, state, full, allowReplacingPayloadSource: true, out var err))
        {
            WriteStatus(StatusPrefix.Failure, err ?? "Invalid payload source value.");
            return;
        }

        WriteStatus(StatusPrefix.Success, $"PAYLOAD_SOURCE => {full}");
        RenderEncodeOptionsTable(context, state);
        await Task.CompletedTask;
    }

    private static async Task HandleEncodeTemplatePicker(EncodeSessionContext context, EncodeSessionState state)
    {
        var templates = context.SnippetService.GetTemplates()
            .OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (templates.Count == 0)
        {
            WriteStatus(StatusPrefix.Failure, "No templates available in the active playbook.");
            return;
        }

        await RunShowAsync(["templates"]);
        AnsiConsole.WriteLine();

        string rawChoice = AnsiConsole.Ask<string>($"[{UiColors.Accent}]Choose TEMPLATE (ID or name):[/]");

        // Try numeric row index first
        if (int.TryParse(rawChoice, NumberStyles.Integer, CultureInfo.InvariantCulture, out int chosenIdx))
        {
            if (chosenIdx >= 0 && chosenIdx < templates.Count)
            {
                state.Template = templates[chosenIdx].Id;
                WriteStatus(StatusPrefix.Success, $"TEMPLATE => {state.Template}");
                RenderEncodeOptionsTable(context, state);
                return;
            }
            WriteStatus(StatusPrefix.Failure, $"TEMPLATE: ID {chosenIdx} not found in catalog.");
            return;
        }

        // Try exact name match
        var exactMatch = templates.FirstOrDefault(t => string.Equals(t.Id, rawChoice, StringComparison.OrdinalIgnoreCase));
        if (exactMatch is not null)
        {
            state.Template = exactMatch.Id;
            WriteStatus(StatusPrefix.Success, $"TEMPLATE => {state.Template}");
            RenderEncodeOptionsTable(context, state);
            return;
        }

        // Try partial name match
        var partialMatches = templates
            .Where(t => t.Id.Contains(rawChoice, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (partialMatches.Length == 1)
        {
            state.Template = partialMatches[0].Id;
            WriteStatus(StatusPrefix.Success, $"TEMPLATE => {state.Template}");
            RenderEncodeOptionsTable(context, state);
            return;
        }
        if (partialMatches.Length > 1)
        {
            WriteStatus(StatusPrefix.Failure, $"Ambiguous choice '{rawChoice}'. Matches: {string.Join(", ", partialMatches.Select(t => t.Id))}");
            return;
        }

        WriteStatus(StatusPrefix.Failure, $"No template found matching '{rawChoice}'.");
        await Task.CompletedTask;
    }

    private static async Task HandleEncodeShellcodeExecutionAsync(EncodeSessionContext context, EncodeSessionState state, string? searchArg)
    {
        var section = context.SnippetService.GetAllSections()
            .FirstOrDefault(s => string.Equals(s.Template, "shellcodeexecution", StringComparison.OrdinalIgnoreCase));

        if (section is null)
        {
            WriteStatus(StatusPrefix.Failure, "Shellcode execution section not found in catalog.");
            return;
        }

        var items = section.Items.Where(i => !IsSyntheticModuleItem(i))
            .OrderBy(i => i.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(searchArg))
        {
            // Interactive: show execution table then prompt — same pattern as ENCODER
            await RunShowAsync(["execution"]);
            AnsiConsole.WriteLine();

            string rawChoice = AnsiConsole.Ask<string>($"[{UiColors.Accent}]Choose SHELLCODE_EXECUTION (ID or name, default to reset):[/]");

            // Try numeric index first
            if (int.TryParse(rawChoice, NumberStyles.Integer, CultureInfo.InvariantCulture, out int chosenIdx))
            {
                if (chosenIdx >= 0 && chosenIdx < items.Count)
                {
                    var chosen = items[chosenIdx];
                    state.Snippets["shellcodeexecution"] = [chosen.Id];
                    WriteStatus(StatusPrefix.Success, $"SHELLCODE_EXECUTION => {NormalizePathSegment(chosen.Id)}");
                    RenderEncodeOptionsTable(context, state);
                    return;
                }
                WriteStatus(StatusPrefix.Failure, $"SHELLCODE_EXECUTION: ID {chosenIdx} not found in catalog.");
                return;
            }

            // Try exact match (by display name or normalized ID)
            var exactMatch = items.FirstOrDefault(i => string.Equals(i.Display, rawChoice, StringComparison.OrdinalIgnoreCase))
                ?? items.FirstOrDefault(i => string.Equals(i.Id, rawChoice, StringComparison.OrdinalIgnoreCase))
                ?? items.FirstOrDefault(i => string.Equals(NormalizePathSegment(i.Id), NormalizePathSegment(rawChoice), StringComparison.OrdinalIgnoreCase));
            if (exactMatch is not null)
            {
                state.Snippets["shellcodeexecution"] = [exactMatch.Id];
                WriteStatus(StatusPrefix.Success, $"SHELLCODE_EXECUTION => {NormalizePathSegment(exactMatch.Id)}");
                RenderEncodeOptionsTable(context, state);
                return;
            }

            // Try partial match
            var partialMatches = items
                .Where(i => i.Display.Contains(rawChoice, StringComparison.OrdinalIgnoreCase)
                         || i.Id.Contains(rawChoice, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (partialMatches.Length == 1)
            {
                state.Snippets["shellcodeexecution"] = [partialMatches[0].Id];
                WriteStatus(StatusPrefix.Success, $"SHELLCODE_EXECUTION => {NormalizePathSegment(partialMatches[0].Id)}");
                RenderEncodeOptionsTable(context, state);
                return;
            }
            if (partialMatches.Length > 1)
            {
                WriteStatus(StatusPrefix.Failure, $"Ambiguous choice '{rawChoice}'. Matches: {string.Join(", ", partialMatches.Select(i => i.Display))}");
                return;
            }

            WriteStatus(StatusPrefix.Failure, $"No execution technique found matching '{rawChoice}'.");
            return;
        }

        // Unset / restore default
        if (string.Equals(searchArg, "default", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(searchArg, "none", StringComparison.OrdinalIgnoreCase))
        {
            state.Snippets.Remove("shellcodeexecution");
            WriteStatus(StatusPrefix.Success, "SHELLCODE_EXECUTION => (default)");
            RenderEncodeOptionsTable(context, state);
            return;
        }

        // Numeric index into ordered items list
        if (int.TryParse(searchArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx)
            && idx >= 0 && idx < items.Count)
        {
            var chosen = items[idx];
            state.Snippets["shellcodeexecution"] = [chosen.Id];
            WriteStatus(StatusPrefix.Success, $"SHELLCODE_EXECUTION => {NormalizePathSegment(chosen.Id)}");
            RenderEncodeOptionsTable(context, state);
            return;
        }

        // Text match: by ID or by display
        var match = items.FirstOrDefault(i => string.Equals(i.Id, searchArg, StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault(i => string.Equals(NormalizePathSegment(i.Id), NormalizePathSegment(searchArg), StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault(i => string.Equals(i.Display, searchArg, StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault(i => i.Id.Contains(searchArg, StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault(i => i.Display.Contains(searchArg, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            state.Snippets["shellcodeexecution"] = [match.Id];
            WriteStatus(StatusPrefix.Success, $"SHELLCODE_EXECUTION => {NormalizePathSegment(match.Id)}");
            RenderEncodeOptionsTable(context, state);
            return;
        }

        WriteStatus(StatusPrefix.Failure, $"Execution technique '{searchArg}' not found. Use 'show execution' or set SHELLCODE_EXECUTION without a value for interactive selection.");
        await Task.CompletedTask;
    }

    private static async Task HandleEncodeCatAsync(EncodeSessionContext context, EncodeSessionState state, string[] tokens)
    {
        if (tokens.Length < 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: cat template [name|row]  |  cat snippet <session_id>");
            return;
        }

        string subCommand = tokens[1].ToLowerInvariant();

        if (subCommand == "template")
        {
            string templateId;
            if (tokens.Length >= 3)
            {
                string raw = string.Join(' ', tokens.Skip(2)).Trim();
                // Numeric row index into sorted template list
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tIdx))
                {
                    var templates = context.SnippetService.GetTemplates()
                        .OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    templateId = tIdx >= 0 && tIdx < templates.Count ? templates[tIdx].Id : raw;
                }
                else
                {
                    templateId = raw;
                }
            }
            else
            {
                templateId = state.Template;
            }

            if (!context.SnippetService.TryGetTemplate(templateId, out var template))
            {
                WriteStatus(StatusPrefix.Failure, $"Template '{templateId}' not found. Use 'show templates' to see available templates.");
                return;
            }

            PrintSourceCode($"template/{NormalizePathSegment(template!.Id)}", template.Content);
            return;
        }

        if (subCommand == "snippet")
        {
            if (tokens.Length < 3)
            {
                WriteStatus(StatusPrefix.Failure, "Usage: cat snippet <session_id>");
                return;
            }

            if (!int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sessionId)
                || sessionId < 0 || sessionId >= state.SnippetOrder.Count)
            {
                WriteStatus(StatusPrefix.Failure, $"Invalid snippet session ID '{tokens[2]}'. Use 'show options' to see added snippets with their [N] IDs.");
                return;
            }

            var (sectionKey, itemId) = state.SnippetOrder[sessionId];
            if (!TryResolveSnippetSection(context, sectionKey, out var section, out _) ||
                !section!.TryGetItem(itemId, out var item))
            {
                WriteStatus(StatusPrefix.Failure, $"Could not resolve snippet #{sessionId}.");
                return;
            }

            PrintSourceCode($"module/{GetSectionCategory(section!)}/{NormalizePathSegment(item!.Id)}", item.Snippet);
            return;
        }

        WriteStatus(StatusPrefix.Failure, "Usage: cat template [name|row]  |  cat snippet <session_id>");
        await Task.CompletedTask;
    }

    private static void PrintSourceCode(string path, string? code)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{UiColors.Header}]── {Markup.Escape(path)} ──────────────────────────────[/]");
        AnsiConsole.WriteLine();

        if (string.IsNullOrWhiteSpace(code))
        {
            AnsiConsole.MarkupLine($"[{UiColors.Muted}](no source available)[/]");
        }
        else
        {
            foreach (var line in code.Replace("\r\n", "\n").Split('\n'))
                AnsiConsole.MarkupLine($"[{UiColors.Value}]{Markup.Escape(line)}[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{UiColors.Muted}]──────────────────────────────────────────────────────[/]");
    }

    private static async Task ApplySnippetToStateAsync(
        EncodeSessionContext context,
        EncodeSessionState state,
        CodeSnippetSection section,
        CodeSnippetItem item)
    {
        // Warn if section not in current template
        if (context.SnippetService.TryGetTemplate(state.Template, out var tpl))
        {
            bool inTemplate = tpl.Placeholders.Any(p =>
                p.Kind == Models.TemplatePlaceholderKind.Snippet &&
                string.Equals(p.SnippetTemplateKey, section.Template, StringComparison.OrdinalIgnoreCase));
            if (!inTemplate)
                WriteStatus(StatusPrefix.Warning,
                    $"'{section.Header}' is not used by template '{state.Template}'. Snippet may have no effect at build time.");
        }

        string sectionKey = section.Template;
        bool isExecutionSection = string.Equals(sectionKey, "shellcodeexecution", StringComparison.OrdinalIgnoreCase);

        if (section.AllowMultiple)
        {
            if (!state.Snippets.TryGetValue(sectionKey, out var existing))
                state.Snippets[sectionKey] = [item.Id];
            else if (!existing.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
                existing.Add(item.Id);
            else
            {
                WriteStatus(StatusPrefix.Info, $"'{item.Display}' is already added.");
                return;
            }
        }
        else
        {
            if (state.Snippets.TryGetValue(sectionKey, out var prev) && prev.Count > 0)
                WriteStatus(StatusPrefix.Warning, $"Replacing previous snippet in '{section.Header}'.");
            state.Snippets[sectionKey] = [item.Id];
        }

        // Track insertion order for non-execution snippets
        if (!isExecutionSection)
        {
            var entry = (sectionKey, item.Id);
            bool alreadyTracked = state.SnippetOrder.Any(o =>
                string.Equals(o.SectionKey, sectionKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(o.ItemId, item.Id, StringComparison.OrdinalIgnoreCase));
            if (!alreadyTracked)
                state.SnippetOrder.Add(entry);
        }

        WriteStatus(StatusPrefix.Success, $"Added: {item.Display}");

        // Warn about required inputs that have no default
        var requiredInputs = item.Inputs
            .Where(i => i.Required && string.IsNullOrWhiteSpace(i.DefaultValue))
            .ToArray();
        if (requiredInputs.Length > 0)
        {
            AnsiConsole.MarkupLine($"[{UiColors.Warning}]This snippet requires additional inputs:[/]");
            foreach (var input in requiredInputs)
            {
                var scopedKey = CompilerService.BuildScopedInputKey(section.Template, item.Id, input.Id);
                AnsiConsole.MarkupLine(
                    $"  [{UiColors.Warning}]■[/] [{UiColors.Label}]{Markup.Escape(input.Label.Length > 0 ? input.Label : input.Id)}[/]" +
                    $"  [{UiColors.Muted}]set {Markup.Escape(scopedKey)} <value>[/]");
            }
        }

        RenderEncodeOptionsTable(context, state);
        await Task.CompletedTask;
    }

    private static void HandleEncodeRemoveSnippet(EncodeSessionContext context, EncodeSessionState state, string[] tokens)
    {
        if (tokens.Length < 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: remove <snippet name, path, or global ID>");
            return;
        }

        string snippetSearch = string.Join(' ', tokens.Skip(1)).Trim();

        // Try session-local ID first, then global ID
        if (int.TryParse(snippetSearch, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedId))
        {
            // Session-local ID (0-based index into SnippetOrder)
            if (parsedId >= 0 && parsedId < state.SnippetOrder.Count)
            {
                var (sk, iid) = state.SnippetOrder[parsedId];
                if (TryResolveSnippetSection(context, sk, out var orderedSection, out _) &&
                    orderedSection!.TryGetItem(iid, out var orderedItem))
                {
                    if (RemoveSnippetFromState(state, sk, iid, orderedItem!.Display))
                    {
                        RenderEncodeOptionsTable(context, state);
                        return;
                    }
                }
                WriteStatus(StatusPrefix.Failure, $"Failed to remove snippet #{parsedId}.");
                return;
            }

            // Fall back: global module ID
            var index = BuildGlobalSnippetIndex(context.SnippetService);
            var entry = index.FirstOrDefault(x => x.GlobalId == parsedId);
            if (entry != default && RemoveSnippetFromState(state, entry.Section.Template, entry.Item.Id, entry.Item.Display))
            {
                RenderEncodeOptionsTable(context, state);
                return;
            }
            WriteStatus(StatusPrefix.Failure, $"Snippet with ID {parsedId} is not currently added. Use session ID shown in 'show options'.");
            return;
        }

        // Try module path
        if (snippetSearch.Contains('/') &&
            TryResolveSnippetByModulePath(context.SnippetService, snippetSearch, out var pathSection, out var pathItem) &&
            RemoveSnippetFromState(state, pathSection!.Template, pathItem!.Id, pathItem.Display))
        {
            RenderEncodeOptionsTable(context, state);
            return;
        }

        // Text search in added snippets
        foreach (var (sectionKey, itemIds) in state.Snippets.ToArray())
        {
            if (!TryResolveSnippetSection(context, sectionKey, out var section, out _))
                continue;

            string? matchedId = itemIds.FirstOrDefault(id =>
            {
                if (!section!.TryGetItem(id, out var item))
                    return false;
                return string.Equals(item.Id, snippetSearch, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(item.Display, snippetSearch, StringComparison.OrdinalIgnoreCase) ||
                       item.Display.Contains(snippetSearch, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(BuildModulePath(section, item), snippetSearch, StringComparison.OrdinalIgnoreCase);
            });

            if (matchedId is null)
                continue;

            section!.TryGetItem(matchedId, out var found);
            if (RemoveSnippetFromState(state, sectionKey, matchedId, found?.Display ?? matchedId))
            {
                RenderEncodeOptionsTable(context, state);
                return;
            }
        }

        WriteStatus(StatusPrefix.Failure, $"'{snippetSearch}' is not in the current list. Use 'show options' to see added snippets.");
    }

    private static bool RemoveSnippetFromState(EncodeSessionState state, string sectionKey, string itemId, string displayName)
    {
        if (!state.Snippets.TryGetValue(sectionKey, out var itemIds))
            return false;
        if (!itemIds.Remove(itemId))
            return false;
        if (itemIds.Count == 0)
            state.Snippets.Remove(sectionKey);
        state.SnippetOrder.RemoveAll(o =>
            string.Equals(o.SectionKey, sectionKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(o.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
        WriteStatus(StatusPrefix.Success, $"Removed: {displayName}");
        return true;
    }

    // ── Snippet index + path resolution ─────────────────────────────────────

    private sealed record SnippetIndexEntry(int GlobalId, CodeSnippetSection Section, CodeSnippetItem Item, string ModulePath);

    private static IReadOnlyList<SnippetIndexEntry> BuildGlobalSnippetIndex(ICodeSnippetCatalogService snippetService)
    {
        return snippetService.GetAllSections()
            .Where(section => !IsSyntheticSnippetSection(section))
            .SelectMany(section => section.Items
                .Where(item => !IsSyntheticModuleItem(item))
                .Select(item => new { Section = section, Item = item, Path = BuildModulePath(section, item) }))
            .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select((x, idx) => new SnippetIndexEntry(idx, x.Section, x.Item, x.Path))
            .ToList();
    }

    private static string BuildModulePath(CodeSnippetSection section, CodeSnippetItem item)
        => $"module/{GetSectionCategory(section)}/{NormalizePathSegment(item.Id)}";

    private static bool TryResolveSnippetByModulePath(
        ICodeSnippetCatalogService snippetService,
        string path,
        out CodeSnippetSection? section,
        out CodeSnippetItem? item)
    {
        section = null;
        item = null;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        string category, itemId;
        if (parts.Length == 3 && string.Equals(parts[0], "module", StringComparison.OrdinalIgnoreCase))
        {
            category = parts[1];
            itemId   = parts[2];
        }
        else if (parts.Length == 2)
        {
            category = parts[0];
            itemId   = parts[1];
        }
        else
        {
            return false;
        }

        string normCat  = NormalizePathSegment(category);
        string normItem = NormalizePathSegment(itemId);

        var foundSection = snippetService.GetAllSections().FirstOrDefault(s =>
            string.Equals(GetSectionCategory(s), normCat, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizePathSegment(s.Template), normCat, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizePathSegment(s.Header), normCat, StringComparison.OrdinalIgnoreCase));

        if (foundSection is null)
            return false;

        var foundItem = foundSection.Items.FirstOrDefault(i =>
            string.Equals(NormalizePathSegment(i.Id), normItem, StringComparison.OrdinalIgnoreCase));

        if (foundItem is null)
            return false;

        section = foundSection;
        item    = foundItem;
        return true;
    }

    private static string SnippetCategoryToHuman(string normalized)
    {
        // Convert snake_case to "Title Case With Spaces"
        return string.Join(' ', normalized.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    private static int GetInteractiveConsoleWidth()
    {
        try
        {
            if (Console.WindowWidth > 0)
                return Console.WindowWidth;
        }
        catch
        {
        }

        try
        {
            if (Console.BufferWidth > 0)
                return Console.BufferWidth;
        }
        catch
        {
        }

        return 80;
    }

    private static bool CanEnterInteractiveShell()
        => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    private static bool SupportsUnicodeTables()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NO_COLOR")))
            return false;

        try
        {
            return Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage;
        }
        catch
        {
            return false;
        }
    }

    private static int[] AllocateColumnWidths(int totalWidth, int[] minimums, int[] weights)
    {
        var widths = minimums.ToArray();
        int remaining = Math.Max(0, totalWidth - widths.Sum());
        int weightTotal = Math.Max(1, weights.Sum());

        for (int i = 0; i < widths.Length; i++)
        {
            int share = remaining * weights[i] / weightTotal;
            widths[i] += share;
        }

        int assigned = widths.Sum();
        int index = 0;
        while (assigned < totalWidth)
        {
            widths[index % widths.Length]++;
            assigned++;
            index++;
        }

        return widths;
    }

    private static string BuildBorderLine(char left, char join, char right, char horizontal, IReadOnlyList<int> widths)
    {
        var sb = new StringBuilder();
        sb.Append(left);
        for (int i = 0; i < widths.Count; i++)
        {
            sb.Append(new string(horizontal, widths[i]));
            sb.Append(i == widths.Count - 1 ? right : join);
        }
        return sb.ToString();
    }

    private static string BuildRow(char vertical, IReadOnlyList<int> widths, IReadOnlyList<string> values)
    {
        var sb = new StringBuilder();
        sb.Append(vertical);
        for (int i = 0; i < widths.Count; i++)
        {
            string value = i < values.Count ? values[i] : string.Empty;
            sb.Append(value.PadRight(widths[i]));
            sb.Append(vertical);
        }
        return sb.ToString();
    }

    private static List<string> WrapCell(string text, int width)
    {
        return WrapText(text, width)
            .Select(line => line.Length > width ? line[..width] : line)
            .ToList();
    }

    private static List<string> WrapText(string text, int width)
    {
        if (width <= 1)
            return [text];

        var lines = new List<string>();
        foreach (var rawLine in text.Replace("\r", string.Empty).Split('\n'))
        {
            var remaining = rawLine;
            while (remaining.Length > width)
            {
                int split = remaining.LastIndexOf(' ', width);
                if (split <= 0)
                    split = width;

                lines.Add(remaining[..split].TrimEnd());
                remaining = remaining[split..].TrimStart();
            }

            lines.Add(remaining);
        }

        if (lines.Count == 0)
            lines.Add(string.Empty);
        return lines;
    }

    private static string TruncateWithEllipsis(string value, int width, string ellipsis)
    {
        if (value.Length <= width)
            return value;
        if (width <= ellipsis.Length)
            return value[..width];
        return value[..(width - ellipsis.Length)] + ellipsis;
    }

    private static void WriteStatus(StatusPrefix prefix, string message)
    {
        string marker = prefix switch
        {
            StatusPrefix.Success => "[+]",
            StatusPrefix.Failure => "[-]",
            StatusPrefix.Info => "[*]",
            StatusPrefix.Warning => "[!]",
            StatusPrefix.Prompt => "[?]",
            _ => "[?]",
        };

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NO_COLOR")) || Console.IsOutputRedirected)
        {
            Console.WriteLine($"{marker} {message}");
            return;
        }

        string color = prefix switch
        {
            StatusPrefix.Success => "green",
            StatusPrefix.Failure => "red",
            StatusPrefix.Info => "cyan1",
            StatusPrefix.Warning => "yellow",
            StatusPrefix.Prompt => "white bold",
            _ => "white",
        };

        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(marker)}[/] {Markup.Escape(message)}");
    }

    private sealed record EncodeOptionSpec(
        string Name,
        bool Required,
        string? DefaultValue,
        string Description,
        string Details,
        string Expected,
        string Example);

    private sealed class EncodeSessionContext
    {
        public EncodeSessionContext(
            IAppPaths paths,
            ICodeSnippetCatalogService snippetService,
            ShellcodeEncodingCatalog? encodingCatalog,
            string? encodingCatalogWarning,
            ISet<string> knownTextInputs)
        {
            Paths = paths;
            SnippetService = snippetService;
            EncodingCatalog = encodingCatalog;
            EncodingCatalogWarning = encodingCatalogWarning;
            KnownTextInputs = knownTextInputs;
        }

        public IAppPaths Paths { get; }
        public ICodeSnippetCatalogService SnippetService { get; }
        public ShellcodeEncodingCatalog? EncodingCatalog { get; set; }
        public string? EncodingCatalogWarning { get; set; }
        public ISet<string> KnownTextInputs { get; }
    }

    private sealed class EncodeSessionState
    {
        public EncodePayloadSource? PayloadSource { get; set; }
        public string Template { get; set; } = "minimal";
        public int Encoder { get; set; }
        public int Envelope { get; set; }
        public bool ShikataGaNai { get; set; }
        public int ShikataEncodeCount { get; set; } = 1;
        public int ShikataMaxBytes { get; set; } = 50;
        public string ShikataPlacement { get; set; } = "pre";
        public Dictionary<string, List<string>> Snippets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string SectionKey, string ItemId)> SnippetOrder { get; set; } = new();
        public Dictionary<string, string> TextInputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? CloneFrom { get; set; }
        public EncodeTriState CloneResources { get; set; } = EncodeTriState.Auto;
        public EncodeTriState CloneIcon { get; set; } = EncodeTriState.Auto;
        public EncodeTriState CloneMetadata { get; set; } = EncodeTriState.Auto;
        public long? PadNops { get; set; }
        public bool Verbose { get; set; }
        public bool Json { get; set; }
        public string CompilationBackend { get; set; } = "Deterministic";
        public List<string> LlvmPasses { get; set; } = [];

        public EncodeSessionState Clone()
        {
            return new EncodeSessionState
            {
                PayloadSource = PayloadSource is null ? null : new EncodePayloadSource(PayloadSource.Kind, PayloadSource.Value),
                Template = Template,
                Encoder = Encoder,
                Envelope = Envelope,
                ShikataGaNai = ShikataGaNai,
                ShikataEncodeCount = ShikataEncodeCount,
                ShikataMaxBytes = ShikataMaxBytes,
                ShikataPlacement = ShikataPlacement,
                Snippets = Snippets.ToDictionary(entry => entry.Key, entry => entry.Value.ToList(), StringComparer.OrdinalIgnoreCase),
                SnippetOrder = SnippetOrder.ToList(),
                TextInputs = new Dictionary<string, string>(TextInputs, StringComparer.OrdinalIgnoreCase),
                CloneFrom = CloneFrom,
                CloneResources = CloneResources,
                CloneIcon = CloneIcon,
                CloneMetadata = CloneMetadata,
                PadNops = PadNops,
                Verbose = Verbose,
                Json = Json,
                CompilationBackend = CompilationBackend,
                LlvmPasses = LlvmPasses.ToList(),
            };
        }
    }

    private sealed record EncodePayloadSource(EncodePayloadSourceKind Kind, string Value);

    private sealed record EncodeBuildValidation(IReadOnlyList<EncodeOptionSpec> MissingRequired, IReadOnlyList<string> Invalid);

    private sealed record EncodeParseOutcome(
        EncodeSessionState State,
        IReadOnlyList<string> Messages,
        IReadOnlyList<EncodeOptionSpec> MissingRequired,
        string? FatalMessage,
        bool RequiresInteractiveFallback);

    private enum EncodePayloadSourceKind
    {
        File,
        Hex,
        Url,
    }

    private enum EncodeTriState
    {
        Auto,
        Enabled,
        Disabled,
    }

    private enum StatusPrefix
    {
        Success,
        Failure,
        Info,
        Warning,
        Prompt,
    }

    private readonly record struct TableBorderStyle(
        char TopLeft,
        char TopJoin,
        char TopRight,
        char LeftJoin,
        char Cross,
        char RightJoin,
        char BottomLeft,
        char BottomJoin,
        char BottomRight,
        char Horizontal,
        char Vertical,
        string Ellipsis)
    {
        public static TableBorderStyle Unicode => new('╔', '╦', '╗', '╠', '╬', '╣', '╚', '╩', '╝', '═', '║', "…");
        public static TableBorderStyle Ascii => new('+', '+', '+', '+', '+', '+', '+', '+', '+', '-', '|', "...");
    }
}
