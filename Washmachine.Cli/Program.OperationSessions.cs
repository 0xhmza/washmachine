using System.Globalization;
using Spectre.Console;
using Washmachine.Cli.Ui;
using Washmachine.Logging;
using Washmachine.Models;
using Washmachine.Services;

namespace Washmachine.Cli;

public static partial class Program
{
    private const string StripSessionCompletionCommand = "strip-session";
    private const string BackdoorSessionCompletionCommand = "backdoor-session";

    private static readonly SessionOptionSpec[] StripOptionSpecs =
    [
        new("PE_FILE", true, null, "Target PE file to strip.", "Path to the PE file that will be analyzed or carved.", "Existing PE file path", @".\loader.exe"),
        new("MODE", false, "ep", "Extraction strategy.", "Choose entry-point carve, a named section, every executable section, or an explicit raw range.", "ep | section | all-exec | range", "ep"),
        new("SECTION", false, null, "Section name when MODE=section.", "Only used when you want to dump one named PE section.", "Existing section name", ".text"),
        new("RANGE", false, null, "Raw byte range when MODE=range.", "Express the range as start:length. Decimal and hex are both accepted.", "start:length", "0x400:0x200"),
        new("OUTPUT", false, "<input>.bin", "Destination .bin path.", "When unset, strip writes next to the input PE with a .bin extension.", "Writable file path", @".\payload.bin"),
        new("ANALYZE_ONLY", false, "false", "Analyze section layout without extracting.", "When true, strip prints the section view and entry-point context instead of writing a file.", "true | false", "true"),
        new("TRIM_TRAILING_ZEROS", false, "true", "Trim null padding from the extracted result.", "Set false to preserve trailing null bytes in the extracted output.", "true | false", "true"),
    ];

    private static readonly IReadOnlyDictionary<string, SessionOptionSpec> StripOptionSpecsByName =
        StripOptionSpecs.ToDictionary(spec => spec.Name, spec => spec, StringComparer.OrdinalIgnoreCase);

    private static readonly SessionOptionSpec[] BackdoorOptionSpecs =
    [
        new("PE_FILE", true, null, "Target PE file to patch.", "Existing EXE or DLL that will receive the prepared payload.", "Existing PE file path", @".\app.exe"),
        new("SHELLCODE", true, null, "Prepared flat .bin payload to inject.", "Backdoor expects a ready-to-run binary payload, not raw framework modules.", "Existing .bin file path", @".\payload.bin"),
        new("OUTPUT", false, "<input>.backdoored.exe", "Destination path for the patched PE.", "When unset, backdoor writes next to the target PE using a .backdoored suffix.", "Writable file path", @".\patched.exe"),
        new("METHOD", false, "code-cave", "Injection method.", "Choose how the payload is placed into the target PE.", "code-cave | new-section | section-ext | text-pad | tls-callback", "code-cave"),
        new("CARRIER", false, "entry-point", "Payload invocation strategy.", "Current implementation supports entry-point only.", "entry-point", "entry-point"),
        new("ENCRYPTION", false, "none", "Payload transform mode.", "Current implementation supports none only for the backdoor command.", "none", "none"),
        new("SECTION_NAME", false, ".extra", "Section name for section-creating methods.", "Used when the selected method creates or extends section data.", "PE section name", ".extra"),
        new("REMOVE_SIGNATURE", false, "true", "Remove Authenticode signature before patching.", "Set false to keep the existing signature block untouched.", "true | false", "true"),
        new("PATCH_SUBSYSTEM", false, "true", "Patch the output subsystem to GUI when needed.", "Set false to preserve the original subsystem.", "true | false", "true"),
        new("PRESERVE_ENTRY", false, "true", "Resume the original entry point after payload execution.", "Current carrier implementation requires true.", "true | false", "true"),
        new("PATCH_IAT", false, "true", "Patch imports as needed for the carrier.", "Set false to skip IAT patching during the backdoor pass.", "true | false", "true"),
        new("PATCH_EXIT", false, "true", "Patch exit behavior after the payload runs.", "Set false to leave exit handling untouched.", "true | false", "true"),
        new("CAVE_MIN_SIZE", false, "0", "Minimum code cave size when METHOD=code-cave.", "Raise this when you want to reject tiny caves during planning.", "Non-negative integer", "512"),
        new("DRY_RUN", false, "false", "Analyze feasibility without writing output.", "When true, backdoor prints the plan and exits before patching.", "true | false", "true"),
        new("SESSION_LOG", false, "auto", "Per-run session logging override.", "auto respects saved settings, true forces logging on, false forces it off.", "auto | true | false", "auto"),
        new("VERBOSE", false, "false", "Show detailed backdoor logs.", "Enables verbose CLI logging during analysis and patching.", "true | false", "true"),
        new("JSON", false, "false", "Emit machine-friendly JSON instead of the styled report.", "Useful for scripts or automation.", "true | false", "true"),
    ];

    private static readonly IReadOnlyDictionary<string, SessionOptionSpec> BackdoorOptionSpecsByName =
        BackdoorOptionSpecs.ToDictionary(spec => spec.Name, spec => spec, StringComparer.OrdinalIgnoreCase);

    private sealed class StripSessionState
    {
        public string? PeFile { get; set; }
        public string Mode { get; set; } = "ep";
        public string? Section { get; set; }
        public string? Range { get; set; }
        public string? Output { get; set; }
        public bool AnalyzeOnly { get; set; }
        public bool TrimTrailingZeros { get; set; } = true;
    }

    private sealed class BackdoorSessionState
    {
        public string? PeFile { get; set; }
        public string? Shellcode { get; set; }
        public string? Output { get; set; }
        public string Method { get; set; } = "code-cave";
        public string Carrier { get; set; } = "entry-point";
        public string Encryption { get; set; } = "none";
        public string SectionName { get; set; } = ".extra";
        public bool RemoveSignature { get; set; } = true;
        public bool PatchSubsystem { get; set; } = true;
        public bool PreserveEntry { get; set; } = true;
        public bool PatchIat { get; set; } = true;
        public bool PatchExit { get; set; } = true;
        public int CaveMinSize { get; set; }
        public bool DryRun { get; set; }
        public EncodeTriState SessionLog { get; set; } = EncodeTriState.Auto;
        public bool Verbose { get; set; }
        public bool Json { get; set; }
    }

    private sealed record SessionValidationResult(IReadOnlyList<SessionOptionSpec> MissingRequired, IReadOnlyList<string> Invalid);

    private static async Task<int> RunStripInteractiveSessionAsync()
    {
        var state = new StripSessionState();
        RenderStripOptions(state);

        while (true)
        {
            AnsiConsole.WriteLine();
            string? input;
            try
            {
                input = ReadLineWithEditor(
                    $"[{UiColors.Header}]washmachine[/] [{UiColors.Accent}]strip[/] [{UiColors.Accent}]>[/] ",
                    "strip-session",
                    StripSessionCompletionCommand);
            }
            catch (InvalidOperationException)
            {
                break;
            }

            if (input is null)
                break;

            var tokens = TokenizeLine(input.Trim());
            if (tokens.Length == 0)
                continue;

            var command = tokens[0].ToLowerInvariant();
            switch (command)
            {
                case "show" when tokens.Length >= 2 && tokens[1].Equals("options", StringComparison.OrdinalIgnoreCase):
                    RenderStripOptions(state);
                    break;

                case "show":
                    await RunShowAsync(tokens.Skip(1).ToArray());
                    break;

                case "clear" or "cls":
                    try { AnsiConsole.Clear(); } catch { /* non-interactive */ }
                    break;

                case "set":
                    await HandleStripSetAsync(state, tokens);
                    break;

                case "unset":
                    HandleStripUnset(state, tokens);
                    break;

                case "get":
                    HandleStripGet(state, tokens);
                    break;

                case "help":
                    await HandleStripHelpAsync(state, tokens);
                    break;

                case "reset":
                    state = new StripSessionState();
                    WriteStatus(StatusPrefix.Success, "All strip options reset to defaults.");
                    break;

                case "run" or "build":
                {
                    var validation = ValidateStripState(state);
                    if (validation.MissingRequired.Count > 0 || validation.Invalid.Count > 0)
                    {
                        PrintSessionValidation("strip", validation);
                        break;
                    }

                    RenderStripOptions(state);
                    await RunStripAsync(BuildStripArguments(state));
                    break;
                }

                case "exit" or "quit":
                    return 0;

                default:
                    WriteStatus(StatusPrefix.Failure, $"Unknown command: {tokens[0]}. Type 'help' for available commands.");
                    break;
            }
        }

        return 0;
    }

    private static async Task<int> RunBackdoorInteractiveSessionAsync()
    {
        var state = new BackdoorSessionState();
        RenderBackdoorOptions(state);

        while (true)
        {
            AnsiConsole.WriteLine();
            string? input;
            try
            {
                input = ReadLineWithEditor(
                    $"[{UiColors.Header}]washmachine[/] [{UiColors.Accent}]backdoor[/] [{UiColors.Accent}]>[/] ",
                    "backdoor-session",
                    BackdoorSessionCompletionCommand);
            }
            catch (InvalidOperationException)
            {
                break;
            }

            if (input is null)
                break;

            var tokens = TokenizeLine(input.Trim());
            if (tokens.Length == 0)
                continue;

            var command = tokens[0].ToLowerInvariant();
            switch (command)
            {
                case "show" when tokens.Length >= 2 && tokens[1].Equals("options", StringComparison.OrdinalIgnoreCase):
                    RenderBackdoorOptions(state);
                    break;

                case "show":
                    await RunShowAsync(tokens.Skip(1).ToArray());
                    break;

                case "clear" or "cls":
                    try { AnsiConsole.Clear(); } catch { /* non-interactive */ }
                    break;

                case "set":
                    await HandleBackdoorSetAsync(state, tokens);
                    break;

                case "unset":
                    HandleBackdoorUnset(state, tokens);
                    break;

                case "get":
                    HandleBackdoorGet(state, tokens);
                    break;

                case "help":
                    await HandleBackdoorHelpAsync(state, tokens);
                    break;

                case "reset":
                    state = new BackdoorSessionState();
                    WriteStatus(StatusPrefix.Success, "All backdoor options reset to defaults.");
                    break;

                case "run" or "build":
                {
                    var validation = ValidateBackdoorState(state);
                    if (validation.MissingRequired.Count > 0 || validation.Invalid.Count > 0)
                    {
                        PrintSessionValidation("backdoor", validation);
                        break;
                    }

                    RenderBackdoorOptions(state);
                    await RunBackdoorAsync(BuildBackdoorArguments(state));
                    break;
                }

                case "exit" or "quit":
                    return 0;

                default:
                    WriteStatus(StatusPrefix.Failure, $"Unknown command: {tokens[0]}. Type 'help' for available commands.");
                    break;
            }
        }

        return 0;
    }

    private static async Task HandleStripSetAsync(StripSessionState state, string[] tokens)
    {
        if (tokens.Length < 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: set <OPTION> [VALUE].");
            return;
        }

        if (!TryResolveSessionOption(tokens[1], StripOptionSpecs, out var spec, out var error))
        {
            WriteStatus(StatusPrefix.Failure, error!);
            return;
        }

        if (tokens.Length == 2)
        {
            PrintOptionRequiredInfo(spec!);
            await ShowStripOptionValuesAsync(state, spec!);
            return;
        }

        string value = string.Join(' ', tokens.Skip(2));
        if (!TryApplyStripOptionValue(state, spec!, value, out var applyError))
        {
            WriteStatus(StatusPrefix.Failure, $"{spec!.Name}: {applyError}. Expected: {spec.Expected}. Example: {spec.Example}");
            return;
        }

        WriteStatus(StatusPrefix.Success, $"{spec!.Name} => {GetStripOptionValue(state, spec)}");
    }

    private static void HandleStripUnset(StripSessionState state, string[] tokens)
    {
        if (tokens.Length != 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: unset <OPTION>.");
            return;
        }

        if (!TryResolveSessionOption(tokens[1], StripOptionSpecs, out var spec, out var error))
        {
            WriteStatus(StatusPrefix.Failure, error!);
            return;
        }

        ResetStripOption(state, spec!.Name);
        WriteStatus(StatusPrefix.Success, $"{spec!.Name} => {GetStripOptionValue(state, spec)}");
    }

    private static void HandleStripGet(StripSessionState state, string[] tokens)
    {
        if (tokens.Length != 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: get <OPTION>.");
            return;
        }

        if (!TryResolveSessionOption(tokens[1], StripOptionSpecs, out var spec, out var error))
        {
            WriteStatus(StatusPrefix.Failure, error!);
            return;
        }

        WriteStatus(StatusPrefix.Info, $"{spec!.Name} => {GetStripOptionValue(state, spec)}");
    }

    private static async Task HandleStripHelpAsync(StripSessionState state, string[] tokens)
    {
        if (tokens.Length == 1)
        {
            WriteStatus(StatusPrefix.Info, "Commands: show options | show <catalog> | set <OPTION> [VALUE] | unset <OPTION> | get <OPTION> | help [OPTION] | reset | run | exit");
            WriteStatus(StatusPrefix.Info, "Required strip settings are shown in the Required? column of the options view.");
            return;
        }

        if (!TryResolveSessionOption(tokens[1], StripOptionSpecs, out var spec, out var error))
        {
            WriteStatus(StatusPrefix.Failure, error!);
            return;
        }

        WriteStatus(StatusPrefix.Info, $"{spec!.Name}: {spec.Details}");
        WriteStatus(StatusPrefix.Info, $"Expected: {spec.Expected}. Example: {spec.Example}");
        await Task.CompletedTask;
    }

    private static async Task ShowStripOptionValuesAsync(StripSessionState state, SessionOptionSpec spec)
    {
        switch (spec.Name)
        {
            case "MODE":
                RenderSessionChoiceCatalog("Possible values for MODE",
                [
                    new SessionValueChoice("ep", "Extract from the entry point to the end of the containing section."),
                    new SessionValueChoice("section", "Extract one named section."),
                    new SessionValueChoice("all-exec", "Concatenate all executable sections."),
                    new SessionValueChoice("range", "Extract an explicit raw start:length range."),
                ], "Use: set MODE <value>");
                return;

            case "SECTION":
                if (!string.IsNullOrWhiteSpace(state.PeFile) && File.Exists(state.PeFile))
                {
                    var logger = new ConsoleLogger { SuppressOutput = true };
                    var stripper = new PeStripService(logger);
                    var analysis = await stripper.AnalyzeAsync(state.PeFile);
                    var choices = analysis.Sections
                        .Select(section => new SessionValueChoice(
                            section.Name,
                            $"raw 0x{section.RawAddress:X8}, size 0x{section.RawSize:X8}, perms {(section.IsReadable ? "R" : "-")}{(section.IsWritable ? "W" : "-")}{(section.IsExecutable ? "X" : "-")}"))
                        .ToArray();
                    RenderSessionChoiceCatalog("Possible values for SECTION", choices, "Use one of the section names above with: set SECTION <name>");
                    return;
                }

                WriteStatus(StatusPrefix.Info, "Set PE_FILE first to discover section names from the target PE.");
                return;

            case "ANALYZE_ONLY":
                RenderSessionChoiceCatalog("Possible values for ANALYZE_ONLY", BuildBooleanChoices("Show analysis only.", "Write extracted bytes.").ToArray(), "Use: set ANALYZE_ONLY <true|false>");
                return;

            case "TRIM_TRAILING_ZEROS":
                RenderSessionChoiceCatalog("Possible values for TRIM_TRAILING_ZEROS", BuildBooleanChoices("Trim trailing null padding.", "Keep trailing null padding.").ToArray(), "Use: set TRIM_TRAILING_ZEROS <true|false>");
                return;

            default:
                WriteStatus(StatusPrefix.Info, $"{spec.Name} expects: {spec.Expected}");
                WriteStatus(StatusPrefix.Info, $"Example: {spec.Example}");
                return;
        }
    }

    private static bool TryApplyStripOptionValue(StripSessionState state, SessionOptionSpec spec, string rawValue, out string? error)
    {
        error = null;
        switch (spec.Name)
        {
            case "PE_FILE":
                state.PeFile = rawValue;
                return true;

            case "MODE":
            {
                string normalized = rawValue.Trim().ToLowerInvariant();
                if (normalized is "ep" or "entry-point" or "section" or "all-exec" or "range")
                {
                    state.Mode = normalized == "entry-point" ? "ep" : normalized;
                    return true;
                }

                error = $"invalid value '{rawValue}'";
                return false;
            }

            case "SECTION":
                state.Section = rawValue;
                return true;

            case "RANGE":
                if (!TryParseStripRange(rawValue, out _, out _, out error))
                    return false;
                state.Range = rawValue;
                return true;

            case "OUTPUT":
                state.Output = rawValue;
                return true;

            case "ANALYZE_ONLY":
                if (!TryParseBoolean(rawValue, out var analyzeOnly))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }
                state.AnalyzeOnly = analyzeOnly;
                return true;

            case "TRIM_TRAILING_ZEROS":
                if (!TryParseBoolean(rawValue, out var trim))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }
                state.TrimTrailingZeros = trim;
                return true;
        }

        error = $"unsupported option '{spec.Name}'";
        return false;
    }

    private static void ResetStripOption(StripSessionState state, string optionName)
    {
        switch (optionName)
        {
            case "PE_FILE":
                state.PeFile = null;
                break;
            case "MODE":
                state.Mode = "ep";
                break;
            case "SECTION":
                state.Section = null;
                break;
            case "RANGE":
                state.Range = null;
                break;
            case "OUTPUT":
                state.Output = null;
                break;
            case "ANALYZE_ONLY":
                state.AnalyzeOnly = false;
                break;
            case "TRIM_TRAILING_ZEROS":
                state.TrimTrailingZeros = true;
                break;
        }
    }

    private static string GetStripOptionValue(StripSessionState state, SessionOptionSpec spec)
    {
        return spec.Name switch
        {
            "PE_FILE" => string.IsNullOrWhiteSpace(state.PeFile) ? "(not set)" : state.PeFile,
            "MODE" => state.Mode,
            "SECTION" => string.IsNullOrWhiteSpace(state.Section) ? "(not set)" : state.Section,
            "RANGE" => string.IsNullOrWhiteSpace(state.Range) ? "(not set)" : state.Range,
            "OUTPUT" => string.IsNullOrWhiteSpace(state.Output) ? "(auto)" : state.Output,
            "ANALYZE_ONLY" => state.AnalyzeOnly ? "true" : "false",
            "TRIM_TRAILING_ZEROS" => state.TrimTrailingZeros ? "true" : "false",
            _ => "(not set)",
        };
    }

    private static void RenderStripOptions(StripSessionState state)
    {
        var rows = StripOptionSpecs
            .Select((spec, index) => new SessionOptionRow(
                index,
                spec.Name,
                GetStripOptionValue(state, spec),
                spec.Required,
                spec.Description,
                spec.Expected))
            .ToArray();

        RenderSessionOptionCatalog(
            "Strip options",
            rows,
            "Commands: show options | show <catalog> | set <OPTION> [VALUE] | run | exit");
    }

    private static SessionValidationResult ValidateStripState(StripSessionState state)
    {
        var missing = new List<SessionOptionSpec>();
        var invalid = new List<string>();

        if (string.IsNullOrWhiteSpace(state.PeFile))
            missing.Add(StripOptionSpecsByName["PE_FILE"]);
        else if (!File.Exists(state.PeFile))
            invalid.Add($"PE_FILE: file not found '{state.PeFile}'.");

        if (state.Mode == "section" && string.IsNullOrWhiteSpace(state.Section))
            missing.Add(StripOptionSpecsByName["SECTION"]);

        if (state.Mode == "range")
        {
            if (string.IsNullOrWhiteSpace(state.Range))
                missing.Add(StripOptionSpecsByName["RANGE"]);
            else if (!TryParseStripRange(state.Range, out _, out _, out var error))
                invalid.Add($"RANGE: {error}.");
        }

        return new SessionValidationResult(missing, invalid);
    }

    private static string[] BuildStripArguments(StripSessionState state)
    {
        var args = new List<string> { state.PeFile! };

        if (!string.IsNullOrWhiteSpace(state.Output))
        {
            args.Add("-o");
            args.Add(state.Output);
        }

        if (!string.Equals(state.Mode, "ep", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-m");
            args.Add(state.Mode);
        }

        if (!string.IsNullOrWhiteSpace(state.Section))
        {
            args.Add("--section");
            args.Add(state.Section);
        }

        if (!string.IsNullOrWhiteSpace(state.Range))
        {
            args.Add("--range");
            args.Add(state.Range);
        }

        if (state.AnalyzeOnly)
            args.Add("--analyze");

        if (!state.TrimTrailingZeros)
            args.Add("--no-trim");

        return args.ToArray();
    }

    private static bool TryParseStripRange(string value, out uint start, out uint length, out string? error)
    {
        start = 0;
        length = 0;
        error = null;

        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            error = "expected start:length";
            return false;
        }

        try
        {
            start = Convert.ToUInt32(parts[0], parts[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
            length = Convert.ToUInt32(parts[1], parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
            return true;
        }
        catch
        {
            error = $"invalid range '{value}'";
            return false;
        }
    }

    private static async Task HandleBackdoorSetAsync(BackdoorSessionState state, string[] tokens)
    {
        if (tokens.Length < 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: set <OPTION> [VALUE].");
            return;
        }

        if (!TryResolveSessionOption(tokens[1], BackdoorOptionSpecs, out var spec, out var error))
        {
            WriteStatus(StatusPrefix.Failure, error!);
            return;
        }

        if (tokens.Length == 2)
        {
            PrintOptionRequiredInfo(spec!);
            await ShowBackdoorOptionValuesAsync(spec!);
            return;
        }

        string value = string.Join(' ', tokens.Skip(2));
        if (!TryApplyBackdoorOptionValue(state, spec!, value, out var applyError))
        {
            WriteStatus(StatusPrefix.Failure, $"{spec!.Name}: {applyError}. Expected: {spec.Expected}. Example: {spec.Example}");
            return;
        }

        WriteStatus(StatusPrefix.Success, $"{spec!.Name} => {GetBackdoorOptionValue(state, spec)}");
    }

    private static void HandleBackdoorUnset(BackdoorSessionState state, string[] tokens)
    {
        if (tokens.Length != 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: unset <OPTION>.");
            return;
        }

        if (!TryResolveSessionOption(tokens[1], BackdoorOptionSpecs, out var spec, out var error))
        {
            WriteStatus(StatusPrefix.Failure, error!);
            return;
        }

        ResetBackdoorOption(state, spec!.Name);
        WriteStatus(StatusPrefix.Success, $"{spec!.Name} => {GetBackdoorOptionValue(state, spec)}");
    }

    private static void HandleBackdoorGet(BackdoorSessionState state, string[] tokens)
    {
        if (tokens.Length != 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: get <OPTION>.");
            return;
        }

        if (!TryResolveSessionOption(tokens[1], BackdoorOptionSpecs, out var spec, out var error))
        {
            WriteStatus(StatusPrefix.Failure, error!);
            return;
        }

        WriteStatus(StatusPrefix.Info, $"{spec!.Name} => {GetBackdoorOptionValue(state, spec)}");
    }

    private static async Task HandleBackdoorHelpAsync(BackdoorSessionState state, string[] tokens)
    {
        if (tokens.Length == 1)
        {
            WriteStatus(StatusPrefix.Info, "Commands: show options | show <catalog> | set <OPTION> [VALUE] | unset <OPTION> | get <OPTION> | help [OPTION] | reset | run | exit");
            WriteStatus(StatusPrefix.Info, "Required backdoor settings are shown in the Required? column of the options view.");
            return;
        }

        if (!TryResolveSessionOption(tokens[1], BackdoorOptionSpecs, out var spec, out var error))
        {
            WriteStatus(StatusPrefix.Failure, error!);
            return;
        }

        WriteStatus(StatusPrefix.Info, $"{spec!.Name}: {spec.Details}");
        WriteStatus(StatusPrefix.Info, $"Expected: {spec.Expected}. Example: {spec.Example}");
        await Task.CompletedTask;
    }

    private static async Task ShowBackdoorOptionValuesAsync(SessionOptionSpec spec)
    {
        switch (spec.Name)
        {
            case "METHOD":
                RenderSessionChoiceCatalog("Possible values for METHOD",
                [
                    new SessionValueChoice("code-cave", "Reuse existing slack space inside the PE."),
                    new SessionValueChoice("new-section", "Add a fresh section for predictable payload capacity."),
                    new SessionValueChoice("section-ext", "Extend the last section without adding a new header."),
                    new SessionValueChoice("text-pad", "Use .text raw padding when it exists."),
                    new SessionValueChoice("tls-callback", "Use TLS callback execution on compatible targets."),
                ], "Use: set METHOD <value>");
                return;

            case "CARRIER":
                RenderSessionChoiceCatalog("Possible values for CARRIER",
                [
                    new SessionValueChoice("entry-point", "Supported carrier. Hijack the entry point and then resume normal execution."),
                ], "Only entry-point is currently implemented.");
                return;

            case "ENCRYPTION":
                RenderSessionChoiceCatalog("Possible values for ENCRYPTION",
                [
                    new SessionValueChoice("none", "Supported mode. Inject the prepared .bin as-is."),
                ], "Use encode first if you need transformed payload bytes.");
                return;

            case "REMOVE_SIGNATURE":
                RenderSessionChoiceCatalog("Possible values for REMOVE_SIGNATURE", BuildBooleanChoices("Strip the signature before patching.", "Keep the signature block untouched.").ToArray(), "Use: set REMOVE_SIGNATURE <true|false>");
                return;

            case "PATCH_SUBSYSTEM":
                RenderSessionChoiceCatalog("Possible values for PATCH_SUBSYSTEM", BuildBooleanChoices("Patch subsystem to GUI when needed.", "Keep the original subsystem.").ToArray(), "Use: set PATCH_SUBSYSTEM <true|false>");
                return;

            case "PRESERVE_ENTRY":
                RenderSessionChoiceCatalog("Possible values for PRESERVE_ENTRY", BuildBooleanChoices("Resume the original entry point after the payload.", "Do not preserve the original entry point (currently unsupported).").ToArray(), "Use: set PRESERVE_ENTRY <true|false>");
                return;

            case "PATCH_IAT":
                RenderSessionChoiceCatalog("Possible values for PATCH_IAT", BuildBooleanChoices("Allow IAT patching during the carrier pass.", "Skip IAT patching.").ToArray(), "Use: set PATCH_IAT <true|false>");
                return;

            case "PATCH_EXIT":
                RenderSessionChoiceCatalog("Possible values for PATCH_EXIT", BuildBooleanChoices("Patch exit behavior after payload execution.", "Leave exit behavior untouched.").ToArray(), "Use: set PATCH_EXIT <true|false>");
                return;

            case "DRY_RUN":
                RenderSessionChoiceCatalog("Possible values for DRY_RUN", BuildBooleanChoices("Plan and validate only.", "Patch the PE for real.").ToArray(), "Use: set DRY_RUN <true|false>");
                return;

            case "SESSION_LOG":
                RenderSessionChoiceCatalog("Possible values for SESSION_LOG", BuildTriStateChoices("Follow saved app settings.", "Force session logging on.", "Force session logging off.").ToArray(), "Use: set SESSION_LOG <auto|true|false>");
                return;

            case "VERBOSE":
            case "JSON":
                RenderSessionChoiceCatalog($"Possible values for {spec.Name}", BuildBooleanChoices("Enable the option.", "Disable the option.").ToArray(), $"Use: set {spec.Name} <true|false>");
                return;

            default:
                WriteStatus(StatusPrefix.Info, $"{spec.Name} expects: {spec.Expected}");
                WriteStatus(StatusPrefix.Info, $"Example: {spec.Example}");
                return;
        }
    }

    private static bool TryApplyBackdoorOptionValue(BackdoorSessionState state, SessionOptionSpec spec, string rawValue, out string? error)
    {
        error = null;
        switch (spec.Name)
        {
            case "PE_FILE":
                state.PeFile = rawValue;
                return true;
            case "SHELLCODE":
                state.Shellcode = rawValue;
                return true;
            case "OUTPUT":
                state.Output = rawValue;
                return true;
            case "METHOD":
            {
                string normalized = rawValue.Trim().ToLowerInvariant();
                if (!BackdoorMethodValues.Contains(normalized))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }
                state.Method = normalized;
                return true;
            }
            case "CARRIER":
            {
                string normalized = rawValue.Trim().ToLowerInvariant();
                if (!BackdoorCarrierValues.Contains(normalized))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }
                state.Carrier = normalized;
                return true;
            }
            case "ENCRYPTION":
            {
                string normalized = rawValue.Trim().ToLowerInvariant();
                if (!BackdoorEncryptionValues.Contains(normalized))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }
                state.Encryption = normalized;
                return true;
            }
            case "SECTION_NAME":
                state.SectionName = rawValue;
                return true;
            case "REMOVE_SIGNATURE":
                return TryAssignBoolean(rawValue, value => state.RemoveSignature = value, out error);
            case "PATCH_SUBSYSTEM":
                return TryAssignBoolean(rawValue, value => state.PatchSubsystem = value, out error);
            case "PRESERVE_ENTRY":
                return TryAssignBoolean(rawValue, value => state.PreserveEntry = value, out error);
            case "PATCH_IAT":
                return TryAssignBoolean(rawValue, value => state.PatchIat = value, out error);
            case "PATCH_EXIT":
                return TryAssignBoolean(rawValue, value => state.PatchExit = value, out error);
            case "CAVE_MIN_SIZE":
                if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var caveMinSize) || caveMinSize < 0)
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }
                state.CaveMinSize = caveMinSize;
                return true;
            case "DRY_RUN":
                return TryAssignBoolean(rawValue, value => state.DryRun = value, out error);
            case "SESSION_LOG":
                if (!TryParseTriState(rawValue, out var sessionLog))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }
                state.SessionLog = sessionLog;
                return true;
            case "VERBOSE":
                return TryAssignBoolean(rawValue, value => state.Verbose = value, out error);
            case "JSON":
                return TryAssignBoolean(rawValue, value => state.Json = value, out error);
        }

        error = $"unsupported option '{spec.Name}'";
        return false;
    }

    private static bool TryAssignBoolean(string rawValue, Action<bool> assign, out string? error)
    {
        error = null;
        if (!TryParseBoolean(rawValue, out var value))
        {
            error = $"invalid value '{rawValue}'";
            return false;
        }

        assign(value);
        return true;
    }

    private static void ResetBackdoorOption(BackdoorSessionState state, string optionName)
    {
        switch (optionName)
        {
            case "PE_FILE":
                state.PeFile = null;
                break;
            case "SHELLCODE":
                state.Shellcode = null;
                break;
            case "OUTPUT":
                state.Output = null;
                break;
            case "METHOD":
                state.Method = "code-cave";
                break;
            case "CARRIER":
                state.Carrier = "entry-point";
                break;
            case "ENCRYPTION":
                state.Encryption = "none";
                break;
            case "SECTION_NAME":
                state.SectionName = ".extra";
                break;
            case "REMOVE_SIGNATURE":
                state.RemoveSignature = true;
                break;
            case "PATCH_SUBSYSTEM":
                state.PatchSubsystem = true;
                break;
            case "PRESERVE_ENTRY":
                state.PreserveEntry = true;
                break;
            case "PATCH_IAT":
                state.PatchIat = true;
                break;
            case "PATCH_EXIT":
                state.PatchExit = true;
                break;
            case "CAVE_MIN_SIZE":
                state.CaveMinSize = 0;
                break;
            case "DRY_RUN":
                state.DryRun = false;
                break;
            case "SESSION_LOG":
                state.SessionLog = EncodeTriState.Auto;
                break;
            case "VERBOSE":
                state.Verbose = false;
                break;
            case "JSON":
                state.Json = false;
                break;
        }
    }

    private static string GetBackdoorOptionValue(BackdoorSessionState state, SessionOptionSpec spec)
    {
        return spec.Name switch
        {
            "PE_FILE" => string.IsNullOrWhiteSpace(state.PeFile) ? "(not set)" : state.PeFile,
            "SHELLCODE" => string.IsNullOrWhiteSpace(state.Shellcode) ? "(not set)" : state.Shellcode,
            "OUTPUT" => string.IsNullOrWhiteSpace(state.Output) ? "(auto)" : state.Output,
            "METHOD" => state.Method,
            "CARRIER" => state.Carrier,
            "ENCRYPTION" => state.Encryption,
            "SECTION_NAME" => state.SectionName,
            "REMOVE_SIGNATURE" => state.RemoveSignature ? "true" : "false",
            "PATCH_SUBSYSTEM" => state.PatchSubsystem ? "true" : "false",
            "PRESERVE_ENTRY" => state.PreserveEntry ? "true" : "false",
            "PATCH_IAT" => state.PatchIat ? "true" : "false",
            "PATCH_EXIT" => state.PatchExit ? "true" : "false",
            "CAVE_MIN_SIZE" => state.CaveMinSize.ToString(CultureInfo.InvariantCulture),
            "DRY_RUN" => state.DryRun ? "true" : "false",
            "SESSION_LOG" => GetTriStateDisplay(state.SessionLog),
            "VERBOSE" => state.Verbose ? "true" : "false",
            "JSON" => state.Json ? "true" : "false",
            _ => "(not set)",
        };
    }

    private static void RenderBackdoorOptions(BackdoorSessionState state)
    {
        var rows = BackdoorOptionSpecs
            .Select((spec, index) => new SessionOptionRow(
                index,
                spec.Name,
                GetBackdoorOptionValue(state, spec),
                spec.Required,
                spec.Description,
                spec.Expected))
            .ToArray();

        RenderSessionOptionCatalog(
            "Backdoor options",
            rows,
            "Commands: show options | show <catalog> | set <OPTION> [VALUE] | run | exit");
    }

    private static SessionValidationResult ValidateBackdoorState(BackdoorSessionState state)
    {
        var missing = new List<SessionOptionSpec>();
        var invalid = new List<string>();

        if (string.IsNullOrWhiteSpace(state.PeFile))
            missing.Add(BackdoorOptionSpecsByName["PE_FILE"]);
        else if (!File.Exists(state.PeFile))
            invalid.Add($"PE_FILE: file not found '{state.PeFile}'.");

        if (string.IsNullOrWhiteSpace(state.Shellcode))
            missing.Add(BackdoorOptionSpecsByName["SHELLCODE"]);
        else if (!File.Exists(state.Shellcode))
            invalid.Add($"SHELLCODE: file not found '{state.Shellcode}'.");

        if (state.Method is not ("code-cave" or "new-section" or "section-ext" or "text-pad" or "tls-callback"))
            invalid.Add($"METHOD: invalid value '{state.Method}'.");

        if (!string.Equals(state.Carrier, "entry-point", StringComparison.OrdinalIgnoreCase))
            invalid.Add($"CARRIER: invalid value '{state.Carrier}'. Only entry-point is currently implemented.");

        if (!string.Equals(state.Encryption, "none", StringComparison.OrdinalIgnoreCase))
            invalid.Add($"ENCRYPTION: invalid value '{state.Encryption}'. Only none is currently implemented.");

        if (state.CaveMinSize < 0)
            invalid.Add($"CAVE_MIN_SIZE: invalid value '{state.CaveMinSize}'.");

        return new SessionValidationResult(missing, invalid);
    }

    private static string[] BuildBackdoorArguments(BackdoorSessionState state)
    {
        var args = new List<string>
        {
            "--pe", state.PeFile!,
            "--shellcode", state.Shellcode!,
            "--method", state.Method,
            "--carrier", state.Carrier,
            "--encryption", state.Encryption,
            "--section-name", state.SectionName,
            "--cave-min-size", state.CaveMinSize.ToString(CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrWhiteSpace(state.Output))
        {
            args.Add("--output");
            args.Add(state.Output);
        }

        if (!state.RemoveSignature)
            args.Add("--no-remove-sig");
        if (!state.PatchSubsystem)
            args.Add("--no-patch-subsystem");
        if (!state.PreserveEntry)
            args.Add("--no-preserve-entry");
        if (!state.PatchIat)
            args.Add("--no-patch-iat");
        if (!state.PatchExit)
            args.Add("--no-patch-exit");
        if (state.DryRun)
            args.Add("--dry-run");
        if (state.SessionLog == EncodeTriState.Enabled)
            args.Add("--session-log");
        else if (state.SessionLog == EncodeTriState.Disabled)
            args.Add("--no-session-log");
        if (state.Verbose)
            args.Add("--verbose");
        if (state.Json)
            args.Add("--json");

        return args.ToArray();
    }

    private static void PrintSessionValidation(string operationName, SessionValidationResult validation)
    {
        foreach (var invalid in validation.Invalid)
            WriteStatus(StatusPrefix.Failure, invalid);

        if (validation.MissingRequired.Count == 0)
            return;

        WriteStatus(StatusPrefix.Failure, $"Missing required {operationName} option(s):");
        foreach (var spec in validation.MissingRequired)
            Console.WriteLine($"  {spec.Name,-20} {spec.Description}");
    }

    private static IEnumerable<string> GetStripSessionCompletionMatches(string[] argTokens, int currentArgIndex, string currentPrefix)
    {
        if (currentArgIndex == 0)
            return FilterCompletionMatches(SharedSessionCommands, currentPrefix);

        string verb = argTokens[0].ToLowerInvariant();
        return verb switch
        {
            "show" => GetSessionShowCompletionMatches(argTokens, currentArgIndex, currentPrefix),
            "set" when currentArgIndex == 1 => FilterCompletionMatches(StripOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "unset" when currentArgIndex == 1 => FilterCompletionMatches(StripOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "get" when currentArgIndex == 1 => FilterCompletionMatches(StripOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "help" when currentArgIndex == 1 => FilterCompletionMatches(StripOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "set" when currentArgIndex == 2 && TryResolveSessionOption(argTokens[1], StripOptionSpecs, out var spec, out _) => GetStripSessionValueCandidates(spec!),
            _ => Array.Empty<string>(),
        };
    }

    private static IEnumerable<string> GetBackdoorSessionCompletionMatches(string[] argTokens, int currentArgIndex, string currentPrefix)
    {
        if (currentArgIndex == 0)
            return FilterCompletionMatches(SharedSessionCommands, currentPrefix);

        string verb = argTokens[0].ToLowerInvariant();
        return verb switch
        {
            "show" => GetSessionShowCompletionMatches(argTokens, currentArgIndex, currentPrefix),
            "set" when currentArgIndex == 1 => FilterCompletionMatches(BackdoorOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "unset" when currentArgIndex == 1 => FilterCompletionMatches(BackdoorOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "get" when currentArgIndex == 1 => FilterCompletionMatches(BackdoorOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "help" when currentArgIndex == 1 => FilterCompletionMatches(BackdoorOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "set" when currentArgIndex == 2 && TryResolveSessionOption(argTokens[1], BackdoorOptionSpecs, out var spec, out _) => GetBackdoorSessionValueCandidates(spec!),
            _ => Array.Empty<string>(),
        };
    }

    private static IEnumerable<string> GetStripSessionValueCandidates(SessionOptionSpec spec)
    {
        return spec.Name switch
        {
            "MODE" => ["ep", "section", "all-exec", "range"],
            "ANALYZE_ONLY" => ["true", "false"],
            "TRIM_TRAILING_ZEROS" => ["true", "false"],
            _ => Array.Empty<string>(),
        };
    }

    private static IEnumerable<string> GetBackdoorSessionValueCandidates(SessionOptionSpec spec)
    {
        return spec.Name switch
        {
            "METHOD" => ["code-cave", "new-section", "section-ext", "text-pad", "tls-callback"],
            "CARRIER" => ["entry-point"],
            "ENCRYPTION" => ["none"],
            "REMOVE_SIGNATURE" or "PATCH_SUBSYSTEM" or "PRESERVE_ENTRY" or "PATCH_IAT" or "PATCH_EXIT" or "DRY_RUN" or "VERBOSE" or "JSON" => ["true", "false"],
            "SESSION_LOG" => ["auto", "true", "false"],
            _ => Array.Empty<string>(),
        };
    }
}
