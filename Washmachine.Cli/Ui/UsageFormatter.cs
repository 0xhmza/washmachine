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

/// <summary>Shared help presentation; existing models and entry points remain compatible.</summary>
public static class UsageFormatter
{
    internal static IAnsiConsole PlainConsole(TextWriter output)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(output)
        });
        console.Profile.Width = ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected
            ? TerminalLayout.Width() : 80;
        return console;
    }

    public static int GetConsoleWidth() => TerminalLayout.Width();
    public static void PrintSectionHeader(string title) => new HelpWriter(AnsiConsole.Console).Heading(title);
    public static void Print(CommandUsage usage) => Print(usage, AnsiConsole.Console);

    /// <summary>Injectable output for tests and hosts, without global state changes.</summary>
    public static void Print(CommandUsage usage, IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(console);
        var writer = new HelpWriter(console);
        writer.Heading(usage.HeaderTitle ?? $"{usage.Name} command");
        writer.Paragraph(usage.Summary, UiColors.Value);
        writer.Heading("Usage");
        writer.Paragraph(usage.Syntax, UiColors.Accent);
        if (!string.IsNullOrWhiteSpace(usage.Description)) writer.Pair("Details", usage.Description);
        if (!string.IsNullOrWhiteSpace(usage.WhenToUse)) writer.Pair("Best for", usage.WhenToUse);
        if (!string.IsNullOrWhiteSpace(usage.Output)) writer.Pair("Output", usage.Output);

        foreach (var group in usage.OptionGroups ?? [])
        {
            writer.Heading(group.Title);
            if (!string.IsNullOrWhiteSpace(group.Description)) writer.Paragraph(group.Description, UiColors.Value);
            int labelWidth = group.Options.Select(o => HelpWriter.Sanitize(o.Flag).GetCellWidth()).DefaultIfEmpty(0).Max();
            foreach (var option in group.Options)
            {
                writer.Pair(option.Flag, option.Description, labelWidth);
                if (!string.IsNullOrWhiteSpace(option.AcceptedValues)) writer.Paragraph($"Values: {option.AcceptedValues}", UiColors.Muted, 4);
                if (!string.IsNullOrWhiteSpace(option.Default)) writer.Paragraph($"Default: {option.Default}", UiColors.Muted, 4);
            }
        }
        foreach (var section in usage.Sections ?? [])
        {
            writer.Heading(section.Title);
            if (!string.IsNullOrWhiteSpace(section.Description)) writer.Paragraph(section.Description, UiColors.Value);
            foreach (var note in section.Notes ?? []) writer.Pair(note.Label, note.Description);
            foreach (var bullet in section.Bullets ?? []) writer.Paragraph($"- {bullet}", UiColors.Value);
        }
        if (usage.Examples is { Length: > 0 })
        {
            writer.Heading("Examples");
            foreach (var example in usage.Examples)
            {
                writer.Paragraph(example.Description, UiColors.Label);
                if (example.Shell != "Any shell") writer.Paragraph($"({example.Shell})", UiColors.Muted);
                writer.Paragraph(example.Command, UiColors.Accent, 4);
                writer.Blank();
            }
        }
        if (usage.Related is { Length: > 0 })
        {
            writer.Heading("Related commands");
            foreach (var related in usage.Related) writer.Pair(related.Label, related.Description);
        }
        writer.Blank();
    }

    public static void PrintFields(string title, IEnumerable<UsageNote> fields) =>
        PrintFields(title, fields, AnsiConsole.Console);

    public static void PrintFields(string title, IEnumerable<UsageNote> fields, IAnsiConsole console)
    {
        var writer = new HelpWriter(console);
        writer.Heading(title);
        foreach (var field in fields) writer.Pair(field.Label, field.Description);
        writer.Blank();
    }
}
