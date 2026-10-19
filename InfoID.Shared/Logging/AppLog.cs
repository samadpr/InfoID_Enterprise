using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfoID.Shared.Logging;

/// <summary>
/// Static access to logging for classes that aren't created by dependency injection
/// (view models built with <c>new</c>, static stores like PrintSettingsStore). DI-created
/// classes can take <c>ILogger&lt;T&gt;</c> in their constructor instead; both write to
/// the same log.
///
/// Usage:
///   private static readonly AppLogger Log = AppLog.For&lt;MyClass&gt;();
///   Log.Error("Print history couldn't be saved", ex);
///
/// Safe before startup has configured logging: messages are then discarded rather than
/// throwing, and loggers created early start writing once <see cref="Configure"/> runs.
/// </summary>
public static class AppLog
{
    private static ILoggerFactory? _factory;

    /// <summary>Called once at startup with the app's logger factory.</summary>
    public static void Configure(ILoggerFactory factory) => _factory = factory;

    internal static ILoggerFactory Factory => _factory ?? NullLoggerFactory.Instance;
    internal static bool IsConfigured => _factory is not null;

    public static AppLogger For<T>() => new(typeof(T).FullName ?? typeof(T).Name);

    public static AppLogger For(string category) => new(category);
}

/// <summary>
/// A logger handle with plain-text methods. The message is written exactly as given (it
/// is NOT treated as a message template), so text containing { } -- file paths, JSON,
/// exception text -- can never cause a formatting error.
/// </summary>
public sealed class AppLogger
{
    private readonly string _category;
    private ILogger? _logger;

    internal AppLogger(string category) => _category = category;

    private ILogger Logger
    {
        get
        {
            if (_logger is not null) return _logger;
            var logger = AppLog.Factory.CreateLogger(_category);
            // Cache only the real logger; before Configure() keep asking.
            if (AppLog.IsConfigured) _logger = logger;
            return logger;
        }
    }

    public void Debug(string message, Exception? exception = null) => Write(LogLevel.Debug, message, exception);
    public void Info(string message, Exception? exception = null) => Write(LogLevel.Information, message, exception);
    public void Warn(string message, Exception? exception = null) => Write(LogLevel.Warning, message, exception);
    public void Error(string message, Exception? exception = null) => Write(LogLevel.Error, message, exception);
    public void Critical(string message, Exception? exception = null) => Write(LogLevel.Critical, message, exception);

    private void Write(LogLevel level, string message, Exception? exception)
    {
        try
        {
            var logger = Logger;
            if (logger.IsEnabled(level))
                logger.Log(level, default, message, exception, static (text, _) => text);
        }
        catch
        {
            // Logging must never throw into the caller.
        }
    }
}
