namespace Washmachine.Cli.Ui;

using Spectre.Console;
using Spectre.Console.Rendering;

/// <summary>
/// Renders the Washmachine welcome chrome: a three-tone block-letter logo,
/// a compact boxed title card, an info grid, and tip lines. Inspired by the
/// layered banner layout used by <c>nousresearch/hermes-agent</c>.
/// </summary>
public static class Banner
{
    public const string AppVersion = "1.0.0";
    public const string Tagline = "Shellcode Loader Builder & PE Backdoor Toolkit";
    public const string Author = "0xhmza";
    public const string AuthorUrl = "https://github.com/0xhmza";

    // Hand-crafted block logo (5 rows). Rendered with the active scheme's
    // three-tone banner gradient: primary → secondary → tertiary.
    private static readonly string[] LogoRows = new[]
    {
        @"██╗    ██╗ █████╗ ███████╗██╗  ██╗███╗   ███╗ █████╗  ██████╗██╗  ██╗██╗███╗   ██╗███████╗",
        @"██║    ██║██╔══██╗██╔════╝██║  ██║████╗ ████║██╔══██╗██╔════╝██║  ██║██║████╗  ██║██╔════╝",
        @"██║ █╗ ██║███████║███████╗███████║██╔████╔██║███████║██║     ███████║██║██╔██╗ ██║█████╗  ",
        @"██║███╗██║██╔══██║╚════██║██╔══██║██║╚██╔╝██║██╔══██║██║     ██╔══██║██║██║╚██╗██║██╔══╝  ",
        @"╚███╔███╔╝██║  ██║███████║██║  ██║██║ ╚═╝ ██║██║  ██║╚██████╗██║  ██║██║██║ ╚████║███████╗",
        @" ╚══╝╚══╝ ╚═╝  ╚═╝╚══════╝╚═╝  ╚═╝╚═╝     ╚═╝╚═╝  ╚═╝ ╚═════╝╚═╝  ╚═╝╚═╝╚═╝  ╚═══╝╚══════╝",
    };

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

    /// <summary>
    /// Write the three-section welcome chrome to the console. Safe to call
    /// multiple times (e.g. from the REPL <c>banner</c> command).
    /// </summary>
    public static void Render(int? templateCount = null, int? sectionCount = null,
                              int? snippetCount = null, string? compilerStatus = null)
    {
        AnsiConsole.WriteLine();

        // The block logo needs ≥ 92 columns to render cleanly. Fall back to
        // the compact boxed title on narrow terminals (small shells, CI, pipe).
        if (SafeConsoleWidth() >= 92)
        {
            RenderLogo();
            AnsiConsole.WriteLine();
        }

        RenderCompactTitleCard();
        AnsiConsole.WriteLine();

        RenderInfoGrid(templateCount, sectionCount, snippetCount, compilerStatus);
        AnsiConsole.WriteLine();

        RenderTipLines();
    }

    private static void RenderLogo()
    {
        var scheme = UiColors.ActiveScheme;
        var width = SafeConsoleWidth();

        // Truncate rows to terminal width so narrow terminals don't wrap.
        var rows = new List<IRenderable>();
        var gradient = new[]
        {
            scheme.BannerPrimary,
            scheme.BannerPrimary,
            scheme.BannerSecondary,
            scheme.BannerSecondary,
            scheme.BannerTertiary,
            scheme.BannerTertiary,
        };

        for (int i = 0; i < LogoRows.Length; i++)
        {
            var line = LogoRows[i];
            if (line.Length > width) line = line[..Math.Max(0, width)];
            var color = gradient[Math.Min(i, gradient.Length - 1)];
            rows.Add(new Markup($"[bold {color}]{Markup.Escape(line)}[/]"));
        }

        AnsiConsole.Write(new Padder(new Rows(rows)).Padding(2, 0, 2, 0));
    }

    private static void RenderCompactTitleCard()
    {
        var scheme = UiColors.ActiveScheme;
        int boxWidth = Math.Min(SafeConsoleWidth() - 4, 88);
        if (boxWidth < 30) return;

        string titleLine = $"⚙ WASHMACHINE — {Tagline}";
        string versionLine = $"v{AppVersion} · by {Author} · scheme: {scheme.Name}";

        int inner = boxWidth - 4; // 2 border chars + 2 padding spaces per side
        titleLine = Fit(titleLine, inner);
        versionLine = Fit(versionLine, inner);

        string bar = new string('═', boxWidth - 2);

        AnsiConsole.MarkupLine($"  [bold {scheme.BoxBorder}]╔{bar}╗[/]");
        AnsiConsole.MarkupLine(
            $"  [bold {scheme.BoxBorder}]║[/] [bold {scheme.BannerPrimary}]{Markup.Escape(titleLine)}[/] [bold {scheme.BoxBorder}]║[/]");
        AnsiConsole.MarkupLine(
            $"  [bold {scheme.BoxBorder}]║[/] [{scheme.Muted}]{Markup.Escape(versionLine)}[/] [bold {scheme.BoxBorder}]║[/]");
        AnsiConsole.MarkupLine($"  [bold {scheme.BoxBorder}]╚{bar}╝[/]");
    }

    private static void RenderInfoGrid(int? templates, int? sections, int? snippets, string? compilerStatus)
    {
        var s = UiColors.ActiveScheme;

        string tmpl  = templates.HasValue ? $"{templates} templates"                       : "templates: —";
        string snip  = sections.HasValue && snippets.HasValue
                        ? $"{sections} categories · {snippets} snippets"
                        : "snippets: —";
        string comp  = string.IsNullOrWhiteSpace(compilerStatus)
                        ? "compiler: —"
                        : compilerStatus!;

        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap());

        grid.AddRow(
            $"[bold {s.Accent}]v{AppVersion}[/]",
            $"[{s.Muted}]│[/]",
            $"[{s.Value}]{tmpl}[/]",
            $"[{s.Muted}]│[/]",
            $"[{s.Value}]{snip}[/]",
            $"[{s.Muted}]│[/]",
            $"[{s.Value}]{comp}[/]");

        AnsiConsole.Write(new Panel(grid)
            .Header($"[bold {s.Header}] Washmachine [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(UiColors.BoxBorderColor)
            .Padding(1, 0));
    }

    private static void RenderTipLines()
    {
        var s = UiColors.ActiveScheme;
        var fact = FunnyFacts[Random.Shared.Next(FunnyFacts.Length)];

        AnsiConsole.MarkupLine(
            $"  [{s.Muted}]{Markup.Escape(Tagline)}[/]");
        AnsiConsole.MarkupLine(
            $"  [{s.Muted}]by[/] [{s.Link} link={AuthorUrl}]{Author}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"  [{s.Muted}]Type[/] [{s.Accent}]help[/] [{s.Muted}]for commands,[/] " +
            $"[{s.Accent}]help <command>[/] [{s.Muted}]for details[/]");
        AnsiConsole.MarkupLine(
            $"  [{s.Muted}]Keys:[/] [{s.Accent}]↑/↓[/] history  " +
            $"[{s.Accent}]←/→[/] move  [{s.Accent}]Home/End[/] jump  [{s.Accent}]Tab[/] complete");
        AnsiConsole.MarkupLine(
            $"  [{s.Muted}]Theme:[/] [{s.Accent}]scheme[/] [{s.Muted}]lists color schemes,[/] " +
            $"[{s.Accent}]scheme <name>[/] [{s.Muted}]switches theme[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [{s.Muted}]💡 {Markup.Escape(fact)}[/]");
        AnsiConsole.WriteLine();
    }

    private static int SafeConsoleWidth()
    {
        try
        {
            int w = Console.WindowWidth;
            return w > 20 ? w : 100;
        }
        catch
        {
            return 100;
        }
    }

    private static string Fit(string text, int width)
    {
        if (text.Length >= width) return text.Substring(0, width);
        return text + new string(' ', width - text.Length);
    }
}
