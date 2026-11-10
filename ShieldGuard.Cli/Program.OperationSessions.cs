using ConsoleLogger = ShieldGuard.Cli.Ui.CliLogger;
using AnsiConsole = ShieldGuard.Cli.Ui.CliConsole;
using System.Globalization;
using Spectre.Console;
using ShieldGuard.Cli.Ui;
using ShieldGuard.Logging;
using ShieldGuard.Models;
using ShieldGuard.Services;

namespace ShieldGuard.Cli;

public static partial class Program
{
    private const string StripSessionCompletionCommand = "strip-session";
    private const string LicenseCaveSessionCompletionCommand = "licensecave-session";

    private static readonly SessionOptionSpec[] StripOptionSpecs =
    [
        new("PE_FILE", true, null, "Target PE file to strip.", "Path to the PE file that will be analyzed or carved.", "Existing PE file path", @".\loader.exe",
            WhenToChange: "Always — this names the file you want to extract bytes from.",
            DependsOn: "Used by every other option in this session."),
        new("MODE", false, "ep", "Extraction strategy.", "Choose entry-point carve, a named section, every executable section, or an explicit raw range.", "ep | section | all-exec | range", "ep",
            WhenToChange: "Pick 'section' to dump a named section, 'range' for raw byte coordinates from analyze, 'all-exec' to concatenate every executable section.",
            DependsOn: "MODE=section requires SECTION; MODE=range requires RANGE."),
        new("SECTION", false, null, "Section name when MODE=section.", "Only used when you want to dump one named PE section.", "Existing section name", ".text",
            WhenToChange: "Set when MODE=section. Type 'set SECTION' alone to list section names from the loaded PE.",
            DependsOn: "MODE=section."),
        new("RANGE", false, null, "Raw byte range when MODE=range.", "Express the range as start:length. Decimal and hex are both accepted.", "start:length", "0x400:0x200",
            WhenToChange: "Set when MODE=range. Use 'analyze <pe>' first to find the right offsets.",
            DependsOn: "MODE=range."),
        new("OUTPUT", false, "<input>.bin", "Destination .bin path.", "When unset, strip writes next to the input PE with a .bin extension.", "Writable file path", @".\licensepayload.bin",
            WhenToChange: "Override only if you want the output somewhere other than next to the input PE."),
        new("ANALYZE_ONLY", false, "false", "Analyze section layout without extracting.", "When true, strip prints the section view and entry-point context instead of writing a file.", "true | false", "true",
            WhenToChange: "Set true for a fast preview of section layout without producing a .bin."),
        new("TRIM_TRAILING_ZEROS", false, "true", "Trim null padding from the extracted result.", "Set false to preserve trailing null bytes in the extracted output.", "true | false", "true",
            WhenToChange: "Set false when downstream tooling expects the original section size including padding."),
    ];

    private static readonly IReadOnlyDictionary<string, SessionOptionSpec> StripOptionSpecsByName =
        StripOptionSpecs.ToDictionary(spec => spec.Name, spec => spec, StringComparer.OrdinalIgnoreCase);

    private static readonly SessionOptionSpec[] LicenseCaveOptionSpecs =
    [
        new("PE_FILE", true, null, "Target PE file to patch.", "Existing EXE or DLL that will receive the prepared licensepayload.", "Existing PE file path", @".\app.exe",
            WhenToChange: "Always — this names the host PE that will carry the licensepayload.",
            DependsOn: "Used by every other option in this session."),
        new("LICENSESTUB", true, null, "Prepared flat .bin licensepayload to inject.", "LicenseCave expects a ready-to-run binary licensepayload, not raw framework modules.", "Existing .bin file path", @".\licensepayload.bin",
            WhenToChange: "Always. Build the .bin with 'encode' first if you only have raw licensestub or a hex blob.",
            DependsOn: "Required by all methods."),
        new("OUTPUT", false, "<input>.licensecaveed.exe", "Destination path for the patched PE.", "When unset, licensecave writes next to the target PE using a .licensecaveed suffix.", "Writable file path", @".\patched.exe",
            WhenToChange: "Override when you want the patched binary stored somewhere specific."),
        new("METHOD", false, "code-cave", "Injection method.", "Choose how the licensepayload is placed into the target PE.", "code-cave | new-section | section-ext | text-pad | tls-callback", "code-cave",
            WhenToChange: "code-cave is limited by available slack space. new-section adds a PE section with more room. tls-callback enables pre-main execution on x64.",
            DependsOn: "code-cave honors CAVE_MIN_SIZE; new-section / section-ext use SECTION_NAME."),
        new("CARRIER", false, "entry-point", "LicensePayload invocation strategy.", "entry-point hooks the EXE/DLL entry point and resumes after the licensepayload runs.", "entry-point | dll-main", "entry-point",
            WhenToChange: "Pick dll-main only when the target is a DLL — both modes patch the entry point but the DLL path leaves the subsystem unchanged."),
        new("ENCRYPTION", false, "none", "LicensePayload transform mode.", "Encryption is performed in the encode pipeline, not here.", "none", "none",
            WhenToChange: "Use the encode command's encoder/envelope/POLY options to transform bytes; licensecave accepts the result as-is."),
        new("SECTION_NAME", false, ".extra", "Section name for section-creating methods.", "Used when the selected method creates or extends section data.", "PE section name", ".extra",
            WhenToChange: "Rename when you want the new section to look like a known one (e.g. .rsrc, .reloc).",
            DependsOn: "METHOD=new-section or METHOD=section-ext."),
        new("REMOVE_SIGNATURE", false, "true", "Remove Authenticode signature before patching.", "Set false to keep the existing signature block untouched (will fail signature checks regardless of patching).", "true | false", "true",
            WhenToChange: "Default is correct for almost every case; only set false if downstream tooling needs the broken signature retained."),
        new("PATCH_SUBSYSTEM", false, "true", "Patch the output subsystem to GUI when needed.", "Set false to preserve the original subsystem (SUBSYSTEM_WINDOWS_CUI / GUI).", "true | false", "true",
            WhenToChange: "Set false when you want the patched binary to keep its original console behavior.",
            DependsOn: "Ignored for DLL targets."),
        new("PRESERVE_ENTRY", false, "true", "Resume the original entry point after licensepayload execution.", "The current carrier implementation always requires true.", "true | false", "true",
            WhenToChange: "Leave true. Provided for forward compatibility with future carriers."),
        new("PATCH_IAT", false, "true", "Patch imports as needed for the carrier.", "Set false to skip IAT patching during the licensecave pass.", "true | false", "true",
            WhenToChange: "Set false only if you've already arranged the imports the carrier needs."),
        new("PATCH_EXIT", false, "true", "Patch exit behavior after the licensepayload runs.", "Set false to leave exit handling untouched.", "true | false", "true",
            WhenToChange: "Set false when the licensepayload is responsible for its own exit / continuation."),
        new("CAVE_MIN_SIZE", false, "0", "Minimum code cave size when METHOD=code-cave.", "Raise this when you want to reject tiny caves during planning.", "Non-negative integer", "512",
            WhenToChange: "Raise to filter out caves that are too small for the licensepayload + thunks; 0 accepts any cave large enough to fit the licensepayload.",
            DependsOn: "METHOD=code-cave."),
        new("DRY_RUN", false, "false", "Analyze feasibility without writing output.", "When true, licensecave prints the plan and exits before patching.", "true | false", "true",
            WhenToChange: "Set true to validate that the chosen method has enough room before committing to a patched file."),
        new("SESSION_LOG", false, "auto", "Per-run session logging override.", "auto respects saved settings, true forces logging on, false forces it off.", "auto | true | false", "auto",
            WhenToChange: "Force on when you want a per-run log even if logging is globally disabled."),
        new("VERBOSE", false, "false", "Show detailed licensecave logs.", "Enables verbose CLI logging during analysis and patching.", "true | false", "true",
            WhenToChange: "Useful for diagnosing why a method was rejected or which cave was selected."),
        new("JSON", false, "false", "Emit machine-friendly JSON instead of the styled report.", "Useful for scripts or automation.", "true | false", "true",
            WhenToChange: "Set true when piping into another tool. The GUI sets this automatically."),
        new("MODE", false, "normal", "LicenseCave execution mode.", "normal: licensemodule + host run together. quiet: host runs normally; licensemodule runs silently when any argument is detected. licensedropper: host drops and runs the licensemodule EXE from an embedded .dpl section on first launch.", "normal | quiet | licensedropper", "quiet",
            WhenToChange: "Pick 'quiet' for behind-the-scenes execution, 'licensedropper' to embed and run a separate licensemodule EXE.",
            DependsOn: "MODE=licensedropper requires LICENSEMODULE_FILE."),
        new("LICENSEMODULE_FILE", false, null, "Standalone licensemodule EXE to embed (LicenseDropper mode only).", "LicenseDropper mode reads this pre-compiled licensemodule, XOR-encrypts it, and appends it to the host PE. Required when MODE=licensedropper.", "Existing .exe file path", @".\licensemodule.exe",
            WhenToChange: "Set when MODE=licensedropper. The file you point to is what the host drops at runtime.",
            DependsOn: "MODE=licensedropper."),
    ];

    private static readonly IReadOnlyDictionary<string, SessionOptionSpec> LicenseCaveOptionSpecsByName =
        LicenseCaveOptionSpecs.ToDictionary(spec => spec.Name, spec => spec, StringComparer.OrdinalIgnoreCase);

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

    private sealed class LicenseCaveSessionState
    {
        public string? PeFile { get; set; }
        public string? LicenseStub { get; set; }
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
        public string LicenseMode { get; set; } = "normal";
        public string? LicenseModuleFile { get; set; }
    }

    private sealed record SessionValidationResult(IReadOnlyList<SessionOptionSpec> MissingRequired, IReadOnlyList<string> Invalid);

    private static async Task<int> RunStripInteractiveSessionAsync()
    {
        var state = new StripSessionState();
        RenderStripOptions(state);
        PrintStripMissingHint(state);
        WriteStatus(StatusPrefix.Info, "Type 'help' for session commands, 'help <OPTION>' for one option, 'show options' to reprint the table.");

        while (true)
        {
            AnsiConsole.WriteLine();
            string? input;
            try
            {
                input = ReadLineWithEditor(
                    $"[{UiColors.Header}]shieldguard[/] [{UiColors.Accent}]strip[/] [{UiColors.Accent}]>[/] ",
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
            if (IsHelpToken(command))
            {
                await HandleStripHelpAsync(state, tokens);
                continue;
            }
            switch (command)
            {
                case "show" when tokens.Length >= 2 && tokens[1].Equals("options", StringComparison.OrdinalIgnoreCase):
                    RenderStripOptions(state);
                    PrintStripMissingHint(state);
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

                    WriteStatus(StatusPrefix.Info, "Final configuration:");
                    RenderStripOptions(state);
                    WriteStatus(StatusPrefix.Info, "All required options valid. Running strip...");
                    await RunStripAsync(BuildStripArguments(state));
                    break;
                }

                case "exit" or "quit":
                    return 0;

                default:
                    WriteStatus(StatusPrefix.Failure, SuggestSessionCommand(tokens[0]));
                    break;
            }
        }

        return 0;
    }

    private static async Task<int> RunLicenseCaveInteractiveSessionAsync()
    {
        var state = new LicenseCaveSessionState();
        RenderLicenseCaveOptions(state);
        PrintLicenseCaveMissingHint(state);
        WriteStatus(StatusPrefix.Info, "Type 'help' for session commands, 'help <OPTION>' for one option, 'show options' to reprint the table.");

        while (true)
        {
            AnsiConsole.WriteLine();
            string? input;
            try
            {
                input = ReadLineWithEditor(
                    $"[{UiColors.Header}]shieldguard[/] [{UiColors.Accent}]licensecave[/] [{UiColors.Accent}]>[/] ",
                    "licensecave-session",
                    LicenseCaveSessionCompletionCommand);
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
            if (IsHelpToken(command))
            {
                await HandleLicenseCaveHelpAsync(state, tokens);
                continue;
            }
            switch (command)
            {
                case "show" when tokens.Length >= 2 && tokens[1].Equals("options", StringComparison.OrdinalIgnoreCase):
                    RenderLicenseCaveOptions(state);
                    PrintLicenseCaveMissingHint(state);
                    break;

                case "show":
                    await RunShowAsync(tokens.Skip(1).ToArray());
                    break;

                case "clear" or "cls":
                    try { AnsiConsole.Clear(); } catch { /* non-interactive */ }
                    break;

                case "set":
                    await HandleLicenseCaveSetAsync(state, tokens);
                    break;

                case "unset":
                    HandleLicenseCaveUnset(state, tokens);
                    break;

                case "get":
                    HandleLicenseCaveGet(state, tokens);
                    break;

                case "reset":
                    state = new LicenseCaveSessionState();
                    WriteStatus(StatusPrefix.Success, "All licensecave options reset to defaults.");
                    break;

                case "run" or "build":
                {
                    var validation = ValidateLicenseCaveState(state);
                    if (validation.MissingRequired.Count > 0 || validation.Invalid.Count > 0)
                    {
                        PrintSessionValidation("licensecave", validation);
                        break;
                    }

                    WriteStatus(StatusPrefix.Info, "Final configuration:");
                    RenderLicenseCaveOptions(state);
                    WriteStatus(StatusPrefix.Info, "All required options valid. Running licensecave...");
                    await RunLicenseCaveAsync(BuildLicenseCaveArguments(state));
                    break;
                }

                case "exit" or "quit":
                    return 0;

                default:
                    WriteStatus(StatusPrefix.Failure, SuggestSessionCommand(tokens[0]));
                    break;
            }
        }

        return 0;
    }

    private static async Task HandleStripSetAsync(StripSessionState state, string[] tokens)
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
            await HandleStripHelpAsync(state, new[] { "help", tokens[1] });
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
        PrintStripMissingHint(state, suppressIfNoneMissing: false);
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
            PrintStripUsage();
            PrintSessionCommandList();
            return;
        }

        if (!TryResolveSessionOption(tokens[1], StripOptionSpecs, out var spec, out var error))
        {
            WriteStatus(StatusPrefix.Failure, error!);
            return;
        }

        PrintOptionHelpDetail(spec!, GetStripOptionValue(state, spec!));
        await ShowStripOptionValuesAsync(state, spec!);
    }

    /// <summary>
    /// Print a "next required field" hint based on the current strip state.
    /// When everything required is set, prints a "ready to build" hint instead
    /// (unless <paramref name="suppressIfNoneMissing"/> is true).
    /// </summary>
    private static void PrintStripMissingHint(StripSessionState state, bool suppressIfNoneMissing = true)
    {
        var validation = ValidateStripState(state);
        if (validation.MissingRequired.Count == 0 && validation.Invalid.Count == 0)
        {
            if (!suppressIfNoneMissing)
                WriteStatus(StatusPrefix.Info, "All required options set. Type 'run' to extract or 'show options' to review.");
            return;
        }

        if (validation.MissingRequired.Count > 0)
        {
            var names = string.Join(", ", validation.MissingRequired.Select(s => s.Name));
            WriteStatus(StatusPrefix.Warning,
                $"{validation.MissingRequired.Count} required option(s) not set: {names}. " +
                $"Type 'help {validation.MissingRequired[0].Name}' for details.");
        }

        foreach (var invalid in validation.Invalid)
            WriteStatus(StatusPrefix.Failure, invalid);
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
        var args = new List<string>();

        args.Add("-Pe");
        args.Add(state.PeFile!);

        if (!string.IsNullOrWhiteSpace(state.Output))
        {
            args.Add("-Output");
            args.Add(state.Output);
        }

        if (!string.Equals(state.Mode, "ep", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-Mode");
            args.Add(state.Mode);
        }

        if (!string.IsNullOrWhiteSpace(state.Section))
        {
            args.Add("-Section");
            args.Add(state.Section);
        }

        if (!string.IsNullOrWhiteSpace(state.Range))
        {
            args.Add("-Range");
            args.Add(state.Range);
        }

        if (state.AnalyzeOnly)
            args.Add("-Analyze");

        if (!state.TrimTrailingZeros)
            args.Add("-NoTrim");

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

    private static async Task HandleLicenseCaveSetAsync(LicenseCaveSessionState state, string[] tokens)
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
            await HandleLicenseCaveHelpAsync(state, new[] { "help", tokens[1] });
            return;
        }

        if (!TryResolveSessionOption(tokens[1], LicenseCaveOptionSpecs, out var spec, out var error))
        {
            WriteStatus(StatusPrefix.Failure, error!);
            return;
        }

        if (tokens.Length == 2)
        {
            PrintOptionRequiredInfo(spec!);
            await ShowLicenseCaveOptionValuesAsync(spec!);
            return;
        }

        string value = string.Join(' ', tokens.Skip(2));
        if (!TryApplyLicenseCaveOptionValue(state, spec!, value, out var applyError))
        {
            WriteStatus(StatusPrefix.Failure, $"{spec!.Name}: {applyError}. Expected: {spec.Expected}. Example: {spec.Example}");
            return;
        }

        WriteStatus(StatusPrefix.Success, $"{spec!.Name} => {GetLicenseCaveOptionValue(state, spec)}");
        PrintLicenseCaveMissingHint(state, suppressIfNoneMissing: false);
    }

    private static void HandleLicenseCaveUnset(LicenseCaveSessionState state, string[] tokens)
    {
        if (tokens.Length != 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: unset <OPTION>.");
            return;
        }

        if (!TryResolveSessionOption(tokens[1], LicenseCaveOptionSpecs, out var spec, out var error))
        {
            WriteStatus(StatusPrefix.Failure, error!);
            return;
        }

        ResetLicenseCaveOption(state, spec!.Name);
        WriteStatus(StatusPrefix.Success, $"{spec!.Name} => {GetLicenseCaveOptionValue(state, spec)}");
    }

    private static void HandleLicenseCaveGet(LicenseCaveSessionState state, string[] tokens)
    {
        if (tokens.Length != 2)
        {
            WriteStatus(StatusPrefix.Failure, "Usage: get <OPTION>.");
            return;
        }

        if (!TryResolveSessionOption(tokens[1], LicenseCaveOptionSpecs, out var spec, out var error))
        {
            WriteStatus(StatusPrefix.Failure, error!);
            return;
        }

        WriteStatus(StatusPrefix.Info, $"{spec!.Name} => {GetLicenseCaveOptionValue(state, spec)}");
    }

    private static async Task HandleLicenseCaveHelpAsync(LicenseCaveSessionState state, string[] tokens)
    {
        if (tokens.Length == 1)
        {
            PrintLicenseCaveUsage();
            PrintSessionCommandList();
            return;
        }

        if (!TryResolveSessionOption(tokens[1], LicenseCaveOptionSpecs, out var spec, out var error))
        {
            WriteStatus(StatusPrefix.Failure, error!);
            return;
        }

        PrintOptionHelpDetail(spec!, GetLicenseCaveOptionValue(state, spec!));
        await ShowLicenseCaveOptionValuesAsync(spec!);
    }

    /// <summary>Print a "next required field" hint based on current licensecave state.</summary>
    private static void PrintLicenseCaveMissingHint(LicenseCaveSessionState state, bool suppressIfNoneMissing = true)
    {
        var validation = ValidateLicenseCaveState(state);
        if (validation.MissingRequired.Count == 0 && validation.Invalid.Count == 0)
        {
            if (!suppressIfNoneMissing)
                WriteStatus(StatusPrefix.Info, "All required options set. Type 'run' to patch or 'show options' to review.");
            return;
        }

        if (validation.MissingRequired.Count > 0)
        {
            var names = string.Join(", ", validation.MissingRequired.Select(s => s.Name));
            WriteStatus(StatusPrefix.Warning,
                $"{validation.MissingRequired.Count} required option(s) not set: {names}. " +
                $"Type 'help {validation.MissingRequired[0].Name}' for details.");
        }

        foreach (var invalid in validation.Invalid)
            WriteStatus(StatusPrefix.Failure, invalid);
    }

    /// <summary>One-line cheat-sheet of the session's command grammar.</summary>
    private static void PrintSessionCommandList()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold {UiColors.Header}]SESSION COMMANDS[/]");
        AnsiConsole.MarkupLine($"  [{UiColors.Rule}]{new string('─', 16)}[/]");
        var lines = new (string cmd, string desc)[]
        {
            ("show options",            "Reprint the options table with current values."),
            ("show <catalog>",          "Browse catalogs (templates, modules, encoders, etc.)."),
            ("set <OPTION> <VALUE>",    "Set a parameter; also accepts: set <OPTION>=<VALUE>"),
            ("set <OPTION>",            "Show accepted values / current state for one parameter."),
            ("unset <OPTION>",          "Clear a parameter (revert to default if any)."),
            ("get <OPTION>",            "Print the current value of one parameter."),
            ("help",                    "Show full command help and the session grammar."),
            ("help <OPTION>",           "Detailed help for one parameter (also: set <OPTION> -h)."),
            ("reset",                   "Clear all user-set values; restore defaults."),
            ("run / build",             "Validate required fields and execute."),
            ("exit / quit",             "Leave the session."),
        };
        int width = lines.Max(l => l.cmd.Length);
        foreach (var (cmd, desc) in lines)
            AnsiConsole.MarkupLine($"  [{UiColors.Accent}]{Markup.Escape(cmd.PadRight(width))}[/]   [{UiColors.Value}]{Markup.Escape(desc)}[/]");
        AnsiConsole.WriteLine();
    }

    private static async Task ShowLicenseCaveOptionValuesAsync(SessionOptionSpec spec)
    {
        await Task.CompletedTask;
        switch (spec.Name)
        {
            case "METHOD":
                RenderSessionChoiceCatalog("Possible values for METHOD",
                [
                    new SessionValueChoice("code-cave", "Reuse existing slack space inside the PE."),
                    new SessionValueChoice("new-section", "Add a fresh section for predictable licensepayload capacity."),
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
                ], "Use encode first if you need transformed licensepayload bytes.");
                return;

            case "REMOVE_SIGNATURE":
                RenderSessionChoiceCatalog("Possible values for REMOVE_SIGNATURE", BuildBooleanChoices("Strip the signature before patching.", "Keep the signature block untouched.").ToArray(), "Use: set REMOVE_SIGNATURE <true|false>");
                return;

            case "PATCH_SUBSYSTEM":
                RenderSessionChoiceCatalog("Possible values for PATCH_SUBSYSTEM", BuildBooleanChoices("Patch subsystem to GUI when needed.", "Keep the original subsystem.").ToArray(), "Use: set PATCH_SUBSYSTEM <true|false>");
                return;

            case "PRESERVE_ENTRY":
                RenderSessionChoiceCatalog("Possible values for PRESERVE_ENTRY", BuildBooleanChoices("Resume the original entry point after the licensepayload.", "Do not preserve the original entry point (currently unsupported).").ToArray(), "Use: set PRESERVE_ENTRY <true|false>");
                return;

            case "PATCH_IAT":
                RenderSessionChoiceCatalog("Possible values for PATCH_IAT", BuildBooleanChoices("Allow IAT patching during the carrier pass.", "Skip IAT patching.").ToArray(), "Use: set PATCH_IAT <true|false>");
                return;

            case "PATCH_EXIT":
                RenderSessionChoiceCatalog("Possible values for PATCH_EXIT", BuildBooleanChoices("Patch exit behavior after licensepayload execution.", "Leave exit behavior untouched.").ToArray(), "Use: set PATCH_EXIT <true|false>");
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

    private static bool TryApplyLicenseCaveOptionValue(LicenseCaveSessionState state, SessionOptionSpec spec, string rawValue, out string? error)
    {
        error = null;
        switch (spec.Name)
        {
            case "PE_FILE":
                state.PeFile = rawValue;
                return true;
            case "LICENSESTUB":
                state.LicenseStub = rawValue;
                return true;
            case "OUTPUT":
                state.Output = rawValue;
                return true;
            case "METHOD":
            {
                string normalized = rawValue.Trim().ToLowerInvariant();
                if (!LicenseCaveMethodValues.Contains(normalized))
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
                if (!LicenseCaveCarrierValues.Contains(normalized))
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
                if (!LicenseCaveEncryptionValues.Contains(normalized))
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
            case "MODE":
            {
                string normalized = rawValue.Trim().ToLowerInvariant();
                if (!LicenseModeValues.Contains(normalized))
                {
                    error = $"invalid value '{rawValue}'";
                    return false;
                }
                state.LicenseMode = normalized;
                return true;
            }
            case "LICENSEMODULE_FILE":
                state.LicenseModuleFile = rawValue;
                return true;
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

    private static void ResetLicenseCaveOption(LicenseCaveSessionState state, string optionName)
    {
        switch (optionName)
        {
            case "PE_FILE":
                state.PeFile = null;
                break;
            case "LICENSESTUB":
                state.LicenseStub = null;
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
            case "MODE":
                state.LicenseMode = "normal";
                break;
            case "LICENSEMODULE_FILE":
                state.LicenseModuleFile = null;
                break;
        }
    }

    private static string GetLicenseCaveOptionValue(LicenseCaveSessionState state, SessionOptionSpec spec)
    {
        return spec.Name switch
        {
            "PE_FILE" => string.IsNullOrWhiteSpace(state.PeFile) ? "(not set)" : state.PeFile,
            "LICENSESTUB" => string.IsNullOrWhiteSpace(state.LicenseStub) ? "(not set)" : state.LicenseStub,
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
            "MODE" => state.LicenseMode,
            "LICENSEMODULE_FILE" => string.IsNullOrWhiteSpace(state.LicenseModuleFile) ? "(not set)" : state.LicenseModuleFile,
            _ => "(not set)",
        };
    }

    private static void RenderLicenseCaveOptions(LicenseCaveSessionState state)
    {
        var rows = LicenseCaveOptionSpecs
            .Select((spec, index) => new SessionOptionRow(
                index,
                spec.Name,
                GetLicenseCaveOptionValue(state, spec),
                spec.Required,
                spec.Description,
                spec.Expected))
            .ToArray();

        RenderSessionOptionCatalog(
            "LicenseCave options",
            rows,
            "Commands: show options | show <catalog> | set <OPTION> [VALUE] | run | exit");
    }

    private static SessionValidationResult ValidateLicenseCaveState(LicenseCaveSessionState state)
    {
        var missing = new List<SessionOptionSpec>();
        var invalid = new List<string>();

        if (string.IsNullOrWhiteSpace(state.PeFile))
            missing.Add(LicenseCaveOptionSpecsByName["PE_FILE"]);
        else if (!File.Exists(state.PeFile))
            invalid.Add($"PE_FILE: file not found '{state.PeFile}'.");

        if (string.IsNullOrWhiteSpace(state.LicenseStub))
            missing.Add(LicenseCaveOptionSpecsByName["LICENSESTUB"]);
        else if (!File.Exists(state.LicenseStub))
            invalid.Add($"LICENSESTUB: file not found '{state.LicenseStub}'.");

        if (state.Method is not ("code-cave" or "new-section" or "section-ext" or "text-pad" or "tls-callback"))
            invalid.Add($"METHOD: invalid value '{state.Method}'.");

        if (!string.Equals(state.Carrier, "entry-point", StringComparison.OrdinalIgnoreCase))
            invalid.Add($"CARRIER: invalid value '{state.Carrier}'. Only entry-point is currently implemented.");

        if (!string.Equals(state.Encryption, "none", StringComparison.OrdinalIgnoreCase))
            invalid.Add($"ENCRYPTION: invalid value '{state.Encryption}'. Only none is currently implemented.");

        if (state.CaveMinSize < 0)
            invalid.Add($"CAVE_MIN_SIZE: invalid value '{state.CaveMinSize}'.");

        if (!LicenseModeValues.Contains(state.LicenseMode))
            invalid.Add($"MODE: invalid value '{state.LicenseMode}'. Valid: {string.Join(", ", LicenseModeValues)}.");

        if (state.LicenseMode == "licensedropper")
        {
            if (string.IsNullOrWhiteSpace(state.LicenseModuleFile))
                missing.Add(LicenseCaveOptionSpecsByName["LICENSEMODULE_FILE"]);
            else if (!File.Exists(state.LicenseModuleFile))
                invalid.Add($"LICENSEMODULE_FILE: file not found '{state.LicenseModuleFile}'.");
        }

        return new SessionValidationResult(missing, invalid);
    }

    private static string[] BuildLicenseCaveArguments(LicenseCaveSessionState state)
    {
        var args = new List<string>
        {
            "-Pe", state.PeFile!,
            "-LicenseStub", state.LicenseStub!,
            "-Method", state.Method,
            "-Carrier", state.Carrier,
            "-Encryption", state.Encryption,
            "-SectionName", state.SectionName,
            "-CaveMinSize", state.CaveMinSize.ToString(CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrWhiteSpace(state.Output))
        {
            args.Add("-Output");
            args.Add(state.Output);
        }

        if (!state.RemoveSignature)
            args.Add("-NoRemoveSig");
        if (!state.PatchSubsystem)
            args.Add("-NoPatchSubsystem");
        if (!state.PreserveEntry)
            args.Add("-NoPreserveEntry");
        if (!state.PatchIat)
            args.Add("-NoPatchIat");
        if (!state.PatchExit)
            args.Add("-NoPatchExit");
        if (state.DryRun)
            args.Add("-DryRun");
        if (state.SessionLog == EncodeTriState.Enabled)
            args.Add("-SessionLog");
        else if (state.SessionLog == EncodeTriState.Disabled)
            args.Add("-NoSessionLog");
        if (state.Verbose)
            args.Add("-Verbose");
        if (state.Json)
            args.Add("-Json");

        args.Add("-Mode");
        args.Add(state.LicenseMode);

        if (!string.IsNullOrWhiteSpace(state.LicenseModuleFile))
        {
            args.Add("-LicenseModule");
            args.Add(state.LicenseModuleFile);
        }

        return args.ToArray();
    }

    private static void PrintSessionValidation(string operationName, SessionValidationResult validation)
    {
        foreach (var invalid in validation.Invalid)
            WriteStatus(StatusPrefix.Failure, invalid);

        if (validation.MissingRequired.Count == 0)
            return;

        WriteStatus(StatusPrefix.Failure, $"Missing required {operationName} option(s):");
        UsageFormatter.PrintFields("Missing options",
            validation.MissingRequired.Select(spec => new UsageNote(spec.Name, spec.Description)),
            UsageFormatter.DefaultConsole(Console.Error));
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

    private static IEnumerable<string> GetLicenseCaveSessionCompletionMatches(string[] argTokens, int currentArgIndex, string currentPrefix)
    {
        if (currentArgIndex == 0)
            return FilterCompletionMatches(SharedSessionCommands, currentPrefix);

        string verb = argTokens[0].ToLowerInvariant();
        return verb switch
        {
            "show" => GetSessionShowCompletionMatches(argTokens, currentArgIndex, currentPrefix),
            "set" when currentArgIndex == 1 => FilterCompletionMatches(LicenseCaveOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "unset" when currentArgIndex == 1 => FilterCompletionMatches(LicenseCaveOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "get" when currentArgIndex == 1 => FilterCompletionMatches(LicenseCaveOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "help" when currentArgIndex == 1 => FilterCompletionMatches(LicenseCaveOptionSpecs.Select(spec => spec.Name), currentPrefix),
            "set" when currentArgIndex == 2 && TryResolveSessionOption(argTokens[1], LicenseCaveOptionSpecs, out var spec, out _) => GetLicenseCaveSessionValueCandidates(spec!),
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

    private static IEnumerable<string> GetLicenseCaveSessionValueCandidates(SessionOptionSpec spec)
    {
        return spec.Name switch
        {
            "METHOD" => ["code-cave", "new-section", "section-ext", "text-pad", "tls-callback"],
            "CARRIER" => ["entry-point"],
            "ENCRYPTION" => ["none"],
            "MODE" => ["normal", "quiet", "licensedropper"],
            "REMOVE_SIGNATURE" or "PATCH_SUBSYSTEM" or "PRESERVE_ENTRY" or "PATCH_IAT" or "PATCH_EXIT" or "DRY_RUN" or "VERBOSE" or "JSON" => ["true", "false"],
            "SESSION_LOG" => ["auto", "true", "false"],
            _ => Array.Empty<string>(),
        };
    }
}
