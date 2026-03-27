namespace Washmachine.Services;

/// <summary>
/// Reports progress for long-running operations (downloads, installations).
/// Pass percentComplete = -1 to switch the reporter into indeterminate (spinner) mode.
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
    private string _lastMessage = string.Empty;

    public void UpdateStatus(string message, int percentComplete)
    {
        if (message == _lastMessage)
            return;

        _lastMessage = message;
        string prefix = percentComplete < 0 ? "[   ] " : $"[{percentComplete,3}%] ";
        Console.WriteLine($"{prefix}{message}");
    }

    public void Close() { }
}
