using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using InfoID.AdminLicenseManager.ViewModels;
using InfoID.AdminLicenseManager.Views;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.AdminLicenseManager;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = AppServices.Build(AppLogging.Factory);
        HookUiThreadErrors();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<App>();
    private static DateTime _lastErrorWindow = DateTime.MinValue;

    /// <summary>An exception escaping a button or page would close the License Manager.
    /// Instead it is logged with its stack trace, staff see a short message (at most one
    /// every 10 seconds) and the app keeps running.</summary>
    private static void HookUiThreadErrors()
    {
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled error on the UI thread", e.Exception);
            e.Handled = true;
            if (DateTime.Now - _lastErrorWindow < TimeSpan.FromSeconds(10)) return;
            _lastErrorWindow = DateTime.Now;
            try
            {
                var window = new Avalonia.Controls.Window
                {
                    Title = "License Manager",
                    Width = 460,
                    SizeToContent = Avalonia.Controls.SizeToContent.Height,
                    CanResize = false,
                    WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                    Content = new Avalonia.Controls.TextBlock
                    {
                        Margin = new Avalonia.Thickness(20),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Text = "Something went wrong, but License Manager is still running.\n\n" +
                               $"Details: {e.Exception.GetBaseException().Message}\n\n" +
                               "The full details are in the log (Open log folder, bottom left).",
                    },
                };
                if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
                    _ = window.ShowDialog(owner);
                else
                    window.Show();
            }
            catch
            {
                // The message window must not cause another error.
            }
        };
    }
}
