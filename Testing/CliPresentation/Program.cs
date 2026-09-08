using System.Diagnostics;
using Spectre.Console;
using Washmachine.Cli;
using Washmachine.Cli.Ui;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
}
var usage = new CommandUsage("inspect", "Read a local file without modifying it.", "inspect <file> [--json]",
    Description: "Literal [brackets], café, 文件 and e\u0301 stay readable.",
    OptionGroups: [new("Options", "Select an output format.", [
        new("--json, -Json", "Write one JSON object to standard output.", "false", "true | false"),
        new("--long-option-with-a-long-name", new string('x', 200))])],
    Sections: [new("Notes", Notes: [new("文件", "Unicode labels align by cell width.")], Bullets: ["Files remain unchanged."])],
    Examples: [new("inspect \"C:\\folder with spaces\\sample.exe\"", "Read a file", "PowerShell")],
    Related: [new("help", "Display available commands.")]);

(IAnsiConsole Console, StringWriter Output) Capture(int width, bool ansi = false)
{
    var output = new StringWriter();
    var console = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = ansi ? AnsiSupport.Yes : AnsiSupport.No,
        ColorSystem = ansi ? ColorSystemSupport.TrueColor : ColorSystemSupport.NoColors,
        Interactive = InteractionSupport.No,
        Out = new AnsiConsoleOutput(output)
    });
    console.Profile.Width = width;
    return (console, output);
}
string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
var stopwatch = Stopwatch.StartNew();
foreach (int width in new[] { 1, 2, 4, 12, 24, 40, 60, 80, 120, 200 })
{
    var capture = Capture(width);
    UsageFormatter.Print(usage, capture.Console);
    var output = capture.Output.ToString();
    Check(!output.Contains('\x1b'), $"No ANSI in plain output at {width}");
    foreach (string line in output.Split('\n'))
        Check(line.TrimEnd('\r').GetCellWidth() <= Math.Max(1, width - 1), $"Line exceeds width {width}: {line}");
    Check(Compact(output).Contains("--json,-Json"), "Legacy flags remain visible");
    Check(Compact(output).Contains("[brackets]"), "Markup remains literal");
    Check(Compact(output).Contains(new string('x', 200)), "Long values are not truncated");
    Check(Compact(output).Contains("inspect\"C:\\folderwithspaces\\sample.exe\""), "Example content retained");
    var repeat = Capture(width);
    UsageFormatter.Print(usage, repeat.Console);
    Check(repeat.Output.ToString() == output, "Rendering is deterministic");
    if (width == 80 && args.Contains("--preview")) System.Console.WriteLine(output);
}
var dynamicCapture = Capture(120);
foreach (int width in new[] { 120, 20, 80, 2, 40, 120 })
{
    dynamicCapture.Console.Profile.Width = width;
    int before = dynamicCapture.Output.GetStringBuilder().Length;
    UsageFormatter.PrintFields("Option", [new("Current", "C:\\folder with spaces\\sample.exe")], dynamicCapture.Console);
    string segment = dynamicCapture.Output.ToString()[before..];
    Check(segment.Split('\n').All(line => line.TrimEnd('\r').GetCellWidth() <= Math.Max(1, width - 1)), "Changed width is respected");
}
var unsafeCapture = Capture(80);
Check(HelpWriter.Wrap("inspect \"C:\\two  spaces\\file.exe\"", 120, 0).Single()
    == "inspect \"C:\\two  spaces\\file.exe\"", "Spaces inside quoted paths are preserved");
UsageFormatter.PrintFields("[literal]", [new("Value", "before\x1b[2J\rAFTER\u202E\a")], unsafeCapture.Console);
string safeOutput = unsafeCapture.Output.ToString();
Check(!safeOutput.Contains('\x1b') && !safeOutput.Contains('\a') && !safeOutput.Contains('\u202E'), "Control sequences cannot reach terminal");
Check(safeOutput.Contains("[LITERAL]"), "Literal heading brackets");
var coloredCapture = Capture(80, true);
UsageFormatter.Print(usage, coloredCapture.Console);
Check(coloredCapture.Output.ToString().Contains("\x1b["), "Interactive color supported");
var plainAfterColor = Capture(80);
UsageFormatter.Print(usage, plainAfterColor.Console);
Check(!plainAfterColor.Output.ToString().Contains('\x1b'), "Console profiles remain independent");
foreach (var text in new[] { "", "hello", new string('x', 4096), "café-文件-😀", "e\u0301" })
foreach (int width in new[] { 0, 1, 2, 10, 40, 80, 200 })
foreach (int cursor in new[] { 0, text.Length / 2, text.Length })
{
    var view = TerminalLayout.View(text, cursor, width);
    Check(view.Text.GetCellWidth() <= width, "Input view fits");
    Check(view.CursorColumn >= 0 && view.CursorColumn <= Math.Max(0, width - 1), "Cursor stays bounded");
}
var analysisHelp = new StringWriter();
var analysisErrors = new StringWriter();
Check(await AnalysisCommand.RunAsync(["--help"], analysisHelp, analysisErrors) == 0, "Analysis help exits successfully");
Check(analysisHelp.ToString().Contains("USAGE") && analysisHelp.ToString().Contains("EXIT CODES"), "Analysis uses shared headings");
Check(analysisErrors.ToString() == "", "Help leaves stderr empty");
var samplePath = typeof(UsageFormatter).Assembly.Location;
var hashBefore = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(samplePath));
foreach (var parameters in new[] { new[] { "--json", samplePath }, new[] { "-Pe", samplePath, "-Json" } })
{
    var jsonOutput = new StringWriter();
    var errors = new StringWriter();
    Check(await AnalysisCommand.RunAsync(parameters, jsonOutput, errors) == 0, "Read-only analysis accepts modern and legacy arguments");
    using var document = System.Text.Json.JsonDocument.Parse(jsonOutput.ToString());
    Check(document.RootElement.GetProperty("IsValid").GetBoolean(), "JSON success schema retained");
    Check(errors.ToString() == "", "Successful analysis leaves stderr empty");
}
Check(hashBefore.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(samplePath))), "Read-only scan preserves bytes");
var invalidOutput = new StringWriter();
var invalidError = new StringWriter();
Check(await AnalysisCommand.RunAsync(["--unknown"], invalidOutput, invalidError) == 2, "Invalid arguments retain exit 2");
Check(invalidOutput.ToString() == "" && invalidError.ToString().Contains("error:"), "Human errors remain on stderr");
var sessionOutput = new StringWriter();
Check(await AnalysisSession.RunAsync(new StringReader("help\nquit\n"), sessionOutput, new StringWriter()) == 0, "Session help exits normally");
Check(sessionOutput.ToString().Contains("ANALYSIS SESSION"), "Session uses shared heading");
stopwatch.Stop();
System.Console.WriteLine($"{checks} presentation/read-only checks passed in {stopwatch.ElapsedMilliseconds} ms. No payload operations executed.");
