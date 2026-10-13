namespace Washmachine.Cli.Ui;

using Spectre.Console;

/// <summary>
/// Compact, literal-text tables without decorative boxes or forced expansion.
/// </summary>
public static class TableFactory
{
    public static Table Create(string title)
    {
        var table = new Table().Border(TableBorder.None);
        if (!string.IsNullOrWhiteSpace(title))
            table.Title(Markup.Escape(HelpWriter.Sanitize(title)),
                new Style(UiColors.ParseHex(UiColors.Header), decoration: Decoration.Bold));
        return table;
    }

    public static Table Create(string title, params string[] columns)
    {
        var table = Create(title);
        foreach (var col in columns)
            table.AddColumn(new TableColumn(new Text(HelpWriter.Sanitize(col),
                new Style(UiColors.ParseHex(UiColors.Header), decoration: Decoration.Bold))));
        return table;
    }
}
