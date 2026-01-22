namespace Washmachine.Models;

// Shared helpers for loose snippet-key matching across templates and headers.
internal static class SnippetKeyNormalizer
{
    public static string Normalize(string? value)
        => new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    public static string TrimPlural(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.EndsWith("es", StringComparison.OrdinalIgnoreCase))
            return value[..^2];
        if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return value[..^1];
        return value;
    }

    public static bool Equalish(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(a, TrimPlural(b), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(TrimPlural(a), b, StringComparison.OrdinalIgnoreCase);
}
