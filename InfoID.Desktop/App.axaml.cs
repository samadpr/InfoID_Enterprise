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
using InfoID.Desktop.Features.BlankCard.Services;
using InfoID.Desktop.Features.BlankCard.ViewModels;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.Features.Templates.Services;
using InfoID.Desktop.Features.Templates.ViewModels;
using InfoID.Desktop.Features.Welcome.Services;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.Shell.ViewModels;
using InfoID.Desktop.Shell.Views;
using InfoID.Infrastructure;
using InfoID.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

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
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Database must be ready before anything else touches it -- this runs the
        // Migrate() + idempotent seed described in DatabaseInitializer, synchronously,
        // before any window is created. On a fresh/deleted database this is what
        // actually (re)creates %AppData%\InfoID\Database\InfoID_DB.db and its tables.
        var initResult = RunDatabaseInitialization();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!initResult.Success)
            {
                desktop.MainWindow = BuildDatabaseErrorWindow(initResult);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            Services.GetRequiredService<IThemeService>().InitializeFromSystem();

            var shell = Services.GetRequiredService<ShellView>();
            shell.DataContext = Services.GetRequiredService<ShellViewModel>();
            desktop.MainWindow = shell;
        }

        base.OnFrameworkInitializationCompleted();
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

        // Desktop shell infrastructure.
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();

        // Feature catalog services (sample data today -- see each service for the
        // real-data migration note).
        services.AddSingleton<IRecentCardService, RecentCardService>();
        services.AddSingleton<IBlankCardCatalogService, BlankCardCatalogService>();
        services.AddSingleton<ITemplateCatalogService, TemplateCatalogService>();

        // Card Designer -- format catalog (Phase 1). Canvas/document services are
        // registered here too as later phases add them.
        services.AddSingleton<ICustomCardModelStore, JsonCustomCardModelStore>();
        services.AddSingleton<ICardFormatCatalogService, CardFormatCatalogService>();

        // Page ViewModels -- transient so every navigation gets a clean instance.
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<BlankCardViewModel>();
        services.AddTransient<CustomCardDialogViewModel>();
        services.AddTransient<TemplatesViewModel>();
        services.AddTransient<CardDesignerViewModel>();

        // Shell.
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ShellView>();
    }
}