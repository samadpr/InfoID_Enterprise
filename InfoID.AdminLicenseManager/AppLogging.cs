using System.Diagnostics;
using InfoID.Shared.Logging;
using Microsoft.Extensions.Logging;

namespace InfoID.AdminLicenseManager;

/// <summary>
/// License Manager's technical log: %LocalAppData%\InfoID License Manager\Logs\
/// InfoID.LicenseManager-yyyy-MM-dd.log, 30 days kept (same shared logger as InfoID and the
/// License API). Separate from the tamper-evident audit log, which records what staff did;
/// this file records errors for troubleshooting. Never logged: passwords, activation codes,
/// serial keys, the license secret or the database password.
/// </summary>
public static class AppLogging
{
    public static string LogsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InfoID License Manager", "Logs");

    public static FileLoggerProvider? Provider { get; private set; }
    public static ILoggerFactory Factory { get; private set; } = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

    /// <summary>Starts file logging; safe to call once at startup.</summary>
    public static void Start()
    {
        if (Provider is not null) return;
        Provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = LogsDirectory,
            FilePrefix = "InfoID.LicenseManager",
            RetainDays = 30,
            WriteToDebugOutput = Debugger.IsAttached,
        });
        Factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(Debugger.IsAttached ? LogLevel.Debug : LogLevel.Information);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
            builder.AddProvider(Provider);
        });
        AppLog.Configure(Factory);

        var version = typeof(AppLogging).Assembly.GetName().Version;
        AppLog.For("InfoID.LicenseManager").Info(
            $"License Manager {version} starting on {Environment.MachineName}. OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}, .NET {Environment.Version}.");
    }

    /// <summary>Writes anything still queued to the file (call on exit). Safe to call twice.</summary>
    public static void Flush()
    {
        try { Provider?.Dispose(); } catch { /* never throw on exit */ }
    }
}
