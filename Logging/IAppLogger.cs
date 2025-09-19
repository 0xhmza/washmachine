namespace Washmachine.Logging;

public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Ok(string message);
}
