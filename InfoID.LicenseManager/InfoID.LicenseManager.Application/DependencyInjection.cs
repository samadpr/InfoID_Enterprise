using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.LicenseManager.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        // Shared building blocks of the services below.
        services.AddScoped<LicenseCodeFactory>();
        services.AddScoped<CustomerMailer>();

        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IDashboardAnalyticsService, DashboardAnalyticsService>();
        services.AddScoped<IGlobalSearchService, GlobalSearchService>();
        services.AddScoped<ILicenseService, LicenseService>();
        services.AddScoped<IActivationService, ActivationService>();
        services.AddScoped<IOnlineActivationService, OnlineActivationService>();
        services.AddScoped<IActivationSimulatorService, ActivationSimulatorService>();
        services.AddScoped<IPlanService, PlanService>();
        services.AddScoped<ILicenseRequestService, LicenseRequestService>();
        services.AddScoped<ICustomerAdminService, CustomerAdminService>();
        services.AddScoped<ICustomerAccountService, CustomerAccountService>();
        services.AddScoped<IVerificationCodeService, VerificationCodeService>();
        services.AddScoped<ITrialService, TrialService>();
        return services;
    }
}
