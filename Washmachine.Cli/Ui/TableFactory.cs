namespace Washmachine.Cli.Ui;

using Spectre.Console;

/// <summary>
/// Factory for consistently-styled Spectre.Console tables.
/// Uses the luxury palette from <see cref="UiColors"/>.
/// </summary>
public static class TableFactory
{
    public static Table Create(string title) =>
        new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(UiColors.BoxBorderColor)
            .Title($"[bold {UiColors.Header}]{title}[/]");

    public static Table Create(string title, params string[] columns)
    {
        var table = Create(title);
        foreach (var col in columns)
            table.AddColumn(new TableColumn($"[bold {UiColors.Accent}]{col}[/]"));
        return table;
    }
}
