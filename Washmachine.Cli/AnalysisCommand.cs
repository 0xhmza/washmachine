using System.Globalization;
using System.Text.Json;
using Washmachine.Logging;
using Washmachine.Models;
using Washmachine.Services;

namespace Washmachine.Cli;

/// <summary>A non-interactive, read-only command with predictable script behavior.</summary>
internal static class AnalysisCommand
{
    public static void WriteHelp(TextWriter output)
    {
        Ui.UsageFormatter.Print(new Ui.CommandUsage(
            "analyze",
            "Inspect a Windows executable or library without executing or modifying it.",
            "washmachine.exe --cli-mode analyze <file> [options]",
            Description: "A compact summary is shown by default. No tool downloads are required.",
            OptionGroups: [new("Options", null, [
                new("--pe <file>, -Pe", "Alternative to the positional file path."),
                new("--details", "Include section and import inventories."),
                new("--interactive", "Open an analysis-only session; use alone in a terminal."),
                new("--json, -Json", "Emit one JSON document without progress messages."),
                new("-h, --help", "Show this help."),
                new("--", "Treat remaining arguments as a file path, even when it starts with '-'.")
            ])],
            Sections: [new("Exit codes", Notes: [
                new("0", "Success or help."), new("1", "File or analysis failure."), new("2", "Invalid arguments.")
            ]), new("Output", Bullets: [
                "JSON success retains the PeAnalysisResult schema (IsValid, FileHash, etc.).",
                "JSON failures contain ok:false and error:{code,message}. Human errors go to stderr."
            ])],
            Examples: [
                new("washmachine.exe --cli-mode analyze library.dll --details", "Inspect sections and imports."),
                new("washmachine.exe --cli-mode analyze --interactive", "Open a read-only session."),
                new("washmachine.exe --cli-mode analyze --json library.dll > report.json", "Save a machine-readable report.")
            ]), Ui.UsageFormatter.PlainConsole(output));
    }

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        Action<PeAnalysisResult>? onSuccess = null)
    {
        // Do not interpret flags after '--' as options, including help and JSON.
        var optionEnd = Array.IndexOf(args, "--");
        var options = args.Take(optionEnd < 0 ? args.Length : optionEnd).ToArray();
        bool json = options.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase) || a.Equals("-Json", StringComparison.OrdinalIgnoreCase));
        bool details = false;
        string? file = null;
        bool positionalOnly = false;

        int Fail(int exitCode, string code, string message)
        {
            if (json)
                output.WriteLine(JsonSerializer.Serialize(new { ok = false, error = new { code, message } }));
            else
            {
                error.WriteLine($"error: {Safe(message)}");
                error.WriteLine("Run 'washmachine.exe --cli-mode analyze --help' for usage.");
            }
            return exitCode;
        }

        if (options.Any(a => a is "--help" or "-h" or "help" or "-Help" or "-help" or "-?" or "/?" or "?"))
        {
            WriteHelp(output);
            return 0;
        }
        if (args.Length == 0)
            return Fail(2, "invalid_arguments", "A PE file is required. Pass one path, quoted if it contains spaces.");

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!positionalOnly)
            {
                if (arg == "--") { positionalOnly = true; continue; }
                if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase) || arg.Equals("-Json", StringComparison.OrdinalIgnoreCase)) continue;
                if (arg == "--details") { details = true; continue; }
                if (arg == "--interactive")
                    return Fail(2, "invalid_arguments", "Use analyze --interactive alone in a terminal; do not combine it with a path or other output options.");
                if (arg is "--pe" or "-Pe")
                {
                    if (++i == args.Length || args[i].StartsWith('-'))
                        return Fail(2, "invalid_arguments", $"{arg} requires a file path.");
                    if (file != null) return Fail(2, "invalid_arguments", "Specify exactly one PE file.");
                    file = args[i];
                    continue;
                }
                if (arg.StartsWith('-'))
                    return Fail(2, "invalid_arguments", $"Unknown option '{arg}'. Available options: --pe, --details, --json, --help.");
            }
            if (file != null) return Fail(2, "invalid_arguments", "Specify exactly one PE file.");
            file = arg;
        }

        if (json && details) return Fail(2, "invalid_arguments", "Choose either --json or --details; JSON already contains full analysis data.");
        file = file?.Trim();
        if (file?.Length >= 2 && file[0] == '"' && file[^1] == '"') file = file[1..^1];
        if (string.IsNullOrWhiteSpace(file)) return Fail(2, "invalid_arguments", "A PE file path is required.");
        if (!File.Exists(file)) return Fail(1, "file_not_found", $"File not found: {file}");

        try
        {
            var result = await new PeAnalyzerService(new QuietLogger()).AnalyzeAsync(file);
            if (!result.IsValid)
                return Fail(1, "invalid_pe", string.IsNullOrWhiteSpace(result.ValidationError) ? "Not a valid PE file." : result.ValidationError);
            if (json) output.WriteLine(JsonSerializer.Serialize(result));
            else WriteSummary(output, result, details);
            onSuccess?.Invoke(result);
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(1, "analysis_failed", ex.Message);
        }
    }

    internal static void WriteSummary(TextWriter output, PeAnalysisResult result, bool details)
    {
        var console = Ui.UsageFormatter.PlainConsole(output);
        Ui.UsageFormatter.PrintFields("PE analysis", [
            new("File", Safe(result.FileName)),
            new("Type", $"{Safe(result.PeType)} / {Safe(result.Architecture)}{(result.IsDotNet ? " / .NET" : "")}"),
            new("Size", $"{result.FileSize.ToString(CultureInfo.InvariantCulture)} bytes"),
            new("SHA-256", result.FileHash),
            new("Entry", $"0x{result.OptionalHeader.AddressOfEntryPoint:x8}"),
            new("Sections", result.Sections.Count.ToString(CultureInfo.InvariantCulture)),
            new("Imports", $"{result.TotalImports} functions / {result.Imports.Count} libraries"),
            new("Exports", result.TotalExports.ToString(CultureInfo.InvariantCulture)),
            new("Entropy", $"{result.OverallEntropy.ToString("F2", CultureInfo.InvariantCulture)} / 8.00"),
            new("Protections", Safe(string.Join(", ", result.Security.EnabledProtections)))
        ], console);
        var writer = new Ui.HelpWriter(console);
        writer.Paragraph("Read-only inspection. Protection flags and signature presence are not a trust verdict.", Ui.UiColors.Muted);
        if (!details)
        {
            writer.Paragraph("Use --details for inventories, or --json for automation.", Ui.UiColors.Muted);
            return;
        }
        Ui.CatalogWriter.Write("Sections", ["Name", "RVA", "Raw bytes", "Permissions", "Entropy"],
            result.Sections.Select(section => (IReadOnlyList<string>)new[]
            {
                Safe(section.Name), $"0x{section.VirtualAddress:x8}", section.RawSize.ToString(CultureInfo.InvariantCulture),
                section.PermissionsString, section.Entropy.ToString("F2", CultureInfo.InvariantCulture)
            }), console: console);
        Ui.CatalogWriter.Write("Imports", ["Library", "Function"],
            result.Imports.SelectMany(library => library.Functions.Count == 0
                ? new[] { (IReadOnlyList<string>)new[] { Safe(library.Name), "(none)" } }
                : library.Functions.Select(function => (IReadOnlyList<string>)new[] { Safe(library.Name), Safe(function.Name) })),
            console: console);
    }

    // PE metadata and paths are untrusted; never let them inject terminal controls.
    private static string Safe(string? value) => Ui.HelpWriter.Sanitize(value ?? "").Replace('\n', ' ');

    private sealed class QuietLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Ok(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Debug(string message) { }
    }
}
