using Washmachine.Services;
using Washmachine.Views;

namespace Washmachine.Services;

/// <summary>
/// GUI progress reporter that wraps RequirementsProgressWindow.
/// </summary>
public sealed class WindowProgressReporter : IProgressReporter
{
    private readonly RequirementsProgressWindow _window;

    public WindowProgressReporter()
    {
        _window = new RequirementsProgressWindow();
        _window.Show();
    }

    public void UpdateStatus(string message, int percentComplete)
        => _window.UpdateStatus(message, percentComplete);

    public void Close()
        => _window.Close();
}
