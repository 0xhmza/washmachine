namespace Washmachine.Cli.Ui;

using Spectre.Console;

/// <summary>
/// Factory for consistently-styled Spectre.Console tables.
/// </summary>
public static class TableFactory
{
    public static Table Create(string title) =>
        new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey42)
            .Title($"[bold underline {UiColors.Accent}]{title}[/]");

    public static Table Create(string title, params string[] columns)
    {
        var table = Create(title);
        foreach (var col in columns)
            table.AddColumn(new TableColumn($"[bold {UiColors.Header}]{col}[/]"));
        return table;
    }
}
