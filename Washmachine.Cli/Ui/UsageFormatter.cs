namespace Washmachine.Cli.Ui;

using Spectre.Console;
using Spectre.Console.Rendering;

public record UsageOption(string Flag, string Description, string? Default = null);
public record UsageExample(string Command, string Description);

public record CommandUsage(
    string Name,
    string Syntax,
    string Description,
    UsageOption[]? Required = null,
    UsageOption[]? Options = null,
    UsageExample[]? Examples = null,
    string[]? Notes = null);

/// <summary>
/// Renders Metasploit-framework-style usage / help text using boxed panels.
/// </summary>
public static class UsageFormatter
{
    public static void Print(CommandUsage usage)
    {
        AnsiConsole.WriteLine();

        // ── Module-info panel ──────────────────────────────
        var info = new Grid().AddColumn().AddColumn();
        info.AddRow($"[{UiColors.Label}]Module:[/]", $"[bold {UiColors.Accent}]washmachine-cli {usage.Name}[/]");
        info.AddRow($"[{UiColors.Label}]Info:[/]",   $"[{UiColors.Value}]{usage.Description}[/]");
        info.AddRow($"[{UiColors.Label}]Usage:[/]",  $"[{UiColors.Muted}]washmachine-cli[/] {usage.Syntax}");

        AnsiConsole.Write(MakePanel(usage.Name, info));

        // ── Required arguments ─────────────────────────────
        if (usage.Required is { Length: > 0 })
        {
            var table = MakeOptionTable(showDefault: false);
            foreach (var opt in usage.Required)
                table.AddRow(
                    $"[{UiColors.Accent}]{Markup.Escape(opt.Flag)}[/]",
                    $"[{UiColors.Value}]{opt.Description}[/]");
            AnsiConsole.Write(MakePanel("Required Arguments", table));
        }

        // ── Optional arguments ─────────────────────────────
        if (usage.Options is { Length: > 0 })
        {
            var table = MakeOptionTable(showDefault: true);
            foreach (var opt in usage.Options)
                table.AddRow(
                    $"[{UiColors.Accent}]{Markup.Escape(opt.Flag)}[/]",
                    $"[{UiColors.Value}]{opt.Description}[/]",
                    opt.Default != null
                        ? $"[{UiColors.Muted}]{Markup.Escape(opt.Default)}[/]"
                        : $"[{UiColors.Muted}]—[/]");
            AnsiConsole.Write(MakePanel("Options", table));
        }

        // ── Examples ───────────────────────────────────────
        if (usage.Examples is { Length: > 0 })
        {
            var rows = new List<IRenderable>();
            foreach (var ex in usage.Examples)
            {
                rows.Add(new Markup($"[{UiColors.Muted}]#[/] [{UiColors.Label}]{ex.Description}[/]"));
                rows.Add(new Markup($"[{UiColors.Accent}]$[/] [{UiColors.Value}]{Markup.Escape(ex.Command)}[/]"));
                rows.Add(new Text(""));
            }
            AnsiConsole.Write(MakePanel("Examples", new Rows(rows)));
        }

        // ── Notes ──────────────────────────────────────────
        if (usage.Notes is { Length: > 0 })
        {
            var rows = usage.Notes.Select(n =>
                (IRenderable)new Markup($"[{UiColors.Muted}]{n}[/]")).ToList();
            AnsiConsole.Write(MakePanel("Notes", new Rows(rows)));
        }
    }

    // ── Helpers ────────────────────────────────────────────

    public static Panel MakePanel(string title, IRenderable content) =>
        new Panel(content)
            .Header($"[bold {UiColors.Header}] {title} [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(UiColors.BoxBorderColor)
            .Padding(1, 0);

    private static Table MakeOptionTable(bool showDefault)
    {
        var t = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(UiColors.BoxBorderColor)
            .AddColumn(new TableColumn($"[{UiColors.Accent}]Name[/]"))
            .AddColumn(new TableColumn($"[{UiColors.Accent}]Description[/]"));

        if (showDefault)
            t.AddColumn(new TableColumn($"[{UiColors.Accent}]Default[/]"));

        return t;
    }
}
