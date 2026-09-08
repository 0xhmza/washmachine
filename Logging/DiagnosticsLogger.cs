namespace Washmachine.Logging;

/// <summary>
/// GUI logger that routes to System.Diagnostics.Debug (visible only in debugger).
/// </summary>
public sealed class DiagnosticsLogger : IAppLogger
{
    public void Info(string message)  => System.Diagnostics.Debug.WriteLine($"[INFO] {message}");
    public void Ok(string message)    => System.Diagnostics.Debug.WriteLine($"[OK] {message}");
    public void Warn(string message)  => System.Diagnostics.Debug.WriteLine($"[WARN] {message}");
    public void Error(string message) => System.Diagnostics.Debug.WriteLine($"[ERROR] {message}");
    public void Debug(string message) => System.Diagnostics.Debug.WriteLine($"[DEBUG] {message}");
}
