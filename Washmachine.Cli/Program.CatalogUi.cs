using System.Globalization;
using Spectre.Console;
using Washmachine.Cli.Ui;

namespace Washmachine.Cli;

// Presentation adapters only. Keep row metadata and stable IDs supplied by the
// callers; this file does not resolve options, read input, or execute commands.
public static partial class Program
{
    internal sealed record SessionOptionRow(
        int Id,
        string Name,
        string CurrentSetting,
        bool Required,
        string Description,
        string PossibleValues = "",
        string? RequiredOverrideLabel = null,
        string? RequiredOverrideColor = null,
        bool IsSubRow = false);

    internal sealed record SessionValueChoice(string Name, string Description);

    internal static void RenderSessionOptionCatalog(string title, IReadOnlyList<SessionOptionRow> rows,
        string footer, IAnsiConsole? console = null) =>
        CatalogWriter.Write(title, ["#", "Name", "Current setting", "Required?", "Possible values", "Description"],
            rows.Select(row => (IReadOnlyList<string>)new[]
            {
                row.IsSubRow ? "" : row.Id.ToString(CultureInfo.InvariantCulture),
                row.Name, row.CurrentSetting, row.RequiredOverrideLabel ?? (row.Required ? "Yes" : "No"),
                row.PossibleValues, row.Description
            }), footer, console);

    // nameColor remains accepted for existing callers. Lists now share one
    // semantic palette instead of applying different colors per tool.
    internal static void RenderSessionChoiceCatalog(string title, IReadOnlyList<SessionValueChoice> choices,
        string? footer = null, string? nameColor = null, IAnsiConsole? console = null) =>
        CatalogWriter.Write(title, ["#", "Name", "Description"],
            choices.Select((choice, id) => (IReadOnlyList<string>)new[]
            {
                id.ToString(CultureInfo.InvariantCulture), choice.Name, choice.Description
            }), footer, console);
}
