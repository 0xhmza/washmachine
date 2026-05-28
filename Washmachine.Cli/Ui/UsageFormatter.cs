namespace Washmachine.Cli.Ui;

using Spectre.Console;

public record UsageOption(
    string Flag,
    string Description,
    string? Default = null,
    string? AcceptedValues = null);

public record UsageOptionGroup(
    string Title,
    string? Description,
    UsageOption[] Options);

public record UsageExample(
    string Command,
    string Description,
    string Shell = "Any shell");

public record UsageNote(
    string Label,
    string Description);

public record UsageSection(
    string Title,
    string? Description = null,
    UsageNote[]? Notes = null,
    string[]? Bullets = null);

public record CommandUsage(
    string Name,
    string Summary,
    string Syntax,
    string? Description = null,
    string? WhenToUse = null,
    string? Output = null,
    UsageOptionGroup[]? OptionGroups = null,
    UsageSection[]? Sections = null,
    UsageExample[]? Examples = null,
    UsageNote[]? Related = null,
    string? HeaderTitle = null);

/// <summary>
/// Flat, box-free help renderer. Sections are separated by bold headers and
/// a thin rule — no panels, no borders.
/// </summary>
public static class UsageFormatter
{
    public static int GetConsoleWidth()
    {
        try
        {
            int width = Console.WindowWidth;
            return width > 40 ? width : 100;
        }
        catch
        {
            return 100;
        }
    }

    /// <summary>Print a section header with an underline rule.</summary>
    public static void PrintSectionHeader(string title)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold {UiColors.Header}]{Markup.Escape(title.ToUpperInvariant())}[/]");
        AnsiConsole.MarkupLine($"[{UiColors.Rule}]{new string('─', title.Length + 2)}[/]");
        AnsiConsole.WriteLine();
    }

    public static void Print(CommandUsage usage)
    {
        PrintSectionHeader(usage.HeaderTitle ?? $"{usage.Name} command");

        // Overview fields
        PrintKv("Summary", usage.Summary, UiColors.Value);
        PrintKv("Usage",   usage.Syntax,  UiColors.Accent);
        if (!string.IsNullOrWhiteSpace(usage.Description))
            PrintKv("Details",  usage.Description!, UiColors.Value);
        if (!string.IsNullOrWhiteSpace(usage.WhenToUse))
            PrintKv("Best for", usage.WhenToUse!,   UiColors.Value);
        if (!string.IsNullOrWhiteSpace(usage.Output))
            PrintKv("Output",   usage.Output!,       UiColors.Value);

        // Option groups
        foreach (var group in usage.OptionGroups ?? Array.Empty<UsageOptionGroup>())
        {
            PrintSectionHeader(group.Title);

            if (!string.IsNullOrWhiteSpace(group.Description))
            {
                AnsiConsole.MarkupLine($"  [{UiColors.Value}]{Markup.Escape(group.Description)}[/]");
                AnsiConsole.WriteLine();
            }

            if (group.Options.Length > 0)
            {
                int flagWidth = group.Options.Max(o => o.Flag.Length);
                foreach (var opt in group.Options)
                {
                    string flag = opt.Flag.PadRight(flagWidth);
                    AnsiConsole.MarkupLine($"  [{UiColors.Accent}]{Markup.Escape(flag)}[/]   [{UiColors.Value}]{Markup.Escape(opt.Description)}[/]");

                    var extras = new List<string>();
                    if (!string.IsNullOrWhiteSpace(opt.AcceptedValues))
                        extras.Add($"Values: {opt.AcceptedValues}");
                    if (!string.IsNullOrWhiteSpace(opt.Default))
                        extras.Add($"Default: {opt.Default}");
                    if (extras.Count > 0)
                        AnsiConsole.MarkupLine($"  {new string(' ', flagWidth)}   [{UiColors.Muted}]{Markup.Escape(string.Join("  ·  ", extras))}[/]");
                }
            }
        }

        // Generic sections
        foreach (var section in usage.Sections ?? Array.Empty<UsageSection>())
        {
            PrintSectionHeader(section.Title);

            if (!string.IsNullOrWhiteSpace(section.Description))
            {
                AnsiConsole.MarkupLine($"  [{UiColors.Value}]{Markup.Escape(section.Description)}[/]");
                AnsiConsole.WriteLine();
            }

            if (section.Notes is { Length: > 0 })
            {
                int labelWidth = section.Notes.Max(n => n.Label.Length);
                foreach (var note in section.Notes)
                {
                    string label = note.Label.PadRight(labelWidth);
                    AnsiConsole.MarkupLine($"  [{UiColors.Label}]{Markup.Escape(label)}[/]   [{UiColors.Value}]{Markup.Escape(note.Description)}[/]");
                }
            }

            if (section.Bullets is { Length: > 0 })
            {
                foreach (var bullet in section.Bullets)
                    AnsiConsole.MarkupLine($"  [{UiColors.Value}]· {Markup.Escape(bullet)}[/]");
            }
        }

        // Examples
        if (usage.Examples is { Length: > 0 })
        {
            PrintSectionHeader("Examples");
            bool first = true;
            foreach (var ex in usage.Examples)
            {
                if (!first) AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"  [{UiColors.Label}]{Markup.Escape(ex.Description)}[/]");
                if (ex.Shell != "Any shell")
                    AnsiConsole.MarkupLine($"  [{UiColors.Muted}]({Markup.Escape(ex.Shell)})[/]");
                AnsiConsole.MarkupLine($"  [{UiColors.Accent}]>[/] [{UiColors.Value}]{Markup.Escape(ex.Command)}[/]");
                first = false;
            }
        }

        // Related commands
        if (usage.Related is { Length: > 0 })
        {
            PrintSectionHeader("Related commands");
            int labelWidth = usage.Related.Max(r => r.Label.Length);
            foreach (var rel in usage.Related)
            {
                string label = rel.Label.PadRight(labelWidth);
                AnsiConsole.MarkupLine($"  [{UiColors.Accent}]{Markup.Escape(label)}[/]   [{UiColors.Value}]{Markup.Escape(rel.Description)}[/]");
            }
        }

        AnsiConsole.WriteLine();
    }

    private static void PrintKv(string label, string value, string valueColor)
    {
        const int labelWidth = 8; // "Best for" = 8
        string paddedLabel = label.PadRight(labelWidth);
        AnsiConsole.MarkupLine($"  [{UiColors.Label}]{Markup.Escape(paddedLabel)}[/]   [{valueColor}]{Markup.Escape(value)}[/]");
    }
}
