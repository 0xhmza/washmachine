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
