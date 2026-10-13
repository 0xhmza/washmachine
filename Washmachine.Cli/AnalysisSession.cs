using System.Diagnostics;
using System.Text.Json;
using Washmachine.Models;

namespace Washmachine.Cli;

/// <summary>Analysis-only session. No provisioning, build recipe, or shell execution.</summary>
internal static class AnalysisSession
{
    public static async Task<int> RunAsync(TextReader input, TextWriter output, TextWriter error)
    {
        var console = Ui.UsageFormatter.PlainConsole(output);
        var errors = Ui.UsageFormatter.PlainConsole(error);
        var writer = new Ui.HelpWriter(console);
        writer.Heading("Analysis session");
        writer.Paragraph("Enter a file path. Commands: help, details, json, quit.", Ui.UiColors.Value);
        writer.Paragraph("Read-only inspection. Blank input or EOF exits.", Ui.UiColors.Muted);
        PeAnalysisResult? current = null;

        while (true)
        {
            output.WriteLine();
            output.Write("washmachine analyze > ");
            await output.FlushAsync();
            var line = await input.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line) || line.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase)
                || line.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                return 0;
            line = line.Trim();
            if (line.Equals("help", StringComparison.OrdinalIgnoreCase) || line == "?")
            {
                Ui.UsageFormatter.PrintFields("Analysis session", [
                    new("File path", "Paste or drag in an EXE/DLL path; quotes and spaces are supported."),
                    new("details", "Show cached sections and imports for the last successfully scanned file."),
                    new("json", "Show its cached JSON report. For scripts, use analyze FILE --json."),
                    new("Refresh", "Paste the file path again after the file changes."),
                    new("quit", "Exit this session. Blank input or EOF also exits.")
                ], Ui.UsageFormatter.PlainConsole(output));
                continue;
            }

            var mode = line.ToLowerInvariant();
            bool repeat = mode is "details" or "json";
            if (repeat && current == null)
            {
                Ui.CliConsole.StatusLine(Ui.StatusKind.Error, "No file selected yet. Paste a file path first.", errors);
                continue;
            }
            if (repeat)
            {
                if (mode == "json") output.WriteLine(JsonSerializer.Serialize(current));
                else AnalysisCommand.WriteSummary(output, current!, details: true);
                Ui.CliConsole.StatusLine(Ui.StatusKind.Info, "Cached result; paste the file path again to rescan.", console);
                continue;
            }
            // '--' prevents a pasted path from being interpreted as CLI options.
            var args = new[] { "--", line };
            Ui.CliConsole.StatusLine(Ui.StatusKind.Working, "Scanning (read-only)...", console);
            await output.FlushAsync();
            var started = Stopwatch.GetTimestamp();
            var code = await AnalysisCommand.RunAsync(args, output, error, result => current = result);
            if (code == 0)
            {
                Ui.CliConsole.StatusLine(Ui.StatusKind.Success, $"Complete in {Stopwatch.GetElapsedTime(started).TotalSeconds:F2}s. File unchanged.", console);
            }
            else
            {
                Ui.CliConsole.StatusLine(Ui.StatusKind.Error, "Scan failed. Correct the path and try again; your previous selection is unchanged.", errors);
            }
        }
    }
}
