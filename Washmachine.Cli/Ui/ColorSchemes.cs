namespace Washmachine.Cli.Ui;

/// <summary>
/// Semantic color tokens used across the CLI chrome.
/// All fields are hex triplets (e.g. <c>#ac4142</c>) so they plug straight
/// into Spectre.Console markup such as <c>[#ac4142]text[/]</c>.
/// </summary>
public sealed record ColorScheme(
    string Name,
    string Author,
    string Background,
    string Foreground,
    // Three-tone banner gradient (top → mid → bottom).
    string BannerPrimary,
    string BannerSecondary,
    string BannerTertiary,
    // Primary chrome.
    string Accent,
    string Header,
    string Value,
    string Muted,
    string Label,
    string Rule,
    string BoxBorder,
    // Semantic.
    string Success,
    string Warning,
    string Error,
    string Hex,
    string Link,
    string Tagline);

/// <summary>
/// Catalog of well-known 16-color terminal schemes re-keyed as
/// <see cref="ColorScheme"/> records. All schemes are dark-background.
/// </summary>
public static class ColorSchemes
{
    public const string DefaultSchemeName = "chris-kempson";

    public static readonly IReadOnlyDictionary<string, ColorScheme> All =
        new Dictionary<string, ColorScheme>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Chris Kempson (Base16 default dark) ──────────────────
            [DefaultSchemeName] = new ColorScheme(
                Name:            "Chris Kempson",
                Author:          "Default (Dark)",
                Background:      "#151515",
                Foreground:      "#d0d0d0",
                BannerPrimary:   "#f4bf75", // yellow
                BannerSecondary: "#aa759f", // magenta
                BannerTertiary:  "#6a9fb5", // blue
                Accent:          "#f4bf75",
                Header:          "#aa759f",
                Value:           "#d0d0d0",
                Muted:           "#808080",
                Label:           "#f5f5f5",
                Rule:            "#505050",
                BoxBorder:       "#505050",
                Success:         "#90a959",
                Warning:         "#f4bf75",
                Error:           "#ac4142",
                Hex:             "#aa759f",
                Link:            "#6a9fb5",
                Tagline:         "#75b5aa"),

            // ── Dracula ─────────────────────────────────────────────
            ["dracula"] = new ColorScheme(
                Name:            "Dracula",
                Author:          "Zeno Rocha",
                Background:      "#282a36",
                Foreground:      "#f8f8f2",
                BannerPrimary:   "#bd93f9",
                BannerSecondary: "#ff79c6",
                BannerTertiary:  "#8be9fd",
                Accent:          "#ff79c6",
                Header:          "#bd93f9",
                Value:           "#f8f8f2",
                Muted:           "#6272a4",
                Label:           "#f8f8f2",
                Rule:            "#44475a",
                BoxBorder:       "#44475a",
                Success:         "#50fa7b",
                Warning:         "#f1fa8c",
                Error:           "#ff5555",
                Hex:             "#bd93f9",
                Link:            "#8be9fd",
                Tagline:         "#f1fa8c"),

            // ── Solarized Dark ──────────────────────────────────────
            ["solarized-dark"] = new ColorScheme(
                Name:            "Solarized Dark",
                Author:          "Ethan Schoonover",
                Background:      "#002b36",
                Foreground:      "#839496",
                BannerPrimary:   "#b58900",
                BannerSecondary: "#cb4b16",
                BannerTertiary:  "#268bd2",
                Accent:          "#b58900",
                Header:          "#268bd2",
                Value:           "#93a1a1",
                Muted:           "#586e75",
                Label:           "#eee8d5",
                Rule:            "#073642",
                BoxBorder:       "#073642",
                Success:         "#859900",
                Warning:         "#b58900",
                Error:           "#dc322f",
                Hex:             "#6c71c4",
                Link:            "#2aa198",
                Tagline:         "#2aa198"),

            // ── Nord ────────────────────────────────────────────────
            ["nord"] = new ColorScheme(
                Name:            "Nord",
                Author:          "Arctic Ice Studio",
                Background:      "#2e3440",
                Foreground:      "#d8dee9",
                BannerPrimary:   "#88c0d0",
                BannerSecondary: "#81a1c1",
                BannerTertiary:  "#5e81ac",
                Accent:          "#88c0d0",
                Header:          "#b48ead",
                Value:           "#e5e9f0",
                Muted:           "#4c566a",
                Label:           "#eceff4",
                Rule:            "#3b4252",
                BoxBorder:       "#3b4252",
                Success:         "#a3be8c",
                Warning:         "#ebcb8b",
                Error:           "#bf616a",
                Hex:             "#b48ead",
                Link:            "#88c0d0",
                Tagline:         "#8fbcbb"),

            // ── Gruvbox Dark ────────────────────────────────────────
            ["gruvbox-dark"] = new ColorScheme(
                Name:            "Gruvbox Dark",
                Author:          "Pavel Pertsev",
                Background:      "#282828",
                Foreground:      "#ebdbb2",
                BannerPrimary:   "#fabd2f",
                BannerSecondary: "#fe8019",
                BannerTertiary:  "#d3869b",
                Accent:          "#fabd2f",
                Header:          "#d3869b",
                Value:           "#ebdbb2",
                Muted:           "#928374",
                Label:           "#fbf1c7",
                Rule:            "#504945",
                BoxBorder:       "#504945",
                Success:         "#b8bb26",
                Warning:         "#fabd2f",
                Error:           "#fb4934",
                Hex:             "#d3869b",
                Link:            "#83a598",
                Tagline:         "#8ec07c"),

            // ── Tokyo Night ─────────────────────────────────────────
            ["tokyo-night"] = new ColorScheme(
                Name:            "Tokyo Night",
                Author:          "Enkia",
                Background:      "#1a1b26",
                Foreground:      "#a9b1d6",
                BannerPrimary:   "#7aa2f7",
                BannerSecondary: "#bb9af7",
                BannerTertiary:  "#7dcfff",
                Accent:          "#7aa2f7",
                Header:          "#bb9af7",
                Value:           "#c0caf5",
                Muted:           "#565f89",
                Label:           "#c0caf5",
                Rule:            "#292e42",
                BoxBorder:       "#292e42",
                Success:         "#9ece6a",
                Warning:         "#e0af68",
                Error:           "#f7768e",
                Hex:             "#bb9af7",
                Link:            "#7dcfff",
                Tagline:         "#73daca"),

            // ── Monokai ─────────────────────────────────────────────
            ["monokai"] = new ColorScheme(
                Name:            "Monokai",
                Author:          "Wimer Hazenberg",
                Background:      "#272822",
                Foreground:      "#f8f8f2",
                BannerPrimary:   "#f92672",
                BannerSecondary: "#fd971f",
                BannerTertiary:  "#66d9ef",
                Accent:          "#a6e22e",
                Header:          "#f92672",
                Value:           "#f8f8f2",
                Muted:           "#75715e",
                Label:           "#f8f8f2",
                Rule:            "#49483e",
                BoxBorder:       "#49483e",
                Success:         "#a6e22e",
                Warning:         "#e6db74",
                Error:           "#f92672",
                Hex:             "#ae81ff",
                Link:            "#66d9ef",
                Tagline:         "#e6db74"),

            // ── One Dark ────────────────────────────────────────────
            ["one-dark"] = new ColorScheme(
                Name:            "One Dark",
                Author:          "Atom",
                Background:      "#282c34",
                Foreground:      "#abb2bf",
                BannerPrimary:   "#61afef",
                BannerSecondary: "#c678dd",
                BannerTertiary:  "#56b6c2",
                Accent:          "#61afef",
                Header:          "#c678dd",
                Value:           "#abb2bf",
                Muted:           "#5c6370",
                Label:           "#e5e5e5",
                Rule:            "#3e4451",
                BoxBorder:       "#3e4451",
                Success:         "#98c379",
                Warning:         "#e5c07b",
                Error:           "#e06c75",
                Hex:             "#c678dd",
                Link:            "#56b6c2",
                Tagline:         "#56b6c2"),

            // ── Catppuccin Mocha ────────────────────────────────────
            ["catppuccin-mocha"] = new ColorScheme(
                Name:            "Catppuccin Mocha",
                Author:          "Catppuccin",
                Background:      "#1e1e2e",
                Foreground:      "#cdd6f4",
                BannerPrimary:   "#f5c2e7",
                BannerSecondary: "#cba6f7",
                BannerTertiary:  "#89b4fa",
                Accent:          "#f5c2e7",
                Header:          "#cba6f7",
                Value:           "#cdd6f4",
                Muted:           "#6c7086",
                Label:           "#f5e0dc",
                Rule:            "#313244",
                BoxBorder:       "#313244",
                Success:         "#a6e3a1",
                Warning:         "#f9e2af",
                Error:           "#f38ba8",
                Hex:             "#cba6f7",
                Link:            "#89dceb",
                Tagline:         "#94e2d5"),
        };

    public static ColorScheme Get(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && All.TryGetValue(name.Trim(), out var scheme))
            return scheme;
        return All[DefaultSchemeName];
    }

    public static IReadOnlyList<string> Names => All.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
}
