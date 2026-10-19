using System.Diagnostics;
using InfoID.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using InfoID.Shared.Logging;

namespace InfoID.Infrastructure.Logging;

public static class LoggingSetup
{
    /// <summary>
    /// Registers Microsoft.Extensions.Logging with InfoID's daily file log
    /// (Logs\{filePrefix}-yyyy-MM-dd.log, 30 days kept). Information and above are
    /// written; Debug too while a debugger is attached. Framework noise (EF Core SQL,
    /// Microsoft.*, System.*) is limited to warnings, and EF never logs parameter
    /// values, so cardholder data doesn't end up in the log.
    /// Returns the provider so the app can flush it on exit (Dispose).
    /// </summary>
    public static FileLoggerProvider AddInfoIdFileLogging(this IServiceCollection services, string filePrefix = "InfoID")
    {
        var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = DatabaseLocation.GetLogsDirectory(),
            FilePrefix = filePrefix,
            RetainDays = 30,
            WriteToDebugOutput = Debugger.IsAttached,
        });

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(Debugger.IsAttached ? LogLevel.Debug : LogLevel.Information);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
            builder.AddProvider(provider);
        });
        return provider;
    }
}
