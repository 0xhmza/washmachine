namespace Washmachine.Cli.Ui;

using System.Globalization;
using Spectre.Console;

/// <summary>
/// Semantic color tokens resolved against the currently-active
/// <see cref="ColorScheme"/>. All tokens return Spectre.Console-markup
/// hex strings (e.g. <c>#ac4142</c>) so callers stay unchanged.
/// </summary>
public static class UiColors
{
    public static ColorScheme ActiveScheme { get; private set; } =
        ColorSchemes.All[ColorSchemes.DefaultSchemeName];

    /// <summary>Switch the active scheme by name. Returns false on unknown name.</summary>
    public static bool TrySetScheme(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (!ColorSchemes.All.TryGetValue(name.Trim(), out var scheme))
            return false;
        ActiveScheme = scheme;
        return true;
    }

    public static void SetScheme(ColorScheme scheme) => ActiveScheme = scheme;

    // ── Primary ────────────────────────────────────────────
    public static string Accent  => ActiveScheme.Accent;
    public static string Header  => ActiveScheme.Header;
    public static string Command => ActiveScheme.Accent;
    public static string Value   => ActiveScheme.Value;

    // ── Semantic ───────────────────────────────────────────
    public static string Hex     => ActiveScheme.Hex;
    public static string Success => ActiveScheme.Success;
    public static string Warning => ActiveScheme.Warning;
    public static string Error   => ActiveScheme.Error;

    // ── Neutrals ───────────────────────────────────────────
    public static string Muted => ActiveScheme.Muted;
    public static string Label => ActiveScheme.Label;
    public static string Rule  => ActiveScheme.Rule;

    // ── Banner / Chrome ────────────────────────────────────
    public static string Banner  => ActiveScheme.BannerPrimary;
    public static string Tagline => ActiveScheme.Tagline;
    public static string Link    => ActiveScheme.Link;

    // ── Box border ─────────────────────────────────────────
    public static string BoxBorder        => ActiveScheme.BoxBorder;
    public static Color  BoxBorderColor   => ParseHex(ActiveScheme.BoxBorder);
    public static Color  BannerColor      => ParseHex(ActiveScheme.BannerPrimary);
    public static Color  BannerColorMid   => ParseHex(ActiveScheme.BannerSecondary);
    public static Color  BannerColorLow   => ParseHex(ActiveScheme.BannerTertiary);

    public static Color ParseHex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6)
            return Color.White;
        var r = byte.Parse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Color(r, g, b);
    }
}
