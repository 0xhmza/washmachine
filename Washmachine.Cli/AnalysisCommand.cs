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
        output.WriteLine($"PE analysis: {Safe(result.FileName)}");
        output.WriteLine($"  Type       {Safe(result.PeType)} / {Safe(result.Architecture)}{(result.IsDotNet ? " / .NET" : "")}");
        output.WriteLine($"  Size       {result.FileSize.ToString(CultureInfo.InvariantCulture)} bytes");
        output.WriteLine($"  SHA-256    {result.FileHash}");
        output.WriteLine($"  Entry      0x{result.OptionalHeader.AddressOfEntryPoint:x8}");
        output.WriteLine($"  Sections   {result.Sections.Count}");
        output.WriteLine($"  Imports    {result.TotalImports} functions / {result.Imports.Count} libraries");
        output.WriteLine($"  Exports    {result.TotalExports}");
        output.WriteLine($"  Entropy    {result.OverallEntropy.ToString("F2", CultureInfo.InvariantCulture)} / 8.00");
        output.WriteLine($"  Protections {Safe(string.Join(", ", result.Security.EnabledProtections))}");
        output.WriteLine("Read-only inspection. Protection flags and signature presence are not a trust verdict.");
        if (!details)
        {
            output.WriteLine("Use --details for inventories, or --json for automation.");
            return;
        }
        output.WriteLine();
        output.WriteLine("Sections (RVA / raw bytes / permissions / entropy):");
        foreach (var section in result.Sections)
            output.WriteLine($"  {Safe(section.Name),-10} 0x{section.VirtualAddress:x8} / {section.RawSize} / {section.PermissionsString} / {section.Entropy.ToString("F2", CultureInfo.InvariantCulture)}");
        output.WriteLine();
        output.WriteLine("Imports:");
        if (result.Imports.Count == 0) output.WriteLine("  None found.");
        foreach (var library in result.Imports)
        {
            output.WriteLine($"  {Safe(library.Name)} ({library.Functions.Count})");
            foreach (var function in library.Functions) output.WriteLine($"    {Safe(function.Name)}");
        }
    }

    // PE metadata and paths are untrusted; never let them inject terminal controls.
    private static string Safe(string? value) => new((value ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray());

    private sealed class QuietLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Ok(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Debug(string message) { }
    }
}
