using InfoID.LicenseManager.Application.Interfaces.Notifications;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Security;
using InfoID.LicenseManager.Infrastructure.Cryptography;
using InfoID.LicenseManager.Infrastructure.ExternalServices;
using InfoID.LicenseManager.Infrastructure.Persistence;
using InfoID.LicenseManager.Infrastructure.Repositories;
using InfoID.LicenseManager.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.LicenseManager.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers everything the application layer needs from infrastructure.
    /// Called once from the web app's composition root.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Scoped: one context (one unit of work) per web request.
        services.AddDbContext<LicenseManagerDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAdminUserRepository, AdminUserRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<ILicenseRepository, LicenseRepository>();
        services.AddScoped<IActivationRepository, ActivationRepository>();
        services.AddScoped<ICustomerRecordRepository, CustomerRecordRepository>();
        services.AddScoped<IBlacklistRepository, BlacklistRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<ILicenseRequestRepository, LicenseRequestRepository>();
        services.AddScoped<ICustomerAccountRepository, CustomerAccountRepository>();
        services.AddScoped<IEmailCodeRepository, EmailCodeRepository>();
        services.AddScoped<ITrialRepository, TrialRepository>();

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ILicenseTokenSigner>(sp => new ConfigurationLicenseTokenSigner(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<ILicenseSecretProvider>(sp => new ConfigurationLicenseSecretProvider(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<IEmailSender>(sp => EmailSenderFactory.Create(sp.GetRequiredService<IConfiguration>()));
        return services;
    }
}
