namespace Washmachine.Logging;

/// <summary>
/// Application logger contract. <see cref="Info"/>, <see cref="Warn"/>,
/// <see cref="Error"/>, and <see cref="Ok"/> are user-visible.
/// <see cref="Debug"/> is only shown when verbose logging is enabled.
/// </summary>
public interface IAppLogger
{
    /// <summary>User-facing informational message.</summary>
    void Info(string message);

    /// <summary>User-facing warning (non-fatal edge case).</summary>
    void Warn(string message);

    /// <summary>User-facing error.</summary>
    void Error(string message);

    /// <summary>User-facing success confirmation.</summary>
    void Ok(string message);

    /// <summary>Internal diagnostic detail – hidden from the UI log by default.</summary>
    void Debug(string message) { } // default no-op for backward compat
}
