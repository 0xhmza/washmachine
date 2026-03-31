namespace Washmachine.Cli.Ui;

using Spectre.Console;

/// <summary>
/// Luxury bright-on-black color palette.
/// All colors are high-brightness for dark terminal backgrounds.
/// </summary>
public static class UiColors
{
    // ── Primary ────────────────────────────────────────────
    public const string Accent    = "gold1";
    public const string Header    = "mediumpurple1";
    public const string Command   = "gold1";
    public const string Value     = "wheat1";

    // ── Semantic ───────────────────────────────────────────
    public const string Hex       = "plum1";
    public const string Success   = "springgreen2_1";
    public const string Warning   = "lightsalmon1";
    public const string Error     = "red1";

    // ── Neutrals ───────────────────────────────────────────
    public const string Muted     = "grey82";
    public const string Label     = "white";
    public const string Rule      = "grey58";

    // ── Banner / Chrome ────────────────────────────────────
    public const string Banner    = "mediumpurple1";
    public const string Tagline   = "grey82";
    public const string Link      = "deepskyblue1";

    // ── Box border ─────────────────────────────────────────
    public const string BoxBorder = "grey58";
    public static readonly Color BoxBorderColor = Color.Grey58;
    public static readonly Color BannerColor = Color.MediumPurple1;
}
