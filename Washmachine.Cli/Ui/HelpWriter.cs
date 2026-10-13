using System.Globalization;
using System.Text;
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
        var style = new Style(foreground: UiColors.ParseHex(color));
        foreach (var line in Wrap(text, Width, indent))
        {
            // Never interpret user-provided brackets or terminal sequences.
            console.Write(new Text(line, style));
            console.WriteLine();
        }
    }

    public void Pair(string label, string value, int preferredWidth = 12)
    {
        label = Sanitize(label);
        value = Sanitize(value);
        int width = Width;
        int labelWidth = Math.Clamp(preferredWidth, 1, Math.Max(1, width / 3));
        if (width - labelWidth - 5 < 28 || label.Contains('\n') || label.GetCellWidth() > labelWidth)
        {
            Paragraph(label, UiColors.Label);
            Paragraph(value, UiColors.Value, 4);
            return;
        }
        string prefix = "  " + label.PadRight(label.Length + labelWidth - label.GetCellWidth()) + "   ";
        string continuation = new(' ', labelWidth + 5);
        bool first = true;
        foreach (string line in Wrap(value, width - labelWidth - 5, 0))
        {
            console.Write(new Text((first ? prefix : continuation) + line));
            console.WriteLine();
            first = false;
        }
    }

    internal static string Sanitize(string text)
    {
        var safe = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length;)
        {
            if (text[i] is '\x1b' or '\u009b' or '\u009d' or '\u0090' or '\u0098' or '\u009e' or '\u009f')
            {
                i = SkipEscape(text, i);
                continue;
            }
            if (text[i] == '\r')
            {
                safe.Append('\n');
                i += i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                continue;
            }
            if (!Rune.TryGetRuneAt(text, i, out var rune)) rune = Rune.ReplacementChar;
            i += rune.Utf16SequenceLength;
            if (rune.Value == '\n') safe.Append('\n');
            else if (rune.Value == '\t') safe.Append(' ');
            else if (Rune.IsControl(rune) || rune.Value is 0x061C or 0x200E or 0x200F
                or >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069) continue;
            else safe.Append(rune.ToString());
        }
        return safe.ToString();
    }

    // Discard complete terminal commands, including their parameters. Merely
    // removing ESC leaves titles, hyperlink targets and CSI parameters visible.
    private static int SkipEscape(string text, int start)
    {
        char kind = text[start];
        int i = start + 1;
        if (kind == '\x1b')
        {
            if (i == text.Length) return i;
            kind = text[i++];
        }
        if (kind is '[' or '\u009b')
        {
            while (i < text.Length)
                if (text[i++] is >= '\x40' and <= '\x7e') break;
        }
        else if (kind is ']' or 'P' or 'X' or '^' or '_' or '\u009d' or '\u0090' or '\u0098' or '\u009e' or '\u009f')
        {
            while (i < text.Length)
            {
                if (text[i++] is '\a' or '\u009c') break;
                if (text[i - 1] == '\x1b' && i < text.Length && text[i] == '\\') { i++; break; }
            }
        }
        else if (kind is >= '\x20' and <= '\x2f')
        {
            while (i < text.Length && text[i] is >= '\x20' and <= '\x2f') i++;
            if (i < text.Length) i++;
        }
        return i;
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
            for (int start = 0; start < paragraph.Length;)
            {
                int end = start + 1;
                bool whitespace = char.IsWhiteSpace(paragraph[start]);
                while (end < paragraph.Length && char.IsWhiteSpace(paragraph[end]) == whitespace) end++;
                string word = paragraph[start..end];
                start = end;
                if (whitespace)
                {
                    separator = word;
                    continue;
                }
                int wordWidth = word.GetCellWidth();
                int separatorWidth = separator.GetCellWidth();
                if (used > 0 && used + separatorWidth + wordWidth > capacity)
                {
                    yield return prefix + line;
                    line.Clear();
                    used = 0;
                }
                if (used > 0) { line.Append(separator); used += separatorWidth; }
                separator = "";
                if (used + wordWidth <= capacity)
                {
                    line.Append(word);
                    used += wordWidth;
                    continue;
                }
                var elements = StringInfo.GetTextElementEnumerator(word);
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
