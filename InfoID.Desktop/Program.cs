using Avalonia;
using System;

namespace InfoID.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Last-chance handlers: whatever escapes is written to the log before InfoID closes.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            InfoID.Shared.Logging.AppLog.For("InfoID.Crash")
                .Critical("Unhandled exception; InfoID is closing", e.ExceptionObject as Exception);
            App.FlushLogs();
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            InfoID.Shared.Logging.AppLog.For("InfoID.Tasks")
                .Error("Unobserved exception in a background task", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            InfoID.Shared.Logging.AppLog.For("InfoID.Crash").Critical("InfoID failed to start or stopped unexpectedly", ex);
            App.FlushLogs();
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
