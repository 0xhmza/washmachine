namespace Washmachine.Cli.Ui;

using Spectre.Console;
using Washmachine.Logging;
using Washmachine.Services;

/// <summary>
/// Runs the CLI's "requirements loading screen" before dropping into the
/// REPL: provisions external tools, locates a C/C++ compiler, and — if no
/// compiler is found — prompts the user to locate one manually or download
/// MinGW-w64. When the loader is dismissed the console is cleared and the
/// welcome banner takes its place.
/// </summary>
public sealed record StartupResult(
    bool Ready,
    bool Provisioned,
    string? CompilerPath,
    string? CompilerKind);

public static class StartupChecks
{
    public static async Task<StartupResult> RunAsync(IAppPaths paths, IAppLogger logger)
    {
        var live = new LoadingScreen();
        live.AddStep("provision", "Ensuring external requirements (Bin2Shell)");
        live.AddStep("compiler", "Locating a C/C++ compiler");
        live.Render();

        // ── 1) Provision Bin2Shell ───────────────────────────────
        bool provisioned;
        try
        {
            live.Update("provision", StepState.Running, "Checking Bin2Shell toolchain…");
            var provisioner = new RequirementProvisioner(paths, logger);
            var reporter = new LoadingScreenProgressReporter(live, "provision");
            await provisioner.EnsureRequirementsAsync(reporter);
            live.Update("provision", StepState.Ok, "Requirements ready.");
            provisioned = true;
        }
        catch (Exception ex)
        {
            live.Update("provision", StepState.Warning, $"Provisioning failed: {ex.Message}");
            provisioned = false;
        }

        // ── 2) Locate a compiler ─────────────────────────────────
        live.Update("compiler", StepState.Running, "Scanning PATH, Visual Studio, bundled toolchains…");
        var locator = new CompilerToolLocator(logger);
        var discovery = await locator.DiscoverAsync();

        string? compilerPath = discovery.Best?.Path;
        string? compilerKind = discovery.Best?.Kind;

        if (!string.IsNullOrEmpty(compilerPath))
        {
            live.Update("compiler", StepState.Ok,
                $"{compilerKind} → {Trim(compilerPath!, 70)}");
        }
        else
        {
            live.Update("compiler", StepState.Warning, "No C/C++ compiler found on this machine.");
        }

        live.Finish();

        // ── 3) Prompt if compiler is missing ─────────────────────
        if (string.IsNullOrEmpty(compilerPath))
        {
            var resolved = await PromptForCompilerAsync(locator, paths, logger);
            if (resolved != null)
            {
                compilerPath = resolved.Path;
                compilerKind = resolved.Kind;
            }
        }

        // Clear the transient loader and hand control back to the caller.
        try { AnsiConsole.Clear(); } catch { /* non-interactive */ }

        return new StartupResult(
            Ready: !string.IsNullOrEmpty(compilerPath),
            Provisioned: provisioned,
            CompilerPath: compilerPath,
            CompilerKind: compilerKind);
    }

    private static async Task<Washmachine.Models.CompilerToolCandidate?> PromptForCompilerAsync(
        ICompilerToolLocator locator, IAppPaths paths, IAppLogger logger)
    {
        var s = UiColors.ActiveScheme;
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
                new Markup(
                    $"[{s.Warning}]No C/C++ compiler was detected.[/]\n" +
                    $"[{s.Muted}]Compilation requires cl.exe (MSVC), clang++.exe, or g++.exe.[/]"))
            .Header($"[bold {s.Header}] Compiler required [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(UiColors.BoxBorderColor)
            .Padding(1, 0));

        AnsiConsole.WriteLine();

        string choice;
        try
        {
            choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[{s.Value}]How would you like to proceed?[/]")
                    .HighlightStyle(new Style(foreground: UiColors.ParseHex(s.Accent), decoration: Decoration.Bold))
                    .AddChoices(new[]
                    {
                        "Locate an existing compiler on disk",
                        "Download MinGW-w64 for later use (~60 MB)",
                        "Skip for now",
                    }));
        }
        catch (Exception)
        {
            // Non-interactive terminal — skip silently.
            return null;
        }

        if (choice.StartsWith("Locate", StringComparison.Ordinal))
        {
            var path = AnsiConsole.Prompt(
                new TextPrompt<string>($"[{s.Value}]Path to cl.exe / clang++.exe / g++.exe:[/]")
                    .PromptStyle(s.Accent)
                    .Validate(p => File.Exists(p.Trim('"'))
                        ? ValidationResult.Success()
                        : ValidationResult.Error($"[{s.Error}]File not found.[/]")));

            try
            {
                var result = await locator.AddManualCandidateAsync(path.Trim('"'));
                return result.Best;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{s.Error}]Could not register compiler: {Markup.Escape(ex.Message)}[/]");
                return null;
            }
        }

        if (choice.StartsWith("Download", StringComparison.Ordinal))
        {
            return await DownloadMingwAsync(locator, paths, logger);
        }

        return null;
    }

    private static async Task<Washmachine.Models.CompilerToolCandidate?> DownloadMingwAsync(
        ICompilerToolLocator locator, IAppPaths paths, IAppLogger logger)
    {
        var downloader = new MingwDownloader(logger, paths);
        var scheme = UiColors.ActiveScheme;

        bool ok = await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new DownloadedColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[yellow]Downloading MinGW-w64[/]");
                task.IsIndeterminate = true;

                return await downloader.DownloadAndExtractAsync((msg, pct) =>
                {
                    task.Description = msg;
                    if (pct >= 0)
                    {
                        task.IsIndeterminate = false;
                        task.Value = pct;
                    }
                    else
                    {
                        task.IsIndeterminate = true;
                    }
                });
            });

        if (!ok)
        {
            AnsiConsole.MarkupLine($"[{scheme.Error}]MinGW download or extraction failed. You can retry with[/] [{scheme.Accent}]provision[/].");
            return null;
        }

        // Re-scan now that a bundled toolchain exists under Tools/.
        var redetected = await locator.DiscoverAsync();
        if (redetected.Best == null)
        {
            AnsiConsole.MarkupLine($"[{scheme.Warning}]Downloaded MinGW but auto-detection missed it. Try the 'Locate' option after extraction.[/]");
            return null;
        }

        AnsiConsole.MarkupLine(
            $"[{scheme.Success}]✓ MinGW ready:[/] [{scheme.Muted}]{Markup.Escape(redetected.Best.Path)}[/]");
        return redetected.Best;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : "…" + s[^(max - 1)..];

    // ─────────────────────────────────────────────────────────────
    // Live "requirements loading" screen implementation.
    // ─────────────────────────────────────────────────────────────

    private enum StepState { Pending, Running, Ok, Warning, Error }

    private sealed class LoadingStep
    {
        public string Id = "";
        public string Label = "";
        public StepState State = StepState.Pending;
        public string Detail = "";
    }

    private sealed class LoadingScreen
    {
        private readonly List<LoadingStep> _steps = new();
        private readonly object _gate = new();
        private bool _finalFrame;
        private int _frame;

        public void AddStep(string id, string label)
        {
            _steps.Add(new LoadingStep { Id = id, Label = label });
        }

        public void Update(string id, StepState state, string detail)
        {
            lock (_gate)
            {
                var step = _steps.FirstOrDefault(s => s.Id == id);
                if (step == null) return;
                step.State = state;
                step.Detail = detail;
            }
            Render();
        }

        public void Render()
        {
            lock (_gate)
            {
                try
                {
                    AnsiConsole.Clear();
                }
                catch { /* redirected */ }

                AnsiConsole.WriteLine();
                var s = UiColors.ActiveScheme;

                var grid = new Grid()
                    .AddColumn(new GridColumn().NoWrap().Width(3))
                    .AddColumn(new GridColumn());

                foreach (var step in _steps)
                {
                    var icon = StateIcon(step.State, _frame);
                    var color = StateColor(step.State);
                    var label = Markup.Escape(step.Label);
                    var detail = string.IsNullOrWhiteSpace(step.Detail)
                        ? ""
                        : $"  [{s.Muted}]{Markup.Escape(step.Detail)}[/]";

                    grid.AddRow(
                        $"[{color}]{icon}[/]",
                        $"[{s.Value}]{label}[/]{detail}");
                }

                var panel = new Panel(grid)
                    .Header($"[bold {s.Header}] Washmachine · starting up [/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(UiColors.BoxBorderColor)
                    .Padding(2, 1);

                AnsiConsole.Write(panel);

                if (!_finalFrame)
                {
                    AnsiConsole.MarkupLine(
                        $"  [{s.Muted}]This takes a moment the first time — downloads cache under Tools/.[/]");
                }

                _frame++;
            }
        }

        public void Finish()
        {
            lock (_gate)
            {
                _finalFrame = true;
            }
        }

        private static string StateIcon(StepState state, int frame) => state switch
        {
            StepState.Pending => "·",
            StepState.Running => "◐◓◑◒"[frame % 4].ToString(),
            StepState.Ok      => "✓",
            StepState.Warning => "!",
            StepState.Error   => "✗",
            _                 => " "
        };

        private static string StateColor(StepState state)
        {
            var sch = UiColors.ActiveScheme;
            return state switch
            {
                StepState.Pending => sch.Muted,
                StepState.Running => sch.Accent,
                StepState.Ok      => sch.Success,
                StepState.Warning => sch.Warning,
                StepState.Error   => sch.Error,
                _                 => sch.Value
            };
        }
    }

    private sealed class LoadingScreenProgressReporter : IProgressReporter
    {
        private readonly LoadingScreen _screen;
        private readonly string _stepId;

        public LoadingScreenProgressReporter(LoadingScreen screen, string stepId)
        {
            _screen = screen;
            _stepId = stepId;
        }

        public void UpdateStatus(string message, int percentComplete)
        {
            var detail = percentComplete >= 0
                ? $"{message} ({percentComplete}%)"
                : message;
            _screen.Update(_stepId, StepState.Running, detail);
        }

        public void Close()
        {
            // The caller decides the final state (Ok/Warning).
        }
    }
}
