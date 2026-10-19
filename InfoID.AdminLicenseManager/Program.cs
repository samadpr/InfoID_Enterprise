using Avalonia;

namespace InfoID.AdminLicenseManager;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Setup helper: `InfoID.AdminLicenseManager --new-secret` prints a fresh random
        // license secret to put in INFOID_LICENSE_SECRET_V1 (see README).
        if (args.Contains("--new-secret"))
        {
            Console.WriteLine(InfoID.Licensing.Core.LicenseSecret.GenerateBase64());
            return;
        }

        AppLogging.Start();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            InfoID.Shared.Logging.AppLog.For("InfoID.LicenseManager.Crash")
                .Critical("Unhandled exception; License Manager is closing", e.ExceptionObject as Exception);
            AppLogging.Flush();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            InfoID.Shared.Logging.AppLog.For("InfoID.LicenseManager.Tasks").Error("Unobserved exception in a background task", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            InfoID.Shared.Logging.AppLog.For("InfoID.LicenseManager.Crash").Critical("License Manager failed to start or stopped unexpectedly", ex);
            throw;
        }
        finally
        {
            AppLogging.Flush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
