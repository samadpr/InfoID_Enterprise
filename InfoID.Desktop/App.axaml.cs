using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using InfoID.Application;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Core.Services;
using InfoID.Desktop.Features.Backup.Services;
using InfoID.Desktop.Features.BlankCard.Services;
using InfoID.Desktop.Features.BlankCard.ViewModels;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.Features.Licensing.Services;
using InfoID.Desktop.Features.Licensing.ViewModels;
using InfoID.Desktop.Features.Printing.Services;
using InfoID.Desktop.Features.Templates.Services;
using InfoID.Desktop.Features.Templates.ViewModels;
using InfoID.Desktop.Features.Welcome.Services;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.Shell.ViewModels;
using InfoID.Desktop.Shell.Views;
using InfoID.Infrastructure;
using InfoID.Infrastructure.Logging;
using InfoID.Shared.Logging;
using InfoID.Infrastructure.Persistence;
using InfoID.Infrastructure.Backup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InfoID.Desktop;

public partial class App : Avalonia.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        // Logging first, so everything registered after it (EF Core included) uses it.
        LogProvider = services.AddInfoIdFileLogging("InfoID");
        var backupOptions = BackupSetup.CreateOptions();
        services.AddSingleton(backupOptions);
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
        AppLog.Configure(Services.GetRequiredService<ILoggerFactory>());
        Audit.Configure(Services);
        LogStartup();
        HookUiThreadErrors();

        // Backup & Restore: a restore prepared before the last restart is applied now,
        // before anything opens the database or reads a settings file. (When restarted
        // for this, first wait for the previous instance to close its files.)
        AppRestart.WaitForPreviousInstance(Environment.GetCommandLineArgs());
        var restore = RestoreStartup.ApplyPending(backupOptions);

        // Database must be ready before anything else touches it -- this runs the
        // Migrate() + idempotent seed described in DatabaseInitializer, synchronously,
        // before any window is created. On a fresh/deleted database this is what
        // actually (re)creates %AppData%\InfoID\Database\InfoID_DB.db and its tables.
        // After a restore this also upgrades the restored database to this version.
        var initResult = RunDatabaseInitialization();
        initResult = FinishRestore(restore, initResult);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!initResult.Success)
            {
                desktop.MainWindow = BuildDatabaseErrorWindow(initResult);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            Services.GetRequiredService<IThemeService>().InitializeFromSystem();

            // Audit trail (FR-REP-4): session start/end and account sign-in/out.
            Audit.Record(InfoID.Domain.Common.Interfaces.AuditModules.Application, "Started",
                $"InfoID {typeof(App).Assembly.GetName().Version?.ToString(3)} started");
            WatchAccountForAudit(Services.GetRequiredService<InfoID.Desktop.Features.Account.Services.AccountSession>());

            desktop.Exit += (_, e) =>
            {
                // Rows still being typed in a design's data grid (Internal Database).
                try { Services.GetService<CardDesignerViewModel>()?.SavePendingDatabaseRows(); }
                catch (Exception ex) { Log.Warn("Saving data-grid rows on exit failed", ex); }

                Log.Info($"InfoID closed (exit code {e.ApplicationExitCode}).");
                // Written before the process ends (bounded, so closing never hangs).
                // Held design changes (audit 15-minute grouping) go first, then "Closed".
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await InfoID.Desktop.Features.CardDesigner.Services.DesignEditAudit.FlushAsync();
                    await Audit.RecordAsync(new InfoID.Domain.Common.Interfaces.AuditEntry(
                        InfoID.Domain.Common.Interfaces.AuditModules.Application, "Closed", "InfoID closed"));
                }).Wait(TimeSpan.FromSeconds(5));
                FlushLogs();
            };

            // Created before any window so the designer canvas can read the trial mode.
            Services.GetRequiredService<InfoID.Desktop.Features.Trial.Services.TrialModeState>();
            var shell = Services.GetRequiredService<ShellView>();
            shell.DataContext = Services.GetRequiredService<ShellViewModel>();
            desktop.MainWindow = shell;

            var scheduler = Services.GetRequiredService<AutoBackupScheduler>();
            scheduler.Start();
            desktop.Exit += (_, _) => scheduler.Dispose();

            if (restore.Attempted)
                shell.Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = ShowRestoreOutcomeAsync(restore));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static readonly AppLogger Log = AppLog.For("InfoID.App");

    /// <summary>The daily file log (Logs\InfoID-yyyy-MM-dd.log); flushed on exit.</summary>
    public static FileLoggerProvider? LogProvider { get; private set; }

    /// <summary>Writes anything still queued to the log file. Safe to call more than once.</summary>
    public static void FlushLogs()
    {
        try { LogProvider?.Dispose(); } catch { /* never throw on exit */ }
    }

    private static void LogStartup()
    {
        var assembly = typeof(App).Assembly.GetName();
        Log.Info($"InfoID {assembly.Version} starting. OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} " +
                 $"({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}), .NET {Environment.Version}, " +
                 $"data folder: {DatabaseLocation.GetApplicationRoot()}");
    }

    private static DateTime _lastErrorDialog = DateTime.MinValue;

    /// <summary>
    /// An exception escaping a UI event handler or command would normally close InfoID and
    /// lose unsaved work. Instead it is logged with its full stack trace, the user is told
    /// (at most one message every 10 seconds, so a repeating error can't flood the screen)
    /// and InfoID keeps running.
    /// </summary>
    private static void HookUiThreadErrors()
    {
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled error on the UI thread", e.Exception);
            e.Handled = true;

            if (DateTime.Now - _lastErrorDialog < TimeSpan.FromSeconds(10)) return;
            _lastErrorDialog = DateTime.Now;
            try
            {
                if (Services.GetService<IDialogService>() is not { } dialogs) return;
                _ = dialogs.ShowDialogAsync<MessageDialogViewModel, bool>(new MessageDialogViewModel(
                    "Something went wrong",
                    "InfoID hit an unexpected error but is still running. Save your work.\n\n" +
                    $"Details: {e.Exception.GetBaseException().Message}\n\n" +
                    "The full details are in the log (Help > Open log folder).",
                    isError: true));
            }
            catch
            {
                // The error dialog itself must not cause another error.
            }
        };
    }

    private static void WatchAccountForAudit(InfoID.Desktop.Features.Account.Services.AccountSession account)
    {
        var signedInAs = account.IsSignedIn ? account.Profile?.Email : null;
        account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(account.IsSignedIn) && e.PropertyName != nameof(account.Profile)) return;
            var now = account.IsSignedIn ? account.Profile?.Email : null;
            if (account.IsSignedIn && now is not null && !string.Equals(now, signedInAs, StringComparison.OrdinalIgnoreCase))
                Audit.Record(InfoID.Domain.Common.Interfaces.AuditModules.Account, "SignedIn", $"Signed in to InfoID account {now}");
            else if (!account.IsSignedIn && signedInAs is not null)
                Audit.Record(InfoID.Domain.Common.Interfaces.AuditModules.Account, "SignedOut", $"Signed out of InfoID account {signedInAs}");
            else return;
            signedInAs = now;
        };
    }

    /// <summary>
    /// After a restore was applied: if the restored database was upgraded fine, keep this
    /// PC's license and record the restore; if not, put the previous data back and
    /// initialize that instead. Returns the initialization result to continue with.
    /// </summary>
    private static DatabaseInitializationResult FinishRestore(RestoreStartup.Outcome restore, DatabaseInitializationResult initResult)
    {
        if (!restore.Attempted) return initResult;

        if (restore.Applied && !initResult.Success)
        {
            RestoreStartup.Rollback(restore, $"The restored database couldn't be opened or upgraded ({initResult.ErrorMessage}).");
            initResult = RunDatabaseInitialization();
        }

        if (!initResult.Success) return initResult;

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InfoIdDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<InfoID.Domain.Common.Interfaces.IAuditTrail>();
        var file = restore.Pending?.SourceFile;
        if (restore.Succeeded)
        {
            RestoreStartup.Complete(restore, context);
            // First entry after a restore; it also re-anchors the audit chain to the
            // restored log, so the restore isn't mistaken for removed entries.
            audit.RecordAsync(new InfoID.Domain.Common.Interfaces.AuditEntry(InfoID.Domain.Common.Interfaces.AuditModules.Backup, "Restored",
                $"Data restored from {System.IO.Path.GetFileName(file)} (backup made {restore.Pending?.BackupCreatedUtc.ToLocalTime():dd MMM yyyy HH:mm})")
            {
                EntityName = "Backup",
                After = new
                {
                    File = file,
                    BackupCreatedUtc = restore.Pending?.BackupCreatedUtc,
                    BackupAppVersion = restore.Pending?.BackupAppVersion,
                    SafetyBackup = restore.Pending?.SafetyBackupPath,
                    restore.Warning,
                },
            }).GetAwaiter().GetResult();
        }
        else
        {
            RestoreStartup.RecordFailure(restore, context);
            audit.RecordAsync(new InfoID.Domain.Common.Interfaces.AuditEntry(InfoID.Domain.Common.Interfaces.AuditModules.Backup, "RestoreFailed",
                $"Restore from {System.IO.Path.GetFileName(file)} didn't complete: {restore.Error}")
            {
                EntityName = "Backup",
                After = new { File = file, restore.Error },
            }).GetAwaiter().GetResult();
        }
        return initResult;
    }

    private static async System.Threading.Tasks.Task ShowRestoreOutcomeAsync(RestoreStartup.Outcome restore)
    {
        try
        {
            var dialogs = Services.GetRequiredService<IDialogService>();
            if (restore.Succeeded)
            {
                var when = restore.Pending is { } p ? p.BackupCreatedUtc.ToLocalTime().ToString("dd MMM yyyy, HH:mm") : "the backup";
                var message = $"InfoID's data was restored from the backup made {when}.\n\n" +
                              "A safety backup of the data you had before is in your backup folder, if you need to go back." +
                              (restore.Warning is null ? "" : "\n\n" + restore.Warning);
                await dialogs.ShowDialogAsync<MessageDialogViewModel, bool>(new MessageDialogViewModel("Restore complete", message,
                    isError: restore.Warning is not null));
            }
            else
            {
                await dialogs.ShowDialogAsync<MessageDialogViewModel, bool>(new MessageDialogViewModel("Restore didn't complete",
                    (restore.Error ?? "The backup couldn't be restored.") + "\n\nDetails are in the log (Help > Open log folder).", isError: true));
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't show the restore result", ex);
        }
    }

    private static DatabaseInitializationResult RunDatabaseInitialization()
    {
        using var scope = Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
        return initializer.InitializeAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Deliberately plain (no DI/theme dependency) fatal-error window -- if the database
    /// can't come up, we still want to tell the user clearly instead of a silent crash
    /// or a stack trace, per the "show a user-friendly error instead of silently
    /// failing" requirement.
    /// </summary>
    private static Window BuildDatabaseErrorWindow(DatabaseInitializationResult result)
    {
        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 12,
        };

        panel.Children.Add(new TextBlock
        {
            Text = "InfoID could not start because its local database could not be initialized.",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Database path: {result.DatabasePath}",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = result.ErrorMessage ?? "Unknown error.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Please ensure InfoID has permission to write to this location, close any other program that may have the file open, then restart InfoID. A detailed log is available in the Logs folder next to Database.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        return new Window
        {
            Title = "InfoID -- Database Error",
            Width = 560,
            Height = 340,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = panel,
        };
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Existing backend layers -- unchanged, consumed through their own abstractions.
        services.AddApplication();
        services.AddInfrastructure();

        // Licensing (SRS 4.7.1): this machine's hardware identity and the embedded
        // serial-key secret, consumed by the Application layer's LicenseService.
        services.AddSingleton<InfoID.Application.Interfaces.IMachineFingerprintProvider, MachineFingerprintProvider>();
        services.AddSingleton<InfoID.Application.Interfaces.ILicenseSecretProvider, EmbeddedLicenseSecretProvider>();
        services.AddSingleton<InfoID.Application.Interfaces.ILicenseServerClient, LicenseServerClient>();
        services.AddSingleton<LicenseBadgeState>();

        // Customer platform: optional account (email-verified), editions, plan requests.
        services.AddSingleton<InfoID.Desktop.Features.Account.Services.AccountSession>();
        services.AddSingleton<InfoID.Desktop.Features.Account.Services.CustomerPortalClient>();
        // Trial mode: printing mode (licensed / trial / sign in first), the trial counter and
        // the print gate every printing entry point goes through.
        services.AddSingleton<InfoID.Desktop.Features.Trial.Services.TrialModeState>();
        services.AddSingleton<InfoID.Desktop.Features.Trial.Services.PrintAuthorizationService>();
        // Audit log "who": Windows user + signed-in account (replaces the Infrastructure default).
        services.AddSingleton<InfoID.Domain.Common.Interfaces.IAuditActorProvider, DesktopAuditActorProvider>();

        // Desktop shell infrastructure.
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IUserPreferencesService, JsonUserPreferencesService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();

        // Feature catalog services (sample data today -- see each service for the
        // real-data migration note).
        services.AddSingleton<IRecentCardService, RecentCardService>();
        services.AddSingleton<IBlankCardCatalogService, BlankCardCatalogService>();
        services.AddSingleton<ITemplateCatalogService, TemplateCatalogService>();

        // Card Designer -- format catalog (Phase 1). Canvas/document services are
        // registered here too as later phases add them.
        services.AddSingleton<ICustomCardModelStore, JsonCustomCardModelStore>();
        services.AddSingleton<ICardFormatCatalogService, CardFormatCatalogService>();
        services.AddSingleton<IDesignClipboard, DesignClipboard>();
        services.AddSingleton<IDesignAssetService, LocalDesignAssetService>();
        services.AddSingleton<IDataBindingEvaluator, DataBindingEvaluator>();
        services.AddSingleton<IPreviewDataProvider, SamplePreviewDataProvider>();
        services.AddSingleton<IDesignChecker, DesignChecker>();
        services.AddSingleton<IThumbnailService, ThumbnailService>();
        services.AddSingleton<IInfoIdFileService, InfoIdFileService>();
        services.AddSingleton<IRecentFilesService, JsonRecentFilesService>();
        // Priority 12 (Add Image workflow / Image Editor).
        services.AddSingleton<IImageEditingService, ImageEditingService>();
        // Priority 12 (Take Photo / camera capture). See FlashCapCameraService.cs's
        // own header comment for this service's honest confidence level.
        services.AddSingleton<ICameraService, FlashCapCameraService>();
        services.AddSingleton<IFaceDetectionService, FaceDetectionService>();
        services.AddScoped<ICardDesignRepository, CardDesignRepository>();

        // Print module -- one real OS-printing backend per platform (see
        // IPrinterService's own doc comment for why this is plain shell commands
        // rather than a System.Drawing.Printing/System.Windows.Forms dependency),
        // chosen once here rather than at every call site.
        services.AddSingleton<IPrinterService>(_ =>
            OperatingSystem.IsWindows() ? new WindowsPrinterService()
            : OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() ? new CupsPrinterService()
            : new UnsupportedPrinterService());
        services.AddSingleton<IPrintStatusStore, JsonPrintStatusStore>();

        // Page ViewModels -- transient so every navigation gets a clean instance.
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<RecentDesignsViewModel>();
        services.AddTransient<BlankCardViewModel>();
        services.AddTransient<CustomCardDialogViewModel>();
        services.AddTransient<TemplatesViewModel>();
        services.AddTransient<LicenseActivationViewModel>();
        services.AddTransient<LicenseStatusViewModel>();
        services.AddTransient<InfoID.Desktop.Features.Account.ViewModels.AccountLicenseViewModel>();
        services.AddTransient<InfoID.Desktop.Features.Account.ViewModels.EditionsViewModel>();
        services.AddTransient<InfoID.Desktop.Features.Account.ViewModels.RequestPlanViewModel>();
        services.AddTransient<InfoID.Desktop.Features.PrintHistory.ViewModels.PrintHistoryViewModel>();
        // Singleton (like CardDesignerViewModel): one dashboard that follows navigation to
        // start/stop its one-minute auto refresh, including Back navigation.
        services.AddSingleton<InfoID.Desktop.Features.Dashboard.ViewModels.DashboardViewModel>();
        // Singleton so the chosen report and filters are kept between visits.
        services.AddSingleton<InfoID.Desktop.Features.Reports.ViewModels.ReportsViewModel>();
        // Backup & Restore (FR-ENT-4): settings, the automatic schedule, and the page
        // (singleton, so a running backup's progress survives leaving the page).
        services.AddSingleton<BackupSettingsStore>();
        services.AddSingleton<AutoBackupScheduler>();
        services.AddSingleton<InfoID.Desktop.Features.Backup.ViewModels.BackupViewModel>();
        // Audit log page (singleton so filters are kept between visits).
        services.AddSingleton<InfoID.Desktop.Features.Audit.ViewModels.AuditLogViewModel>();
        // Visitor Management (badge workflow): singleton so filters and ticks are kept between visits.
        services.AddSingleton<InfoID.Desktop.Features.Visitors.ViewModels.VisitorsViewModel>();
        // Singleton (not transient): the designer's open tabs must survive a trip to
        // Home and back, and the header's "Windows" menu needs one persistent instance
        // to list open designs from regardless of which page is currently shown.
        services.AddSingleton<CardDesignerViewModel>();
        services.AddSingleton<IPhotoFolderService, PhotoFolderService>();

        // Shell.
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ShellView>();
    }
}
