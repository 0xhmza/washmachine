using System.Globalization;
using System.Text;
using Spectre.Console;
using Washmachine.Cli.Ui;

namespace Washmachine.Cli;

public static partial class Program
{
    private sealed record SessionOptionSpec(
        string Name,
        bool Required,
        string? DefaultValue,
        string Description,
        string Details,
        string Expected,
        string Example,
        string? WhenToChange = null,
        string? DependsOn = null);

    private sealed record SessionOptionRow(
        int Id,
        string Name,
        string CurrentSetting,
        bool Required,
        string Description,
        string PossibleValues = "",
        string? RequiredOverrideLabel = null,
        string? RequiredOverrideColor = null,
        bool IsSubRow = false);

    private sealed record SessionValueChoice(string Name, string Description);

    private static readonly string[] SharedSessionCommands =
    [
        "show",
        "set",
        "unset",
        "get",
        "help",
        "reset",
        "run",
        "build",
        "exit",
        "quit",
    ];

    private static void RenderSessionOptionCatalog(string title, IReadOnlyList<SessionOptionRow> rows, string footer)
    {
        AnsiConsole.MarkupLine($"[bold {UiColors.Header}]{Markup.Escape(title)}[/]");
        AnsiConsole.MarkupLine($"[{UiColors.Muted}]{new string('=', title.Length)}[/]");
        AnsiConsole.WriteLine();

        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{UiColors.Muted}]No entries found.[/]");
            return;
        }

        // "Current Setting" is 15 chars; ensure the column is at least that wide
        var nonSubRows = rows.Where(row => !row.IsSubRow).ToArray();
        int idWidth = Math.Max(1, nonSubRows.Length > 0
            ? nonSubRows.Max(row => row.Id.ToString(CultureInfo.InvariantCulture).Length)
            : 1);
        int nameWidth = Math.Min(24, Math.Max(4, rows.Max(row => row.Name.Length)));
        int currentWidth = Math.Min(28, Math.Max(15, rows.Max(row => row.CurrentSetting.Length)));
        const int reqWidth = 9; // "Required?" = 9
        int possibleWidth = Math.Min(20, Math.Max(15, rows.Max(row => row.PossibleValues.Length)));

        WriteSessionCatalogLine(
            "#".PadLeft(idWidth),
            "Name".PadRight(nameWidth),
            "Current Setting".PadRight(currentWidth),
            "Required?".PadRight(reqWidth),
            "Possible Values".PadRight(possibleWidth),
            "Description",
            UiColors.Accent, UiColors.Accent);

        WriteSessionCatalogLine(
            new string('-', idWidth),
            new string('-', nameWidth),
            new string('-', currentWidth),
            new string('-', reqWidth),
            new string('-', possibleWidth),
            new string('-', "Description".Length),
            UiColors.Muted, UiColors.Muted);

        foreach (var row in rows)
        {
            string reqLabel = row.RequiredOverrideLabel ?? (row.Required ? "Yes" : "No");
            string reqColor = row.RequiredOverrideColor ?? (row.Required ? UiColors.Error : UiColors.Muted);
            string rowColor = row.IsSubRow ? UiColors.Muted : UiColors.Value;
            string idCell = row.IsSubRow
                ? new string(' ', idWidth)
                : row.Id.ToString(CultureInfo.InvariantCulture).PadLeft(idWidth);

            WriteSessionCatalogLine(
                idCell,
                TruncateWithEllipsis(row.Name, nameWidth, "...").PadRight(nameWidth),
                TruncateWithEllipsis(row.CurrentSetting, currentWidth, "...").PadRight(currentWidth),
                reqLabel.PadRight(reqWidth),
                TruncateWithEllipsis(row.PossibleValues, possibleWidth, "...").PadRight(possibleWidth),
                row.Description,
                rowColor, reqColor);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{UiColors.Muted}]{Markup.Escape(footer)}[/]");
    }

    private static void RenderSessionChoiceCatalog(string title, IReadOnlyList<SessionValueChoice> choices, string? footer = null, string? nameColor = null)
    {
        AnsiConsole.MarkupLine($"[bold {UiColors.Header}]{Markup.Escape(title)}[/]");
        AnsiConsole.MarkupLine($"[{UiColors.Muted}]{new string('=', title.Length)}[/]");
        AnsiConsole.WriteLine();

        if (choices.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{UiColors.Muted}]No choices available.[/]");
        }
        else
        {
            int idWidth = Math.Max(1, (choices.Count - 1).ToString(CultureInfo.InvariantCulture).Length);
            int nameWidth = Math.Min(32, Math.Max(4, choices.Max(choice => choice.Name.Length)));

            WriteChoiceCatalogLine("#".PadLeft(idWidth), "Name".PadRight(nameWidth), "Description", UiColors.Accent);
            WriteChoiceCatalogLine(new string('-', idWidth), new string('-', nameWidth), new string('-', "Description".Length), UiColors.Muted);

            for (int i = 0; i < choices.Count; i++)
            {
                WriteChoiceCatalogLine(
                    i.ToString(CultureInfo.InvariantCulture).PadLeft(idWidth),
                    choices[i].Name.PadRight(nameWidth),
                    choices[i].Description,
                    nameColor ?? UiColors.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(footer))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[{UiColors.Muted}]{Markup.Escape(footer)}[/]");
        }
    }

    private static void WriteSessionCatalogLine(string id, string name, string current, string required, string possibleValues, string description, string rowColor, string requiredColor)
    {
        var prefix = new StringBuilder()
            .Append(id)
            .Append("   ")
            .Append(name)
            .Append("   ")
            .Append(current)
            .Append("   ")
            .ToString();

        AnsiConsole.Markup($"[{rowColor}]{Markup.Escape(prefix)}[/]");
        AnsiConsole.Markup($"[{requiredColor}]{Markup.Escape(required)}[/]");
        AnsiConsole.Markup($"[{rowColor}]   {Markup.Escape(possibleValues)}[/]");
        AnsiConsole.MarkupLine($"[{rowColor}]   {Markup.Escape(description)}[/]");
    }

    private static void WriteChoiceCatalogLine(string id, string name, string description, string color)
    {
        var line = new StringBuilder()
            .Append(id)
            .Append("   ")
            .Append(name)
            .Append("   ")
            .Append(description)
            .ToString();

        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(line)}[/]");
    }

    private static bool TryResolveSessionOption(
        string input,
        IReadOnlyList<SessionOptionSpec> specs,
        out SessionOptionSpec? spec,
        out string? error)
    {
        spec = null;
        error = null;

        string normalized = NormalizeSessionOptionName(input);
        spec = specs.FirstOrDefault(candidate => NormalizeSessionOptionName(candidate.Name) == normalized);
        if (spec is not null)
            return true;

        var matches = specs
            .Where(candidate =>
            {
                string candidateKey = NormalizeSessionOptionName(candidate.Name);
                return candidateKey.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)
                    || candidateKey.Contains(normalized, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        if (matches.Length == 1)
        {
            spec = matches[0];
            return true;
        }

        if (matches.Length > 1)
        {
            error = $"Ambiguous option '{input}'. Matches: {string.Join(", ", matches.Select(match => match.Name))}";
            return false;
        }

        // No partial match — try fuzzy similarity.
        var suggestions = SuggestSimilarNames(normalized, specs.Select(s => s.Name), 3);
        if (suggestions.Count > 0)
            error = $"Unknown option '{input}'. Did you mean: {string.Join(", ", suggestions)}? Type 'show options' for the full list.";
        else
            error = $"Unknown option '{input}'. Type 'show options' to list available options.";
        return false;
    }

    /// <summary>
    /// Build a "Unknown command — did you mean X" message for session shells.
    /// Uses the shared session-command vocabulary (set, unset, show, build, etc.).
    /// </summary>
    private static string SuggestSessionCommand(string typed)
    {
        var suggestions = SuggestSimilarNames(typed, SharedSessionCommands, 3);
        if (suggestions.Count > 0)
            return $"Unknown command: {typed}. Did you mean: {string.Join(", ", suggestions)}? Type 'help' to list commands.";
        return $"Unknown command: {typed}. Type 'help' for available commands.";
    }

    /// <summary>
    /// Render the long-form help for one option (title, required/default/current,
    /// details, expected, example, when to change, depends on) using consistent
    /// status-prefixed lines.
    /// </summary>
    private static void PrintOptionHelpDetail(SessionOptionSpec spec, string currentValue)
    {
        var fields = new List<UsageNote>
        {
            new("Summary", spec.Description),
            new("Required", spec.Required ? "yes" : "no"),
            new("Default", string.IsNullOrWhiteSpace(spec.DefaultValue) ? "(none)" : spec.DefaultValue),
            new("Current", currentValue),
            new("Details", spec.Details),
            new("Expected", spec.Expected),
            new("Example", $"set {spec.Name} {spec.Example}")
        };
        if (!string.IsNullOrWhiteSpace(spec.WhenToChange)) fields.Add(new("When to change", spec.WhenToChange));
        if (!string.IsNullOrWhiteSpace(spec.DependsOn)) fields.Add(new("Depends on", spec.DependsOn));
        UsageFormatter.PrintFields(spec.Name, fields);
    }

    private static string NormalizeSessionOptionName(string value)
    {
        return new string(value
            .Where(ch => ch != '_' && ch != '-' && !char.IsWhiteSpace(ch))
            .ToArray())
            .ToUpperInvariant();
    }

    private static IEnumerable<string> GetSessionShowCompletionMatches(string[] argTokens, int currentArgIndex, string currentPrefix)
    {
        if (currentArgIndex == 1)
            return FilterCompletionMatches(new[] { "options" }.Concat(ShowTargets).Concat(ShowLegacyTargets), currentPrefix);

        if (currentArgIndex == 2 && argTokens.Length > 1 && NormalizeShowTarget(argTokens[1]) == "modules")
            return FilterCompletionMatches(GetShowModuleCategoryCandidates(), currentPrefix);

        return Array.Empty<string>();
    }

    private static void PrintOptionRequiredInfo(SessionOptionSpec spec)
    {
        if (spec.Required)
            WriteStatus(StatusPrefix.Warning, $"{spec.Name} is required.");
        else
        {
            string defVal = spec.DefaultValue is null ? "not set" : spec.DefaultValue;
            WriteStatus(StatusPrefix.Info, $"{spec.Name} is optional (default: {defVal}).");
        }
    }

    private static IReadOnlyList<SessionValueChoice> BuildBooleanChoices(string trueDescription, string falseDescription) =>
    [
        new SessionValueChoice("true", trueDescription),
        new SessionValueChoice("false", falseDescription),
    ];

    private static IReadOnlyList<SessionValueChoice> BuildTriStateChoices(string autoDescription, string trueDescription, string falseDescription) =>
    [
        new SessionValueChoice("auto", autoDescription),
        new SessionValueChoice("true", trueDescription),
        new SessionValueChoice("false", falseDescription),
    ];
}
