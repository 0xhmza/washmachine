namespace Washmachine.Logging;

/// <summary>
/// Logger that writes every message to both a wrapped <see cref="IAppLogger"/>
/// (typically the console or UI logger) and a plain-text log file on disk.
/// Disposing this logger flushes and closes the underlying file stream.
/// </summary>
public sealed class TeeLogger : IAppLogger, IDisposable
{
    private readonly IAppLogger _inner;
    private readonly StreamWriter _file;

    public TeeLogger(IAppLogger inner, string logFilePath)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        var dir = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        _file = new StreamWriter(logFilePath, append: true) { AutoFlush = true };
    }

    public void Info(string message)
    {
        _inner.Info(message);
        Write("INFO", message);
    }

    public void Warn(string message)
    {
        _inner.Warn(message);
        Write("WARN", message);
    }

    public void Error(string message)
    {
        _inner.Error(message);
        Write("ERROR", message);
    }

    public void Ok(string message)
    {
        _inner.Ok(message);
        Write("OK", message);
    }

    public void Debug(string message)
    {
        _inner.Debug(message);
        Write("DEBUG", message);
    }

    /// <summary>Write a raw line to the log file only (no console output).</summary>
    public void FileOnly(string message)
    {
        try { _file.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}"); }
        catch { /* swallow – never crash on logging */ }
    }

    /// <summary>Write a block of text to the log file only (no console output).</summary>
    public void FileOnlyBlock(string text)
    {
        try { _file.Write(text); _file.Flush(); }
        catch { /* swallow */ }
    }

    private void Write(string level, string message)
    {
        try { _file.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level,-5}] {message}"); }
        catch { /* swallow – never crash on logging */ }
    }

    public void Dispose()
    {
        try { _file.Flush(); _file.Dispose(); }
        catch { /* swallow */ }
    }
}
