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

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
