using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace Washmachine.Cli.Ui;

/// <summary>One forward-only, literal-text renderer for all help surfaces.</summary>
internal sealed class HelpWriter(IAnsiConsole console)
{
    private int Width => Math.Clamp(console.Profile.Width, 1, 4096);
    public void Blank() => console.WriteLine();

    public void Heading(string title)
    {
        Blank();
        Paragraph(title.ToUpperInvariant(), UiColors.Header, 0);
    }

    public void Paragraph(string text, string color, int indent = 2)
    {
        foreach (var line in Wrap(text, Width, indent))
        {
            // Never interpret user-provided brackets or terminal sequences.
            console.Write(new Text(line, new Style(foreground: UiColors.ParseHex(color))));
            console.WriteLine();
        }
    }

    public void Pair(string label, string value, int preferredWidth = 12)
    {
        label = Sanitize(label);
        value = Sanitize(value);
        int labelWidth = Math.Min(preferredWidth, Math.Max(1, Width / 3));
        if (Width - labelWidth - 5 < 28 || label.Contains('\n') || label.GetCellWidth() > labelWidth)
        {
            Paragraph(label, UiColors.Label);
            Paragraph(value, UiColors.Value, 4);
            return;
        }
        string prefix = "  " + label.PadRight(label.Length + labelWidth - label.GetCellWidth()) + "   ";
        var lines = Wrap(value, Width - labelWidth - 5, 0).ToArray();
        for (int i = 0; i < lines.Length; i++)
        {
            string left = i == 0 ? prefix : new string(' ', labelWidth + 5);
            console.Write(new Text(left + lines[i]));
            console.WriteLine();
        }
    }

    internal static string Sanitize(string text)
    {
        var safe = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\n') safe.Append('\n');
            else if (rune.Value == '\t') safe.Append(' ');
            else if (Rune.IsControl(rune) || rune.Value is 0x061C or 0x200E or 0x200F
                or >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069) continue;
            else safe.Append(rune.ToString());
        }
        return safe.ToString();
    }

    internal static IEnumerable<string> Wrap(string text, int width, int indent)
    {
        int available = Math.Max(1, Math.Clamp(width, 1, 4096) - 1);
        indent = Math.Clamp(indent, 0, Math.Max(0, available - 8));
        int capacity = available - indent;
        string prefix = new(' ', indent);
        foreach (string paragraph in Sanitize(text).Split('\n'))
        {
            var line = new StringBuilder();
            int used = 0;
            string separator = "";
            foreach (Match word in Regex.Matches(paragraph, @"\s+|\S+"))
            {
                if (string.IsNullOrWhiteSpace(word.Value))
                {
                    separator = word.Value;
                    continue;
                }
                int wordWidth = word.Value.GetCellWidth();
                if (used > 0 && used + separator.Length + wordWidth > capacity)
                {
                    yield return prefix + line;
                    line.Clear();
                    used = 0;
                }
                if (used > 0) { line.Append(separator); used += separator.Length; }
                separator = "";
                var elements = StringInfo.GetTextElementEnumerator(word.Value);
                while (elements.MoveNext())
                {
                    string element = elements.GetTextElement();
                    int cells = Math.Max(0, element.GetCellWidth());
                    if (cells > capacity) { element = "?"; cells = 1; }
                    if (used + cells > capacity)
                    {
                        yield return prefix + line;
                        line.Clear();
                        used = 0;
                    }
                    line.Append(element);
                    used += cells;
                }
            }
            yield return prefix + line;
        }
    }
}
