using Spectre.Console;
using Spectre.Console.Rendering;

namespace Washmachine.Cli.Ui;

internal enum StatusKind { Info, Success, Warning, Error, Prompt, Pending, Working }

/// <summary>Shared presentation adapter; delegates interaction and work to existing callers.</summary>
internal static class CliConsole
{
    public static Profile Profile => AnsiConsole.Profile;
    public static void WriteLine() => UsageFormatter.DefaultConsole().WriteLine();
    public static void WriteLine(string text) => new HelpWriter(UsageFormatter.DefaultConsole()).Paragraph(text, UiColors.Value, 0);
    public static void Markup(string text) => UsageFormatter.DefaultConsole().Write(Parse(text));
    public static void MarkupLine(string text)
    {
        var console = UsageFormatter.DefaultConsole();
        console.Write(Parse(text));
        console.WriteLine();
    }

    public static void Write(IRenderable value, IAnsiConsole? console = null)
    {
        console ??= UsageFormatter.DefaultConsole();
        if (value is Table table)
        {
            string title = table.Title == null ? "" : Plain(Parse(table.Title.Text));
            string? caption = table.Caption == null ? null : Plain(Parse(table.Caption.Text));
            if (table.Columns.Count == 0) { new HelpWriter(console).Paragraph("No entries found.", UiColors.Muted); return; }
            var rows = table.Rows.Select(row => (IReadOnlyList<string>)row.Select(Plain).ToArray()).ToList();
            if (table.ShowFooters && table.Columns.Any(column => column.Footer != null))
                rows.Add(table.Columns.Select(column => column.Footer == null ? "" : Plain(column.Footer)).ToArray());
            if (!table.ShowHeaders && table.Columns.Count == 2)
            {
                UsageFormatter.PrintFields(title, rows.Select(row => new UsageNote(row[0], row[1])), console);
                if (!string.IsNullOrWhiteSpace(caption)) new HelpWriter(console).Paragraph(caption, UiColors.Muted);
                return;
            }
            CatalogWriter.Write(title, table.Columns.Select(column => Plain(column.Header)).ToArray(), rows, caption, console);
            return;
        }
        if (value is Panel panel)
        {
            var writer = new HelpWriter(console);
            if (panel.Header != null) writer.Heading(Plain(Parse(panel.Header.Text)));
            var header = panel.Header; var border = panel.Border; var padding = panel.Padding;
            bool expand = panel.Expand; int? width = panel.Width, height = panel.Height;
            string body;
            try
            {
                panel.Header = null; panel.Border = BoxBorder.None; panel.Padding = new Padding(0);
                panel.Expand = false; panel.Width = null; panel.Height = null;
                body = Plain(panel);
            }
            finally
            {
                panel.Header = header; panel.Border = border; panel.Padding = padding;
                panel.Expand = expand; panel.Width = width; panel.Height = height;
            }
            writer.Paragraph(body, UiColors.Value, 0);
            return;
        }
        console.Write(value);
    }

    public static void StatusLine(StatusKind kind, string message, IAnsiConsole? console = null)
    {
        console ??= UsageFormatter.DefaultConsole(kind == StatusKind.Error ? Console.Error : Console.Out);
        string label = kind switch
        {
            StatusKind.Success => "ok", StatusKind.Warning => "warning", StatusKind.Error => "error",
            StatusKind.Prompt => "input", StatusKind.Pending => "pending", StatusKind.Working => "working", _ => "info"
        };
        string color = kind switch
        {
            StatusKind.Success => UiColors.Success, StatusKind.Warning => UiColors.Warning,
            StatusKind.Error => UiColors.Error, _ => UiColors.Value
        };
        new HelpWriter(console).Paragraph($"{label}: {message}", color, 0);
    }

    public static T Prompt<T>(IPrompt<T> prompt) where T : notnull => AnsiConsole.Console.Prompt(ConfigurePrompt(prompt));
    public static List<T> Prompt<T>(MultiSelectionPrompt<T> prompt) where T : notnull => AnsiConsole.Console.Prompt(ConfigurePrompt(prompt));
    public static T Ask<T>(string text) where T : notnull => Prompt(new TextPrompt<T>(HelpWriter.Sanitize(text)));
    internal static IPrompt<T> ConfigurePrompt<T>(IPrompt<T> prompt) where T : notnull
    {
        var highlight = new Style(UiColors.ParseHex(UiColors.Accent), decoration: Decoration.Bold);
        if (prompt is SelectionPrompt<T> single)
        {
            single.PageSize = 10;
            single.HighlightStyle = highlight;
            single.MoreChoicesText = "Use Up/Down to see more choices.";
        }
        if (prompt is TextPrompt<T> text) text.PromptStyle = new Style(UiColors.ParseHex(UiColors.Accent));
        return prompt;
    }
    internal static MultiSelectionPrompt<T> ConfigurePrompt<T>(MultiSelectionPrompt<T> prompt) where T : notnull
    {
        prompt.PageSize = 10;
        prompt.HighlightStyle = new Style(UiColors.ParseHex(UiColors.Accent), decoration: Decoration.Bold);
        prompt.MoreChoicesText = "Use Up/Down to see more choices.";
        prompt.InstructionsText = "Space selects; Enter confirms.";
        return prompt;
    }
    public static Progress Progress() => UsageFormatter.DefaultConsole().Progress().AutoClear(false).HideCompleted(false);
    public static Status Status() => UsageFormatter.DefaultConsole().Status();
    public static void Clear()
    {
        if (!Console.IsOutputRedirected && !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase))
            AnsiConsole.Console.Clear();
    }

    private static IRenderable Parse(string text)
    {
        text = HelpWriter.Sanitize(text);
        try { return new Markup(text); }
        catch (InvalidOperationException) { return new Text(text); }
    }
    private static string Plain(IRenderable value)
    {
        using var output = new StringWriter();
        var console = UsageFormatter.PlainConsole(output);
        console.Profile.Width = 4096;
        console.Write(value);
        return HelpWriter.Sanitize(output.ToString());
    }
}
