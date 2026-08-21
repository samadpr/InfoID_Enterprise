using System;
using System.IO;

namespace InfoID.Infrastructure.Persistence;

/// <summary>
/// Appends timestamped lines to %AppData%\InfoID\Logs\startup.log. Never throws --
/// logging must not be able to crash startup, so every write is best-effort.
/// </summary>
public sealed class FileStartupLogger : IStartupLogger
{
    private readonly string _logFilePath;
    private readonly object _sync = new();

    public FileStartupLogger()
    {
        _logFilePath = Path.Combine(DatabaseLocation.GetLogsDirectory(), "startup.log");
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} :: {exception}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_sync)
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, line);
            }
        }
        catch
        {
            // Logging must never take the app down. Swallow deliberately.
        }
    }
}