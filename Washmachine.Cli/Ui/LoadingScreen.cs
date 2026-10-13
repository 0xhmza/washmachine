using Spectre.Console;

namespace Washmachine.Cli.Ui;

internal enum StepState { Pending, Running, Ok, Warning, Error }

/// <summary>Forward-only startup status. No cursor manipulation or animated repainting.</summary>
internal sealed class LoadingScreen(IAnsiConsole? console = null)
{
    private sealed record Step(string Id, string Label, StepState State = StepState.Pending, string Detail = "");
    private readonly List<Step> _steps = new();
    private readonly object _gate = new();
    private bool _rendered;
    public void AddStep(string id, string label) { lock (_gate) _steps.Add(new(id, label)); }
    public void Render()
    {
        lock (_gate)
        {
            if (_rendered) return;
            _rendered = true;
            new HelpWriter(console ?? UsageFormatter.DefaultConsole()).Heading("Starting up");
            foreach (var step in _steps) Write(step);
        }
    }
    public void Update(string id, StepState state, string detail)
    {
        lock (_gate)
        {
            int index = _steps.FindIndex(step => step.Id == id);
            if (index < 0) return;
            var next = _steps[index] with { State = state, Detail = detail };
            if (next == _steps[index]) return;
            _steps[index] = next;
            if (_rendered) Write(next);
        }
    }
    public void Finish() { (console ?? UsageFormatter.DefaultConsole()).WriteLine(); }
    private void Write(Step step) => CliConsole.StatusLine(step.State switch
    {
        StepState.Pending => StatusKind.Pending, StepState.Running => StatusKind.Working,
        StepState.Ok => StatusKind.Success, StepState.Warning => StatusKind.Warning, _ => StatusKind.Error
    }, string.IsNullOrWhiteSpace(step.Detail) ? step.Label : $"{step.Label}: {step.Detail}", console);
}
