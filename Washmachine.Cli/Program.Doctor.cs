using Spectre.Console;
using Washmachine.Cli.Ui;
using Washmachine.Logging;
using Washmachine.Services;

namespace Washmachine.Cli;

public static partial class Program
{
    /// <summary>
    /// Runs <see cref="ToolPreflightService"/> and prints a Spectre table of
    /// every external tool: location, version, and whether it meets the LLVM
    /// minimum required by the bundled obfuscation passes.
    /// </summary>
    private static async Task<int> RunDoctorAsync(string[] args)
    {
        if (args.Length > 0 && IsHelpToken(args[0]))
        {
            PrintDoctorUsage();
            return 0;
        }

        bool jsonMode = args.Any(a => a is "--json");

        var paths = new AppPaths();
        var logger = new ConsoleLogger();
        var preflight = new ToolPreflightService(paths, new CompilerToolLocator(logger), logger);
        var report = await preflight.RunAsync();

        if (jsonMode)
        {
            var payload = new
            {
                allOk = report.AllOk,
                statuses = report.Statuses,
                requiredLlvmMajor = ToolPreflightService.RequiredLlvmMajor,
                recommendedLlvmMajor = ToolPreflightService.RecommendedLlvmMajor,
            };
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(payload,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return report.AllOk ? 0 : 1;
        }

        var s = UiColors.ActiveScheme;
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(UiColors.BoxBorderColor)
            .Title($"[bold {s.Header}]Tool preflight[/]")
            .AddColumn(new TableColumn($"[{s.Label}]Tool[/]"))
            .AddColumn(new TableColumn($"[{s.Label}]Status[/]"))
            .AddColumn(new TableColumn($"[{s.Label}]Version[/]"))
            .AddColumn(new TableColumn($"[{s.Label}]Detail[/]"));

        foreach (var st in report.Statuses)
        {
            string statusText;
            string statusColor;
            if (!st.Found) { statusText = "MISSING"; statusColor = s.Error; }
            else if (!st.MeetsRequirements) { statusText = "INCOMPATIBLE"; statusColor = s.Warning; }
            else { statusText = "OK"; statusColor = s.Success; }

            table.AddRow(
                $"[{s.Accent}]{Markup.Escape(st.Tool)}[/]",
                $"[{statusColor}]{statusText}[/]",
                $"[{s.Value}]{Markup.Escape(st.Version ?? "—")}[/]",
                $"[{s.Muted}]{Markup.Escape(st.Detail)}[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        if (report.AllOk)
        {
            WriteStatus(StatusPrefix.Success, "All required tools present and version-compatible.");
            return 0;
        }

        foreach (var miss in report.Missing)
            WriteStatus(StatusPrefix.Failure, $"{miss.Tool}: {miss.Detail}");
        foreach (var inc in report.Incompatible)
            WriteStatus(StatusPrefix.Warning, $"{inc.Tool}: {inc.Detail}");

        AnsiConsole.MarkupLine(
            $"[{s.Muted}]Required LLVM major:[/] [{s.Value}]≥ {ToolPreflightService.RequiredLlvmMajor}[/]   " +
            $"[{s.Muted}]Recommended:[/] [{s.Value}]{ToolPreflightService.RecommendedLlvmMajor}+[/]");

        return 1;
    }

    private static int PrintDoctorUsage()
    {
        UsageFormatter.Print(new CommandUsage(
            Name: "doctor",
            Summary: "Verify that LLVM/clang, MSVC cl.exe, and Bin2Shell are installed and version-compatible.",
            Syntax: "washmachine-cli doctor [--json]",
            Description: "Runs the same preflight that auto-fires when the REPL launches, but prints a full per-tool report.",
            WhenToUse: "Run before shipping a build, when troubleshooting an obfuscation-pass load failure, or to confirm an LLVM upgrade.",
            Output: "A table of tool status + remediation hints. Exit code 0 when all OK, 1 otherwise.",
            OptionGroups: [],
            Sections: [],
            Examples:
            [
                new UsageExample("washmachine-cli doctor", "Print the preflight table.", "Any shell"),
                new UsageExample("washmachine-cli doctor --json", "Machine-friendly output for CI scripts.", "Any shell"),
            ],
            Related: []));
        return 0;
    }
}
