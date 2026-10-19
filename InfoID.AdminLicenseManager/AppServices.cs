using InfoID.AdminLicenseManager.ViewModels;
using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InfoID.AdminLicenseManager;

/// <summary>Composition root. The connection string comes from appsettings.json, and
/// the environment variable INFOID_LICENSE_DB overrides it so the real password never
/// has to live in a file on staff machines.</summary>
public static class AppServices
{
    public static IServiceProvider Build(ILoggerFactory? loggerFactory = null)
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
        // Same logger factory as AppLog: EF Core's database errors and any service taking
        // ILogger<T> write to the License Manager log file.
        if (loggerFactory is not null)
        {
            services.AddSingleton(loggerFactory);
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        }
        services.AddDbContextFactory<LicenseDbContext>(o => o.UseNpgsql(connectionString));

        services.AddSingleton(new AdminContext { ClientAddress = Environment.MachineName });
        services.AddSingleton(LicenseSecretStore.FromEnvironment());
        services.AddSingleton<AuditService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<LicenseCodeService>();
        services.AddSingleton<ActivationService>();
        services.AddSingleton<DashboardService>();
        // Customer platform: approving a request emails the code (SMTP from
        // INFOID_SMTP_*; without it, emails are saved in the INFOID_MAIL_OUTBOX folder).
        services.AddSingleton(EmailSenderFactory.FromEnvironment());
        services.AddSingleton<PlanService>();
        services.AddSingleton<LicenseRequestService>();
        services.AddSingleton<CustomerAccountService>();
        services.AddSingleton<CustomerAdminService>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<LicenseCodesViewModel>();
        services.AddTransient<GenerateSerialKeyViewModel>();
        services.AddTransient<AuditLogViewModel>();
        services.AddTransient<LicenseRequestsViewModel>();
        services.AddTransient<PlansViewModel>();
        services.AddTransient<CustomersViewModel>();
        services.AddTransient<ActivationSimulatorViewModel>();

        return services.BuildServiceProvider();
    }
}
