namespace Washmachine.Services;

/// <summary>
/// Reports progress for long-running operations (downloads, installations).
/// Implemented by the GUI layer (window-based) or CLI layer (console-based).
/// </summary>
public interface IProgressReporter
{
    void UpdateStatus(string message, int percentComplete);
    void Close();
}

/// <summary>
/// Console-based progress reporter for headless/CLI scenarios.
/// </summary>
public sealed class ConsoleProgressReporter : IProgressReporter
{
    private int _lastPercent = -1;

    public void UpdateStatus(string message, int percentComplete)
    {
        if (percentComplete != _lastPercent)
        {
            Console.WriteLine($"[{percentComplete,3}%] {message}");
            _lastPercent = percentComplete;
        }
    }

    public void Close() { }
}
