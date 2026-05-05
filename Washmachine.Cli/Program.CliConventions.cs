namespace Washmachine.Cli;

/// <summary>
/// Centralized CLI conventions: help-flag tokens accepted at every layer,
/// a similarity helper for "did you mean?" suggestions, and a normalized
/// view of <c>set OPTION VALUE</c> vs <c>set OPTION=VALUE</c> input.
///
/// These helpers exist so help discoverability and typo suggestions stay
/// uniform across the top-level dispatcher, the REPL, sub-mode shells,
/// and Metasploit-style sessions.
/// </summary>
public static partial class Program
{
    /// <summary>
    /// Tokens that should always trigger help, regardless of which command
    /// or session they appear in. Comparison is case-insensitive.
    /// </summary>
    private static readonly string[] HelpTokens =
    {
        "help", "--help", "-h", "-?", "/?", "?",
        "-Help", "-help", "--Help",
    };

    /// <summary>True if the token is any recognized help flag/keyword.</summary>
    private static bool IsHelpToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        foreach (var t in HelpTokens)
        {
            if (string.Equals(t, token, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>True if the args list begins with a help token.</summary>
    private static bool ArgsStartWithHelp(string[] args) =>
        args.Length > 0 && IsHelpToken(args[0]);

    /// <summary>Tokens accepted as a version request anywhere top-level.</summary>
    private static readonly string[] VersionTokens =
    {
        "--version", "-v", "-V", "version", "-Version",
    };

    /// <summary>True if the token is a version-request flag.</summary>
    private static bool IsVersionToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        foreach (var t in VersionTokens)
        {
            if (string.Equals(t, token, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Suggest the closest matches for an unknown name out of a candidate list.
    /// Returns up to <paramref name="max"/> entries, ordered by edit distance,
    /// only when the closest one is "near enough" to be useful.
    /// </summary>
    private static IReadOnlyList<string> SuggestSimilarNames(
        string input,
        IEnumerable<string> candidates,
        int max = 3)
    {
        if (string.IsNullOrWhiteSpace(input)) return Array.Empty<string>();

        var ranked = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => new
            {
                Name = c,
                Distance = LevenshteinDistance(c.ToLowerInvariant(), input.ToLowerInvariant()),
                SharedPrefix = SharedPrefixLength(c, input)
            })
            .Where(item =>
                item.Distance <= 3
                || item.SharedPrefix >= 2
                || item.Name.Contains(input, StringComparison.OrdinalIgnoreCase)
                || input.Contains(item.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Distance)
            .ThenByDescending(item => item.SharedPrefix)
            .Select(item => item.Name)
            .Take(max)
            .ToArray();

        return ranked;
    }

    private static int SharedPrefixLength(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < n && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i]))
            i++;
        return i;
    }

    /// <summary>
    /// Normalize a <c>set</c> command's tokens so both <c>set X Y</c> and
    /// <c>set X=Y</c> are accepted. Returns the rewritten token array; if
    /// the input did not need rewriting, returns the original.
    /// Sets <paramref name="usedEqualsSyntax"/> when the user typed <c>X=Y</c>.
    /// </summary>
    private static string[] NormalizeSetTokens(string[] tokens, out bool usedEqualsSyntax)
    {
        usedEqualsSyntax = false;
        if (tokens.Length < 2) return tokens;

        // Case 1: set X=Y  → tokens = ["set", "X=Y"]
        if (tokens.Length == 2)
        {
            int eq = tokens[1].IndexOf('=');
            if (eq > 0 && eq < tokens[1].Length - 1)
            {
                usedEqualsSyntax = true;
                var result = new string[3];
                result[0] = tokens[0];
                result[1] = tokens[1][..eq];
                result[2] = tokens[1][(eq + 1)..];
                return result;
            }
        }

        // Case 2: set X =Y  or  set X= Y  → join the equals piece
        if (tokens.Length >= 3 && tokens[2].StartsWith("="))
        {
            usedEqualsSyntax = true;
            var rest = tokens[2].Length > 1
                ? new[] { tokens[2][1..] }.Concat(tokens.Skip(3))
                : tokens.Skip(3);
            return new[] { tokens[0], tokens[1] }.Concat(rest).ToArray();
        }

        // Case 3: token-1 ends with '=' so split it: ["set", "X=", "Y"]
        if (tokens.Length >= 3 && tokens[1].EndsWith("="))
        {
            usedEqualsSyntax = true;
            return new[] { tokens[0], tokens[1].TrimEnd('=') }.Concat(tokens.Skip(2)).ToArray();
        }

        return tokens;
    }
}
