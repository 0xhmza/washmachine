using Spectre.Console;

namespace Washmachine.Cli.Ui;

/// <summary>Use the terminal's native editing; do not repaint or truncate logical input.</summary>
internal static class TerminalInput
{
    public static string? ReadLine(string promptMarkup, TextReader? input = null,
        IAnsiConsole? console = null, bool? interactive = null)
    {
        input ??= Console.In;
        interactive ??= !Console.IsInputRedirected && !Console.IsOutputRedirected;
        if (interactive.Value)
        {
            console ??= UsageFormatter.DefaultConsole();
            string text = HelpWriter.Sanitize(promptMarkup);
            int width = Math.Clamp(console.Profile.Width, 1, 4096);
            if (width <= 3) text = width > 1 ? ">" : "";
            else if (Markup.Remove(text).GetCellWidth() >= width - 2) text = "> ";
            try { console.Write(new Markup(text)); }
            catch (InvalidOperationException) { console.Write(new Text(text)); }
        }
        try { return input.ReadLine(); }
        catch (IOException) { return null; }
    }
}
