using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using Washmachine.Cli.Ui;
using Washmachine.Logging;
using Washmachine.Models;
using Washmachine.Services;

namespace Washmachine.Cli;

public static partial class Program
{
    private sealed record ShowRow(int Id, string Name, string Description);

    private static readonly Regex ShowNameWordBoundaryRegex = new(
        @"([a-z0-9])([A-Z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static CommandUsage BuildShowUsage() =>
        new(
            Name: "show",
            Summary: "Display encoders, envelopes, modules, or supporting catalogs in a Metasploit-style view.",
            Syntax: "washmachine-cli show <all|encoders|envelopes|modules|templates|compilers> [category]",
            Description: "show renders colorized catalog tables with stable row IDs, canonical path-like names, and descriptions.",
            WhenToUse: "Use it whenever you need discovery data before choosing a template, snippet, encoder, or compiler path.",
            Output: "Prints Metasploit-inspired catalog sections using #, Name, and Description columns.",
            OptionGroups:
            [
                new UsageOptionGroup(
                    "Targets",
                    "Primary discovery surfaces. Legacy --prefixed targets still resolve for compatibility.",
                    [
                        new UsageOption("all", "Show encoders, envelopes, and modules in one combined pass."),
                        new UsageOption("encoders", "Show encoder/<name> entries from the live Bin2Shell catalog."),
                        new UsageOption("envelopes", "Show envelope/<name> entries from the live Bin2Shell catalog."),
                        new UsageOption("modules [category]", "Show module/<category>/<name> entries from the active playbook. Add a category such as anti_analysis to filter."),
                        new UsageOption("templates", "Show template/<name> entries from the active playbook."),
                        new UsageOption("compilers", "Show compiler/<kind> entries discovered on this machine."),
                    ]),
            ],
            Sections:
            [
                new UsageSection(
                    "Notes",
                    Bullets:
                    [
                        "show encoders and show envelopes depend on Bin2Shell being present. If it is missing, run provision first.",
                        "show modules mirrors the snippet sections/items that encode accepts through --snippet.",
                        "show all focuses on Metasploit-style module surfaces: encoders, envelopes, and modules.",
                    ]),
            ],
            Examples:
            [
                new UsageExample("washmachine-cli show all", "Inspect the full module surface in one pass.", "Any shell"),
                new UsageExample("washmachine-cli show encoders", "Inspect the live encoder catalog.", "Any shell"),
                new UsageExample("washmachine-cli show envelopes", "Inspect the live envelope catalog.", "Any shell"),
                new UsageExample("washmachine-cli show modules anti_analysis", "Filter playbook modules to one category.", "Any shell"),
                new UsageExample("washmachine-cli show templates", "Inspect the template catalog used by encode -t.", "Any shell"),
            ],
            Related:
            [
                new UsageNote("encode", "Use show first when you need valid template, module, encoder, or envelope identifiers."),
                new UsageNote("provision", "Run it if show encoders or show envelopes reports that Bin2Shell is unavailable."),
            ]);

    private static int PrintShowUsage()
    {
        UsageFormatter.Print(BuildShowUsage());
        return 0;
    }

    private static async Task<int> RunShowAsync(string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine($"[{UiColors.Error}][[-]][/] Argument required");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[{UiColors.Muted}][[*]][/] Valid parameters for the [{UiColors.Accent}]show[/] command are: [{UiColors.Accent}]all[/], [{UiColors.Accent}]encoders[/], [{UiColors.Accent}]envelopes[/], [{UiColors.Accent}]modules[/], [{UiColors.Accent}]templates[/], [{UiColors.Accent}]compilers[/], [{UiColors.Accent}]execution[/]");
            return 1;
        }

        if (args.Length > 2)
        {
            AnsiConsole.MarkupLine($"[{UiColors.Error}]Error:[/] show accepts at most one target and one optional module category filter.");
            return 1;
        }

        string target = NormalizeShowTarget(args[0]);
        string? filter = args.Length > 1 ? NormalizePathSegment(args[1]) : null;
        var paths = new AppPaths();
        var logger = new ConsoleLogger();

        if (filter is not null && target != "modules")
        {
            AnsiConsole.MarkupLine($"[{UiColors.Error}]Error:[/] Only [{UiColors.Accent}]show modules <category>[/] accepts a second argument.");
            return 1;
        }

        return target switch
        {
            "all" => await ShowAllAsync(paths),
            "encoders" => await ShowEncodingSectionAsync(paths, "Encoders", "encoder", showEncoders: true),
            "envelopes" => await ShowEncodingSectionAsync(paths, "Envelopes", "envelope", showEncoders: false),
            "modules" => ShowModuleSection(paths, filter),
            "templates" => ShowTemplateSection(paths),
            "compilers" => await ShowCompilerSectionAsync(logger),
            "execution" => ShowExecutionSection(paths),
            _ => ShowUnknownTarget(target),
        };
    }

    private static async Task<int> ShowAllAsync(AppPaths paths)
    {
        int exitCode = 0;
        exitCode = Math.Max(exitCode, await ShowEncodingSectionAsync(paths, "Encoders", "encoder", showEncoders: true));
        AnsiConsole.WriteLine();
        exitCode = Math.Max(exitCode, await ShowEncodingSectionAsync(paths, "Envelopes", "envelope", showEncoders: false));
        AnsiConsole.WriteLine();
        exitCode = Math.Max(exitCode, ShowModuleSection(paths, filter: null));
        return exitCode;
    }

    private static async Task<int> ShowEncodingSectionAsync(AppPaths paths, string title, string prefix, bool showEncoders)
    {
        var runner = new Bin2ShellRunner(paths);
        var service = new ShellcodeEncodingCatalogService(runner, paths);

        ShellcodeEncodingCatalog catalog;
        try
        {
            catalog = await service.GetCatalogAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[{UiColors.Warning}]Warning:[/] Bin2Shell is not available for [{UiColors.Accent}]show {title.ToLowerInvariant()}[/].");
            AnsiConsole.MarkupLine($"[{UiColors.Muted}]{Markup.Escape(ex.Message)}[/]");
            AnsiConsole.MarkupLine($"[{UiColors.Muted}]Run [{UiColors.Accent}]washmachine-cli provision[/] [{UiColors.Muted}]and try again.[/]");
            return 1;
        }

        var source = showEncoders ? catalog.Encoders : catalog.Envelopes;
        var rows = source
            .Select(item => new ShowRow(
                item.Index,
                $"{prefix}/{NormalizePathSegment(item.Name)}",
                NormalizeDescription(item.Description)))
            .ToArray();

        RenderShowTable(title, rows, showEncoders ? UiColors.Success : UiColors.Warning);
        return 0;
    }

    private static int ShowModuleSection(AppPaths paths, string? filter)
    {
        var catalog = new YamlCodeSnippetCatalogService(paths);
        var sections = catalog.GetAllSections()
            .Where(section => !IsSyntheticSnippetSection(section))
            .Where(section => filter is null || SectionMatchesCategory(section, filter))
            .ToList();

        if (filter is not null && sections.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{UiColors.Error}]Error:[/] Unknown module category: {Markup.Escape(filter)}.");
            AnsiConsole.MarkupLine($"[{UiColors.Muted}]Use [{UiColors.Accent}]show modules[/] [{UiColors.Muted}]to inspect available categories.[/]");
            return 1;
        }

        var rows = sections
            .SelectMany(section => section.Items
                .Where(item => !IsSyntheticModuleItem(item))
                .Select(item => new
                {
                    Name = $"module/{GetSectionCategory(section)}/{NormalizePathSegment(item.Id)}",
                    Description = NormalizeDescription(item.Display)
                }))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => new ShowRow(index, item.Name, item.Description))
            .ToArray();

        RenderShowTable(filter is null ? "Modules" : $"Modules ({filter})", rows, UiColors.Link);
        return 0;
    }

    private static int ShowTemplateSection(AppPaths paths)
    {
        var catalog = new YamlCodeSnippetCatalogService(paths);
        var rows = catalog.GetTemplates()
            .OrderBy(template => template.Id, StringComparer.OrdinalIgnoreCase)
            .Select((template, index) => new ShowRow(
                index,
                $"template/{NormalizePathSegment(template.Id)}",
                NormalizeDescription(string.IsNullOrWhiteSpace(template.Description) ? template.Display : template.Description)))
            .ToArray();

        RenderShowTable("Templates", rows, UiColors.Accent);
        return 0;
    }

    private static int ShowExecutionSection(AppPaths paths)
    {
        var catalog = new YamlCodeSnippetCatalogService(paths);
        var section = catalog.GetAllSections()
            .FirstOrDefault(s => string.Equals(s.Template, "shellcodeexecution", StringComparison.OrdinalIgnoreCase));

        if (section is null)
        {
            AnsiConsole.MarkupLine($"[{UiColors.Error}]Error:[/] Shellcode execution section not found in catalog.");
            return 1;
        }

        var rows = section.Items
            .Where(item => !IsSyntheticModuleItem(item))
            .OrderBy(item => item.Display, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => new ShowRow(
                index,
                NormalizePathSegment(item.Id),
                NormalizeDescription(item.Display)))
            .ToArray();

        RenderShowTable("Shellcode Execution", rows, UiColors.Accent);
        return 0;
    }

    private static async Task<int> ShowCompilerSectionAsync(ConsoleLogger logger)
    {
        var locator = new CompilerToolLocator(logger);
        var result = await locator.DiscoverAsync();

        var rows = result.Candidates
            .Select((candidate, index) => new ShowRow(
                index,
                $"compiler/{NormalizePathSegment(candidate.Kind.ToString())}",
                NormalizeDescription($"{candidate.Path}{(result.Best?.Path == candidate.Path ? " (preferred)" : string.Empty)}")))
            .ToArray();

        RenderShowTable("Compilers", rows, UiColors.Success);

        if (result.Errors.Count > 0)
        {
            AnsiConsole.WriteLine();
            foreach (var error in result.Errors)
                AnsiConsole.MarkupLine($"[{UiColors.Warning}]warning:[/] [{UiColors.Muted}]{Markup.Escape(error)}[/]");
        }

        return 0;
    }

    private static int ShowUnknownTarget(string target)
    {
        AnsiConsole.MarkupLine($"[{UiColors.Error}]Error:[/] Unknown show target: {Markup.Escape(target)}.");
        AnsiConsole.MarkupLine($"[{UiColors.Muted}]Use [{UiColors.Accent}]all[/], [{UiColors.Accent}]encoders[/], [{UiColors.Accent}]envelopes[/], [{UiColors.Accent}]modules[/], [{UiColors.Accent}]templates[/], [{UiColors.Accent}]compilers[/], or [{UiColors.Accent}]execution[/].[/]");
        return 1;
    }

    private static void RenderShowTable(string title, IReadOnlyList<ShowRow> rows, string nameColor)
    {
        AnsiConsole.MarkupLine($"[bold {UiColors.Header}]{Markup.Escape(title)}[/]");
        AnsiConsole.MarkupLine($"[{UiColors.Muted}]{new string('=', title.Length)}[/]");
        AnsiConsole.WriteLine();

        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{UiColors.Muted}]No entries found.[/]");
            return;
        }

        int idWidth = Math.Max(1, rows.Max(row => row.Id.ToString(CultureInfo.InvariantCulture).Length));
        int nameWidth = Math.Max(4, rows.Max(row => row.Name.Length));

        WriteShowLine("#".PadLeft(idWidth), "Name".PadRight(nameWidth), "Description", UiColors.Accent);
        WriteShowLine(new string('-', idWidth), new string('-', nameWidth), new string('-', "Description".Length), UiColors.Muted);

        foreach (var row in rows)
        {
            WriteShowLine(
                row.Id.ToString(CultureInfo.InvariantCulture).PadLeft(idWidth),
                row.Name.PadRight(nameWidth),
                row.Description,
                nameColor);
        }
    }

    private static void WriteShowLine(string id, string name, string description, string color)
    {
        var line = new StringBuilder()
            .Append(id)
            .Append("   ")
            .Append(name)
            .Append("   ")
            .Append(description)
            .ToString();

        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(line)}[/]");
    }

    private static string[] GetShowModuleCategoryCandidates()
    {
        try
        {
            var catalog = new YamlCodeSnippetCatalogService(new AppPaths());
            return catalog.GetAllSections()
                .Where(s => !IsSyntheticSnippetSection(s))
                .Select(GetSectionCategory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string NormalizeShowTarget(string rawTarget)
    {
        string target = rawTarget.Trim().TrimStart('-').ToLowerInvariant();
        return target switch
        {
            "encoder" => "encoders",
            "envelope" => "envelopes",
            "module" => "modules",
            "template" => "templates",
            "compiler" => "compilers",
            "snippet" or "snippets" => "modules",
            "execution" or "exec" => "execution",
            _ => target
        };
    }

    private static string GetSectionCategory(CodeSnippetSection section)
    {
        if (!string.IsNullOrWhiteSpace(section.Display))
            return NormalizePathSegment(section.Display);
        if (!string.IsNullOrWhiteSpace(section.Header))
            return NormalizePathSegment(section.Header);
        return NormalizePathSegment(section.Template);
    }

    private static bool SectionMatchesCategory(CodeSnippetSection section, string filter)
    {
        string normalizedFilter = NormalizePathSegment(filter);
        return string.Equals(GetSectionCategory(section), normalizedFilter, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizePathSegment(section.Template), normalizedFilter, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizePathSegment(section.Header), normalizedFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSyntheticModuleItem(CodeSnippetItem item)
    {
        return string.Equals(item.Id, "None", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Display, "None", StringComparison.OrdinalIgnoreCase)
            || item.Display.Contains("Skip", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSyntheticSnippetSection(CodeSnippetSection section)
    {
        return string.Equals(section.Template, "shellcodeexecution", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section.Template, "genericshellcode", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDescription(string? description)
    {
        return string.IsNullOrWhiteSpace(description) ? "." : description.Trim();
    }

    private static string NormalizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        string normalized = ShowNameWordBoundaryRegex.Replace(value.Trim(), "$1_$2");
        normalized = normalized.Replace('-', '_').Replace(' ', '_').Replace('/', '_').Replace('\\', '_');
        normalized = Regex.Replace(normalized, @"[^A-Za-z0-9_]+", "_", RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"_+", "_", RegexOptions.CultureInvariant);
        normalized = normalized.Trim('_');
        return normalized.Length == 0 ? "unknown" : normalized.ToLowerInvariant();
    }
}
