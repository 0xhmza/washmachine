namespace Washmachine.Cli.Ui;

using Spectre.Console;

/// <summary>Compact shared welcome text: no fixed-width art or random output.</summary>
public static class Banner
{
    public const string AppVersion = "1.0.0";
    public const string Tagline = "Shellcode Loader Builder & PE Backdoor Toolkit";
    public const string Author = "0xhmza";
    public const string AuthorUrl = "https://github.com/0xhmza";

    // Retain the public signature for all existing callers.
    public static void Render(int? templateCount = null, int? sectionCount = null,
        int? snippetCount = null, string? compilerStatus = null)
    {
        AnsiConsole.MarkupLine($"[bold {UiColors.Header}]Washmachine[/] [{UiColors.Muted}]v{AppVersion}[/]");
        AnsiConsole.MarkupLine($"[{UiColors.Muted}]Type help for commands; exit to close.[/]");
        AnsiConsole.WriteLine();
    }
}
