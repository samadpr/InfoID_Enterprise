using InfoID.AdminLicenseManager.ViewModels;
using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.AdminLicenseManager;

/// <summary>Composition root. The connection string comes from appsettings.json, and
/// the environment variable INFOID_LICENSE_DB overrides it so the real password never
/// has to live in a file on staff machines.</summary>
public static class AppServices
{
    public static IServiceProvider Build()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = Environment.GetEnvironmentVariable("INFOID_LICENSE_DB")
                               ?? configuration.GetConnectionString("LicenseDb")
                               ?? throw new InvalidOperationException("No LicenseDb connection string configured.");

        var services = new ServiceCollection();
        services.AddDbContextFactory<LicenseDbContext>(o => o.UseNpgsql(connectionString));

        services.AddSingleton(new AdminContext { ClientAddress = Environment.MachineName });
        services.AddSingleton(LicenseSecretStore.FromEnvironment());
        services.AddSingleton<AuditService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<LicenseCodeService>();
        services.AddSingleton<ActivationService>();
        services.AddSingleton<DashboardService>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<LicenseCodesViewModel>();
        services.AddTransient<GenerateSerialKeyViewModel>();
        services.AddTransient<AuditLogViewModel>();

        return services.BuildServiceProvider();
    }
}
