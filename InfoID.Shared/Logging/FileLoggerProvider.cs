using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace InfoID.Shared.Logging;

/// <summary>Settings for <see cref="FileLoggerProvider"/>.</summary>
public sealed class FileLoggerOptions
{
    /// <summary>Folder for the log files (InfoID's Logs folder by default).</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>File name prefix: "InfoID" gives InfoID-2026-10-09.log.</summary>
    public string FilePrefix { get; set; } = "InfoID";

    /// <summary>Log files older than this are deleted at startup.</summary>
    public int RetainDays { get; set; } = 30;

    /// <summary>A single day's file is capped; past this, new lines go to a "-2", "-3"... file.</summary>
    public long MaxFileBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Also echo every line to Visual Studio's Output window (debug builds).</summary>
    public bool WriteToDebugOutput { get; set; } = true;
}

/// <summary>
/// A small, dependency-free file logger for Microsoft.Extensions.Logging: one file per
/// day (Logs\InfoID-yyyy-MM-dd.log), 30 days kept, written on a background thread so
/// logging never slows the UI or printing. Logging must never break the application,
/// so every I/O failure here is swallowed.
///
/// Line format:
///   2026-10-09 12:04:31.118 +05:30 [ERR] InfoID.Desktop.Features.Printing...: message
///       System.IO.IOException: ... (full exception, indented)
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLoggerOptions _options;
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>(), 10_000);
    private readonly Thread _writer;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private bool _disposed;

    public FileLoggerProvider(FileLoggerOptions options)
    {
        _options = options;
        try
        {
            System.IO.Directory.CreateDirectory(_options.Directory);
            DeleteOldFiles();
        }
        catch
        {
            // No log folder means no file logging; the app still runs.
        }

        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "InfoID log writer" };
        _writer.Start();
    }

    /// <summary>Path of today's log file (for "Open log folder" and support).</summary>
    public string CurrentFilePath => Path.Combine(_options.Directory, $"{_options.FilePrefix}-{DateTime.Now:yyyy-MM-dd}.log");

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    internal void Enqueue(string line)
    {
        if (_options.WriteToDebugOutput) System.Diagnostics.Debug.Write(line);
        if (_disposed) return;
        // TryAdd: if the queue is full (logging storm) drop the line instead of blocking the caller.
        try { _queue.TryAdd(line); } catch (InvalidOperationException) { }
    }

    private void WriteLoop()
    {
        var buffer = new StringBuilder();
        try
        {
            foreach (var line in _queue.GetConsumingEnumerable())
            {
                buffer.Append(line);
                // Batch whatever else is already waiting into one file write.
                while (buffer.Length < 64 * 1024 && _queue.TryTake(out var next)) buffer.Append(next);
                Flush(buffer);
            }
        }
        catch
        {
            // Never let the writer thread crash the process.
        }
        finally
        {
            Flush(buffer);
        }
    }

    private void Flush(StringBuilder buffer)
    {
        if (buffer.Length == 0) return;
        try
        {
            File.AppendAllText(PathForWrite(), buffer.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Disk full, file locked, folder removed: drop this batch.
        }
        buffer.Clear();
    }

    /// <summary>Today's file, or today's "-n" overflow file once the size cap is reached.</summary>
    private string PathForWrite()
    {
        var path = CurrentFilePath;
        for (var n = 2; n < 100; n++)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < _options.MaxFileBytes) return path;
            path = Path.Combine(_options.Directory, $"{_options.FilePrefix}-{DateTime.Now:yyyy-MM-dd}-{n}.log");
        }
        return path;
    }

    private void DeleteOldFiles()
    {
        var cutoff = DateTime.Now.Date.AddDays(-Math.Max(1, _options.RetainDays));
        foreach (var file in System.IO.Directory.EnumerateFiles(_options.Directory, _options.FilePrefix + "-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
            catch
            {
                // In use or no permission: try again next start.
            }
        }
    }

    /// <summary>Writes everything still queued, then stops the writer (call on app exit).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(3));
        _queue.Dispose();
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly FileLoggerProvider _provider;

        public FileLogger(string category, FileLoggerProvider provider)
        {
            _category = category;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string message;
            try { message = formatter(state, exception); }
            catch { message = state?.ToString() ?? string.Empty; }

            var sb = new StringBuilder(128);
            sb.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
              .Append(" [").Append(Level(logLevel)).Append("] ")
              .Append(_category).Append(": ")
              .Append(message)
              .AppendLine();
            if (exception is not null)
            {
                foreach (var line in exception.ToString().Split('\n'))
                    sb.Append("    ").Append(line.TrimEnd('\r')).AppendLine();
            }
            _provider.Enqueue(sb.ToString());
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }
}
