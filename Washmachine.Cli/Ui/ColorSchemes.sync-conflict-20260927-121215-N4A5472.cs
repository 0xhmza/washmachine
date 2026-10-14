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
    string BannerPrimary,
    string BannerSecondary,
    string BannerTertiary,
    string Accent,
    string Header,
    string Value,
    string Muted,
    string Label,
    string Rule,
    string BoxBorder,
    string Success,
    string Warning,
    string Error,
    string Hex,
    string Link,
    string Tagline);

/// <summary>
/// Catalog of built-in CLI color schemes. The default scheme is Dracula, with
/// several additional dark themes still available through the <c>scheme</c>
/// command for users who want a different terminal palette.
/// </summary>
public static class ColorSchemes
{
    public const string DefaultSchemeName = "dracula";

    public static readonly IReadOnlyDictionary<string, ColorScheme> All =
        new Dictionary<string, ColorScheme>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSchemeName] = new ColorScheme(
                Name:            "Dracula",
                Author:          "Dracula Theme",
                Background:      "#282A36",
                Foreground:      "#F8F8F2",
                BannerPrimary:   "#BD93F9",
                BannerSecondary: "#FF79C6",
                BannerTertiary:  "#8BE9FD",
                Accent:          "#8BE9FD",
                Header:          "#FF79C6",
                Value:           "#F8F8F2",
                Muted:           "#6272A4",
                Label:           "#F8F8F2",
                Rule:            "#44475A",
                BoxBorder:       "#6272A4",
                Success:         "#50FA7B",
                Warning:         "#FFB86C",
                Error:           "#FF5555",
                Hex:             "#F1FA8C",
                Link:            "#8BE9FD",
                Tagline:         "#BD93F9"),

            ["dark-modern"] = new ColorScheme(
                Name:            "Dark Modern",
                Author:          "Visual Studio Code",
                Background:      "#1e1e1e",
                Foreground:      "#d4d4d4",
                BannerPrimary:   "#007acc",
                BannerSecondary: "#dcdcaa",
                BannerTertiary:  "#4ec9b0",
                Accent:          "#007acc",
                Header:          "#dcdcaa",
                Value:           "#d4d4d4",
                Muted:           "#858585",
                Label:           "#f3f3f3",
                Rule:            "#2a2d2e",
                BoxBorder:       "#2a2d2e",
                Success:         "#b5cea8",
                Warning:         "#ce9178",
                Error:           "#f44747",
                Hex:             "#c586c0",
                Link:            "#9cdcfe",
                Tagline:         "#9cdcfe"),

            ["dark-plus"] = new ColorScheme(
                Name:            "Dark+ (default dark)",
                Author:          "Visual Studio Code",
                Background:      "#1e1e1e",
                Foreground:      "#d4d4d4",
                BannerPrimary:   "#569cd6",
                BannerSecondary: "#dcdcaa",
                BannerTertiary:  "#4ec9b0",
                Accent:          "#569cd6",
                Header:          "#dcdcaa",
                Value:           "#d4d4d4",
                Muted:           "#808080",
                Label:           "#f3f3f3",
                Rule:            "#3c3c3c",
                BoxBorder:       "#3c3c3c",
                Success:         "#b5cea8",
                Warning:         "#ce9178",
                Error:           "#f44747",
                Hex:             "#c586c0",
                Link:            "#9cdcfe",
                Tagline:         "#6a9955"),

            ["abyss"] = new ColorScheme(
                Name:            "Abyss",
                Author:          "Visual Studio Code",
                Background:      "#000c18",
                Foreground:      "#ffffff",
                BannerPrimary:   "#6dbae4",
                BannerSecondary: "#ffd600",
                BannerTertiary:  "#f28779",
                Accent:          "#6dbae4",
                Header:          "#ffd600",
                Value:           "#ffffff",
                Muted:           "#406375",
                Label:           "#ffffff",
                Rule:            "#002138",
                BoxBorder:       "#002138",
                Success:         "#6dbae4",
                Warning:         "#ffeb95",
                Error:           "#ec5f67",
                Hex:             "#cc99cc",
                Link:            "#6dbae4",
                Tagline:         "#7285b7"),

            ["kimbie-dark"] = new ColorScheme(
                Name:            "Kimbie Dark",
                Author:          "Visual Studio Code",
                Background:      "#221a0f",
                Foreground:      "#d3af86",
                BannerPrimary:   "#889b4a",
                BannerSecondary: "#f06431",
                BannerTertiary:  "#dc3958",
                Accent:          "#f79a32",
                Header:          "#889b4a",
                Value:           "#d3af86",
                Muted:           "#504945",
                Label:           "#f0d8b4",
                Rule:            "#3a2d1f",
                BoxBorder:       "#3a2d1f",
                Success:         "#889b4a",
                Warning:         "#f79a32",
                Error:           "#dc3958",
                Hex:             "#a89984",
                Link:            "#f06431",
                Tagline:         "#a89984"),

            ["monokai"] = new ColorScheme(
                Name:            "Monokai",
                Author:          "Visual Studio Code",
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

            ["red"] = new ColorScheme(
                Name:            "Red",
                Author:          "Visual Studio Code",
                Background:      "#390000",
                Foreground:      "#f5f5f5",
                BannerPrimary:   "#eb939a",
                BannerSecondary: "#ffd2a7",
                BannerTertiary:  "#de7c79",
                Accent:          "#eb939a",
                Header:          "#ffd2a7",
                Value:           "#f5f5f5",
                Muted:           "#b26b6b",
                Label:           "#ffffff",
                Rule:            "#5c1d1d",
                BoxBorder:       "#5c1d1d",
                Success:         "#ffc6c6",
                Warning:         "#e0b062",
                Error:           "#ff5a5a",
                Hex:             "#de7c79",
                Link:            "#ffd2a7",
                Tagline:         "#b26b6b"),

            ["solarized-dark"] = new ColorScheme(
                Name:            "Solarized Dark",
                Author:          "Visual Studio Code",
                Background:      "#002b36",
                Foreground:      "#839496",
                BannerPrimary:   "#b58900",
                BannerSecondary: "#2aa198",
                BannerTertiary:  "#268bd2",
                Accent:          "#859900",
                Header:          "#268bd2",
                Value:           "#93a1a1",
                Muted:           "#586e75",
                Label:           "#eee8d5",
                Rule:            "#073642",
                BoxBorder:       "#073642",
                Success:         "#859900",
                Warning:         "#b58900",
                Error:           "#dc322f",
                Hex:             "#d33682",
                Link:            "#2aa198",
                Tagline:         "#2aa198"),

            ["tomorrow-night-blue"] = new ColorScheme(
                Name:            "Tomorrow Night Blue",
                Author:          "Visual Studio Code",
                Background:      "#002451",
                Foreground:      "#ffffff",
                BannerPrimary:   "#6699cc",
                BannerSecondary: "#ff9da4",
                BannerTertiary:  "#66cccc",
                Accent:          "#6699cc",
                Header:          "#ff9da4",
                Value:           "#ffffff",
                Muted:           "#7285b7",
                Label:           "#ffffff",
                Rule:            "#00346e",
                BoxBorder:       "#00346e",
                Success:         "#d1f1a9",
                Warning:         "#ffcc66",
                Error:           "#f2777a",
                Hex:             "#cc99cc",
                Link:            "#66cccc",
                Tagline:         "#7285b7"),

            ["dark-high-contrast"] = new ColorScheme(
                Name:            "Dark High Contrast",
                Author:          "Visual Studio Code",
                Background:      "#000000",
                Foreground:      "#ffffff",
                BannerPrimary:   "#00ff00",
                BannerSecondary: "#00ffff",
                BannerTertiary:  "#ff00ff",
                Accent:          "#00ff00",
                Header:          "#00ffff",
                Value:           "#ffffff",
                Muted:           "#7f7f7f",
                Label:           "#ffffff",
                Rule:            "#ffffff",
                BoxBorder:       "#ffffff",
                Success:         "#00ff00",
                Warning:         "#ffff00",
                Error:           "#ff0000",
                Hex:             "#ff00ff",
                Link:            "#00ffff",
                Tagline:         "#ffff00"),
        };

    public static ColorScheme Get(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && All.TryGetValue(name.Trim(), out var scheme))
            return scheme;

        return All[DefaultSchemeName];
    }

    public static IReadOnlyList<string> Names =>
        All.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
}
