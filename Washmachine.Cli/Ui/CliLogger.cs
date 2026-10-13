using Washmachine.Logging;

namespace Washmachine.Cli.Ui;

/// <summary>Human-readable CLI logs using the same labels, wrapping and stream policy.</summary>
internal sealed class CliLogger : IAppLogger
{
    public bool VerboseEnabled { get; set; }
    public bool SuppressOutput { get; set; }
    public void Info(string message) => Write(StatusKind.Info, message);
    public void Ok(string message) => Write(StatusKind.Success, message);
    public void Warn(string message) => Write(StatusKind.Warning, message);
    public void Error(string message) => Write(StatusKind.Error, message);
    public void Debug(string message) { if (VerboseEnabled) Write(StatusKind.Info, message); }
    private void Write(StatusKind kind, string message) { if (!SuppressOutput) CliConsole.StatusLine(kind, message); }
}
