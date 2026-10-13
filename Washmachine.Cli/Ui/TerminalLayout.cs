using System.Text;
using System.Globalization;
using Spectre.Console;

namespace Washmachine.Cli.Ui;

/// <summary>Terminal geometry only; never executes or interprets input.</summary>
internal static class TerminalLayout
{
    public static int Width()
    {
        if (Console.IsOutputRedirected) return 80;
        try { return Console.WindowWidth > 0 ? Console.WindowWidth : 80; }
        catch (IOException) { return 80; }
        catch (InvalidOperationException) { return 80; }
    }

    internal readonly record struct InputView(string Text, int CursorColumn);

    public static InputView View(string input, int cursor, int columns)
    {
        if (columns <= 0) return new("", 0);
        cursor = Math.Clamp(cursor, 0, input.Length);
        var cells = new List<(string Text, int Width)>();
        int cursorCell = 0;
        var elements = StringInfo.GetTextElementEnumerator(input);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            int offset = elements.ElementIndex;
            string text = element.EnumerateRunes().Any(rune => Rune.IsControl(rune)
                || rune.Value is 0x061C or 0x200E or 0x200F or >= 0x202A and <= 0x202E
                or >= 0x2066 and <= 0x2069) ? "?" : element;
            int width = Math.Max(0, text.GetCellWidth());
            if (width > columns) { text = "?"; width = 1; }
            cells.Add((text, width));
            if (offset + element.Length <= cursor) cursorCell += width;
        }
        int first = 0;
        while (cursorCell >= columns && first < cells.Count)
            cursorCell -= cells[first++].Width;
        var visible = new StringBuilder();
        int used = 0;
        for (int i = first; i < cells.Count; i++)
        {
            if (used + cells[i].Width > columns) break;
            visible.Append(cells[i].Text);
            used += cells[i].Width;
        }
        return new(visible.ToString(), Math.Clamp(cursorCell, 0, columns - 1));
    }
}
