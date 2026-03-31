namespace Washmachine.Cli.Ui;

using Spectre.Console;

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
/// Renders consistent usage/help text for all CLI commands.
/// </summary>
public static class UsageFormatter
{
    public static void Print(CommandUsage usage)
    {
        AnsiConsole.MarkupLine($"\n  [{UiColors.Accent}]washmachine-cli {usage.Name}[/] — {usage.Description}\n");

        AnsiConsole.MarkupLine($"  [{UiColors.Header}]Usage:[/]  washmachine-cli {usage.Syntax}\n");

        if (usage.Required is { Length: > 0 })
        {
            var reqTable = TableFactory.Create("Required", "Option", "Description");
            foreach (var opt in usage.Required)
                reqTable.AddRow($"[{UiColors.Accent}]{Markup.Escape(opt.Flag)}[/]", opt.Description);
            AnsiConsole.Write(reqTable);
            AnsiConsole.WriteLine();
        }

        if (usage.Options is { Length: > 0 })
        {
            var optTable = TableFactory.Create("Options", "Flag", "Description", "Default");
            foreach (var opt in usage.Options)
                optTable.AddRow(
                    $"[{UiColors.Accent}]{Markup.Escape(opt.Flag)}[/]",
                    opt.Description,
                    opt.Default ?? "—");
            AnsiConsole.Write(optTable);
            AnsiConsole.WriteLine();
        }

        if (usage.Examples is { Length: > 0 })
        {
            AnsiConsole.Write(new Rule($"[{UiColors.Header}]Examples[/]").RuleStyle(Style.Parse("grey42")));
            foreach (var ex in usage.Examples)
            {
                AnsiConsole.MarkupLine($"  [{UiColors.Muted}]# {ex.Description}[/]");
                AnsiConsole.MarkupLine($"  [{UiColors.Value}]{Markup.Escape(ex.Command)}[/]\n");
            }
        }

        if (usage.Notes is { Length: > 0 })
        {
            AnsiConsole.Write(new Rule($"[{UiColors.Header}]Notes[/]").RuleStyle(Style.Parse("grey42")));
            foreach (var note in usage.Notes)
                AnsiConsole.MarkupLine($"  [{UiColors.Muted}]{note}[/]");
            AnsiConsole.WriteLine();
        }
    }
}
