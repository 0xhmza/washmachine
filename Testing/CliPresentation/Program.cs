using System.Diagnostics;
using Spectre.Console;
using Washmachine.Cli;
using Washmachine.Cli.Ui;
using CatalogProgram = Washmachine.Cli.Program;

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

// Subprocess probes exercise real stdout/stderr and environment detection;
// they never load the application, provision tools, or run payload workflows.
if (args is ["--verify-resize"])
{
    await ConPtyResizeTests.VerifyAsync();
    return;
}
if (args is ["--resize-child", var reportPath])
{
    await ConPtyResizeTests.RunChildAsync(reportPath, usage);
    return;
}
if (args is ["--probe-help"])
{
    UsageFormatter.Print(usage);
    return;
}
if (args is ["--probe-catalog"])
{
    CatalogProgram.RenderSessionChoiceCatalog("Choices", [new("alpha", "First choice"), new("beta", "Second choice")], "End of choices.");
    return;
}
if (args.Length > 0 && args[0] == "--probe-analysis")
{
    Environment.ExitCode = await AnalysisCommand.RunAsync(args[1..], Console.Out, Console.Error);
    return;
}

(IAnsiConsole Console, StringWriter Output) Capture(int width, bool ansi = false,
    ColorSystemSupport colors = ColorSystemSupport.TrueColor)
{
    var output = new StringWriter();
    var console = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = ansi ? AnsiSupport.Yes : AnsiSupport.No,
        ColorSystem = ansi ? colors : ColorSystemSupport.NoColors,
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
Check(HelpWriter.Sanitize("first\r\nsecond\rthird") == "first\nsecond\nthird", "CRLF and CR normalize to line breaks");
Check(HelpWriter.Sanitize("before\x1b[31mred\x1b[0mafter") == "beforeredafter", "Entire ANSI CSI sequences are removed");
Check(HelpWriter.Sanitize("before\x1b]0;window title\aafter") == "beforeafter", "OSC window-title sequences are removed");
Check(HelpWriter.Sanitize("before\x1b]8;;https://example.com\x1b\\label\x1b]8;;\x1b\\after") == "beforelabelafter", "OSC hyperlinks leave only visible text");
Check(HelpWriter.Sanitize("before\u009b2Jafter") == "beforeafter", "C1 CSI sequences are removed");
Check(HelpWriter.Sanitize("before\x1b[12;") == "before", "Incomplete escape sequences are safely discarded");
Check(HelpWriter.Sanitize("before\x1bPignored\x1b\\after") == "beforeafter", "DCS payload is removed");
Check(HelpWriter.Sanitize("before\u009dignored\u009cafter") == "beforeafter", "C1 OSC payload is removed");
Check(HelpWriter.Sanitize("x\ud800y") == "x\ufffdy", "Malformed Unicode has a predictable replacement");
foreach (int width in new[] { 8, 12, 20 })
{
    string wrapped = string.Join('\n', HelpWriter.Wrap("ab\u3000\u3000cd ef", width, 0));
    Check(wrapped.Split('\n').All(line => line.GetCellWidth() <= width - 1), "Wide whitespace fits display cells");
    Check(Compact(wrapped) == "abcdef", "Wide whitespace wrapping preserves words");
}
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
foreach (var colors in new[] { ColorSystemSupport.Legacy, ColorSystemSupport.Standard, ColorSystemSupport.EightBit, ColorSystemSupport.TrueColor })
{
    var limited = Capture(60, ansi: true, colors);
    limited.Console.Profile.Capabilities.Unicode = false;
    UsageFormatter.Print(usage, limited.Console);
    string output = limited.Output.ToString();
    Check(Compact(HelpWriter.Sanitize(output)).Contains("inspect<file>[--json]"), $"{colors} retains text without Unicode terminal support");
    Check(!output.Contains("38;2;"), $"{colors} default palette does not request RGB sequences");
}
Check(UiColors.ParseHex("#nothex") == Color.Default, "Invalid color falls back to terminal foreground");
Check(UiColors.ParseHex("default") == Color.Default, "Terminal foreground is supported");
Check(UiColors.ParseHex("cyan") == Color.Aqua, "Basic terminal accent is supported");
Check(ColorSchemes.Get(null).Foreground == "default", "Default palette respects the terminal foreground");
Check(ColorSchemes.Get("dracula").Name == "Dracula", "Existing explicit palette remains available");
Check(UiColors.ParseHex("#00ff80") == new Color(0, 255, 128), "Existing RGB themes retain their values");
Check(UiColors.ParseHex(" #00ff80 ") == new Color(0, 255, 128), "Color whitespace is handled consistently");
Check(ColorSchemes.Names.Contains("terminal") && ColorSchemes.Names.Contains("dracula"), "Palette names expose native and explicit themes");
foreach (var text in new[] { "", "hello", new string('x', 4096), "café-文件-😀", "e\u0301" })
foreach (int width in new[] { 0, 1, 2, 10, 40, 80, 200 })
foreach (int cursor in new[] { 0, text.Length / 2, text.Length })
{
    var view = TerminalLayout.View(text, cursor, width);
    Check(view.Text.GetCellWidth() <= width, "Input view fits");
    Check(view.CursorColumn >= 0 && view.CursorColumn <= Math.Max(0, width - 1), "Cursor stays bounded");
}
Check(TerminalLayout.View("e\u0301", 0, 1).Text == "e\u0301", "Input keeps combining characters together");
Check(TerminalLayout.View("😀x", 1, 4).CursorColumn == 0, "Cursor inside a surrogate pair snaps to its start");
Check(!TerminalLayout.View("ab\u202Ecd", 5, 20).Text.Contains('\u202E'), "Input does not emit directional overrides");
Check(TerminalLayout.View("文件", 0, 1).Text == "?", "Unrenderable wide input has a visible fallback");
foreach (var text in new[] { "ae\u0301xyz", "x😀y", "x👩‍💻y", "x🇩🇪y", "a文件z" })
foreach (int width in new[] { 1, 2, 3, 7, 16 })
for (int cursor = 0; cursor <= text.Length; cursor++)
{
    var view = TerminalLayout.View(text, cursor, width);
    Check(view.Text.GetCellWidth() <= width, "Grapheme viewport fits at every UTF-16 cursor offset");
    Check(view.CursorColumn >= 0 && view.CursorColumn < width, "Grapheme cursor fits at every UTF-16 offset");
    Check(!view.Text.StartsWith('\u0301') && !view.Text.StartsWith('\u200d'), "Viewport never starts inside a grapheme");
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
var hostileSummary = new StringWriter();
AnalysisCommand.WriteSummary(hostileSummary, new Washmachine.Models.PeAnalysisResult
{
    FileName = "before\x1b]0;title\aafter\u202E\r\nnext",
    PeType = "PE\x1b[31m32\x1b[0m", Architecture = "x86"
}, details: false);
Check(hostileSummary.ToString().StartsWith("PE analysis: beforeafter next"), "Read-only metadata cannot introduce extra lines, titles or bidi overrides");
Check(!hostileSummary.ToString().Contains('\x1b') && !hostileSummary.ToString().Contains("title"), "Read-only metadata strips full terminal sequences");
var longText = string.Join(' ', Enumerable.Repeat("word[with]brackets 文件", 1000));
var longLines = HelpWriter.Wrap(longText, 40, 2).ToArray();
Check(longLines.All(line => line.GetCellWidth() <= 39), "Large help data stays within cell width");
Check(Compact(string.Join('\n', longLines)) == Compact(longText), "Large help data is retained without truncation");

var sampleChoices = new CatalogProgram.SessionValueChoice[]
{
    new("alpha [literal]", "Description with café, 文件 and e\u0301."),
    new(new string('x', 120), "Second line\nwith a newline and extra information.")
};
var sampleOptions = new CatalogProgram.SessionOptionRow[]
{
    new(7, "option-" + new string('=', 120) + "-tail", "C:\\two  spaces\\" + new string('z', 120), true,
        "A full description with [literal] text.", "first | second"),
    new(99, "additional-detail", "[sample]", false, "Subrow keeps a blank ID.",
        RequiredOverrideLabel: "Optional", RequiredOverrideColor: "invalid-color", IsSubRow: true)
};
foreach (int width in new[] { 1, 2, 12, 24, 40, 60, 80, 100, 120, 200 })
{
    var choices = Capture(width);
    CatalogProgram.RenderSessionChoiceCatalog("Choices [literal]", sampleChoices, "Choose a name.", console: choices.Console);
    string choiceOutput = choices.Output.ToString();
    Check(!choiceOutput.Contains('\x1b'), "Plain choices have no terminal commands");
    Check(choiceOutput.Count(c => c == 'x') == 121, $"All choice-name characters survive wrapping at width {width}");
    Check(Compact(choiceOutput).Contains("[literal]"), "Choice brackets are literal");
    Check(choiceOutput.Split('\n').All(line => line.TrimEnd('\r').GetCellWidth() <= Math.Max(1, width - 1)), $"Choice rows fit width {width}");
    var options = Capture(width);
    CatalogProgram.RenderSessionOptionCatalog("Options", sampleOptions, "Values are display data.", options.Console);
    string optionOutput = options.Output.ToString();
    Check(optionOutput.Count(c => c == 'z') == 120, "Current values are not truncated");
    Check(optionOutput.Count(c => c == '=') == 120, "Option names are not truncated");
    Check(Compact(optionOutput).Contains("7") && !Compact(optionOutput).Contains("99"), "Supplied IDs and blank subrow IDs remain stable");
    Check(Compact(optionOutput).Contains("Optional") && Compact(optionOutput).Contains("[sample]"), "Override labels and literal values remain visible");
    Check(optionOutput.Split('\n').All(line => line.TrimEnd('\r').GetCellWidth() <= Math.Max(1, width - 1)), $"Option rows fit width {width}");
    var banner = Capture(width);
    Banner.Render(banner.Console);
    Check(Compact(banner.Output.ToString()).Contains("Washmachinev1.0.0"), "Welcome text is retained in narrow terminals");
    Check(banner.Output.ToString().Split('\n').All(line => line.TrimEnd('\r').GetCellWidth() <= Math.Max(1, width - 1)), "Welcome text fits without fixed-width art");
    if (width == 80 && args.Contains("--preview")) Console.WriteLine(choiceOutput + optionOutput);
}
var emptyChoices = Capture(80);
CatalogProgram.RenderSessionChoiceCatalog("Empty choices", [], "Footer stays visible.", console: emptyChoices.Console);
Check(emptyChoices.Output.ToString().Contains("No entries found.") && emptyChoices.Output.ToString().Contains("Footer stays visible."), "Empty lists use the same message and retain their footer");
var literalTable = Capture(80);
literalTable.Console.Write(TableFactory.Create("[literal]\x1b]0;title\a", "[name]").AddRow(new Text("[value]")));
Check(literalTable.Output.ToString().Contains("[literal]") && literalTable.Output.ToString().Contains("[name]") && literalTable.Output.ToString().Contains("[value]"), "Table titles, headings and text cells remain literal");
Check(!literalTable.Output.ToString().Contains("title") && !literalTable.Output.ToString().Contains('\x1b'), "Table title terminal sequences are stripped");
var changingCatalog = Capture(120);
foreach (int width in new[] { 120, 24, 80, 2, 200 })
{
    changingCatalog.Console.Profile.Width = width;
    int before = changingCatalog.Output.GetStringBuilder().Length;
    CatalogProgram.RenderSessionOptionCatalog("Options", sampleOptions, "Footer", changingCatalog.Console);
    string segment = changingCatalog.Output.ToString()[before..];
    Check(segment.Count(c => c == 'z') == 120 && segment.Count(c => c == '=') == 120, "Resized catalogs retain all field data");
    Check(segment.Split('\n').All(line => line.TrimEnd('\r').GetCellWidth() <= Math.Max(1, width - 1)), "Catalog layout follows the current width");
}
var styledChoices = Capture(80, ansi: true);
CatalogProgram.RenderSessionChoiceCatalog("Choices", sampleChoices, console: styledChoices.Console);
Check(styledChoices.Output.ToString().Contains("\x1b["), "Interactive list headings can be styled");
Check(styledChoices.Output.ToString().Count(c => c == 'x') == 121, "Styled lists preserve the same data as plain lists");
int enumerations = 0;
IEnumerable<IReadOnlyList<string>> SinglePassRows()
{
    Check(++enumerations == 1, "Catalog row sources are enumerated once");
    yield return new[] { "alpha", "First choice" };
}
CatalogWriter.Write("Single pass", ["Name", "Description"], SinglePassRows(), console: Capture(80).Console);
var malformedCatalog = Capture(80);
try
{
    CatalogWriter.Write("Malformed", ["Name", "Description"], [new[] { "one cell" }], console: malformedCatalog.Console);
    Check(false, "Malformed rows must be rejected");
}
catch (ArgumentException)
{
    Check(malformedCatalog.Output.ToString() == "", "Malformed rows fail before partial output");
}

foreach (var scenario in new[] { "pipe", "no-color", "dumb", "ci-forced-color" })
{
    var probe = await RunProbe(["--probe-help"], scenario);
    Check(probe.ExitCode == 0 && probe.Error == "", $"{scenario} help exits cleanly");
    Check(probe.Output.Contains("INSPECT COMMAND") && probe.Output.Contains("--json"), $"{scenario} retains help content");
    Check(!probe.Output.Contains('\x1b'), $"{scenario} stdout contains no terminal commands");
    Check(probe.Output.Split('\n').All(line => line.TrimEnd('\r').GetCellWidth() <= 79), $"{scenario} uses predictable pipe width");
    var catalogProbe = await RunProbe(["--probe-catalog"], scenario);
    Check(catalogProbe.ExitCode == 0 && catalogProbe.Error == "" && !catalogProbe.Output.Contains('\x1b'), $"{scenario} catalogs honor plain output policy");
    Check(catalogProbe.Output.Contains("alpha") && catalogProbe.Output.Contains("beta") && catalogProbe.Output.Contains("End of choices."), $"{scenario} catalogs retain their entries and footer");
}
var errorProbe = await RunProbe(["--probe-analysis", "--unknown"], "pipe");
Check(errorProbe.ExitCode == 2 && errorProbe.Output == "" && errorProbe.Error.Contains("error:"), "Actual invalid-command process preserves stderr and exit code");
var jsonErrorProbe = await RunProbe(["--probe-analysis", "--json", "--unknown"], "pipe");
using (var jsonError = System.Text.Json.JsonDocument.Parse(jsonErrorProbe.Output))
{
    Check(jsonErrorProbe.ExitCode == 2 && jsonErrorProbe.Error == "", "Actual JSON failure has one stdout document and exit code 2");
    Check(!jsonError.RootElement.GetProperty("ok").GetBoolean(), "JSON error contract retained");
}
var missingOutput = new StringWriter();
var missingError = new StringWriter();
Check(await AnalysisCommand.RunAsync(["--json", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")], missingOutput, missingError) == 1, "Missing file exits 1");
using (var missingJson = System.Text.Json.JsonDocument.Parse(missingOutput.ToString()))
    Check(missingJson.RootElement.GetProperty("error").GetProperty("code").GetString() == "file_not_found" && missingError.ToString() == "", "Missing-file JSON is clean and typed");
Check(await AnalysisSession.RunAsync(new StringReader(""), new StringWriter(), new StringWriter()) == 0, "Read-only session EOF exits without retry loops");
stopwatch.Stop();
System.Console.WriteLine($"{checks} presentation/read-only checks passed in {stopwatch.ElapsedMilliseconds} ms. No payload operations executed.");

static async Task<(int ExitCode, string Output, string Error)> RunProbe(string[] arguments, string scenario)
{
    var info = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true, RedirectStandardError = true,
        RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true
    };
    info.ArgumentList.Add(typeof(UsageFormatter).Assembly.Location);
    foreach (string argument in arguments) info.ArgumentList.Add(argument);
    foreach (string name in new[] { "NO_COLOR", "TERM", "FORCE_COLOR", "CLICOLOR_FORCE", "CI", "COLORTERM", "GITHUB_ACTIONS" }) info.Environment.Remove(name);
    if (scenario == "no-color") info.Environment["NO_COLOR"] = "1";
    if (scenario == "dumb") info.Environment["TERM"] = "dumb";
    if (scenario == "ci-forced-color")
    {
        info.Environment["GITHUB_ACTIONS"] = "true";
        info.Environment["FORCE_COLOR"] = "1";
    }
    using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not launch test probe.");
    process.StandardInput.Close();
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
    return (process.ExitCode, await output, await error);
}
