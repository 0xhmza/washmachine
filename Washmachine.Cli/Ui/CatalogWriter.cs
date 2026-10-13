using Spectre.Console;
using Spectre.Console.Rendering;

namespace Washmachine.Cli.Ui;

/// <summary>Literal, responsive presentation for option and choice lists.</summary>
internal static class CatalogWriter
{
    public static void Write(string title, IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string>> rows, string? footer = null, IAnsiConsole? console = null)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        if (columns.Count == 0) throw new ArgumentException("A catalog needs at least one column.", nameof(columns));
        console ??= UsageFormatter.DefaultConsole();
        var headings = columns.Select(text => HelpWriter.Sanitize(text).Replace('\n', ' ')).ToArray();
        var entries = rows.Select(row =>
        {
            if (row.Count != headings.Length) throw new ArgumentException("Row and column counts must match.", nameof(rows));
            return row.Select(HelpWriter.Sanitize).ToArray();
        }).ToArray();
        var writer = new HelpWriter(console);
        writer.Heading(title);
        int width = Math.Clamp(console.Profile.Width, 1, 4096);
        int minimumWidth = headings.Sum(text => Math.Max(12, text.GetCellWidth())) + headings.Length * 2;
        if (entries.Length == 0)
            writer.Paragraph("No entries found.", UiColors.Muted);
        else if (width - 1 >= minimumWidth)
        {
            var table = TableFactory.Create("", headings);
            table.Width = width - 1;
            foreach (var row in entries)
                table.AddRow(row.Select(text => (IRenderable)new Text(text)).ToArray());
            console.Write(table);
        }
        else
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (i > 0) writer.Blank();
                for (int column = 0; column < headings.Length; column++)
                    writer.Pair(headings[column], entries[i][column]);
            }
        }
        if (!string.IsNullOrWhiteSpace(footer))
        {
            writer.Blank();
            writer.Paragraph(footer, UiColors.Muted);
        }
    }
}
