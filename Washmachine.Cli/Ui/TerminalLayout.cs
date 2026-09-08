using System.Text;
using Spectre.Console;

namespace Washmachine.Cli.Ui;

/// <summary>Terminal geometry only; never executes or interprets input.</summary>
internal static class TerminalLayout
{
    public static int Width()
    {
        try { return Console.WindowWidth > 0 ? Console.WindowWidth : 80; }
        catch (IOException) { return 80; }
        catch (InvalidOperationException) { return 80; }
    }

    internal readonly record struct InputView(string Text, int CursorColumn);

    public static InputView View(string input, int cursor, int columns)
    {
        if (columns <= 0) return new("", 0);
        cursor = Math.Clamp(cursor, 0, input.Length);
        var cells = new List<(string Text, int Start, int Width)>();
        int offset = 0;
        foreach (var rune in input.EnumerateRunes())
        {
            string text = Rune.IsControl(rune) ? "?" : rune.ToString();
            int width = Math.Max(0, text.GetCellWidth());
            cells.Add((text, offset, width));
            offset += rune.Utf16SequenceLength;
        }
        int cursorCell = cells.TakeWhile(c => c.Start < cursor).Sum(c => c.Width);
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
