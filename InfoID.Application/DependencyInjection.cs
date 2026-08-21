using InfoID.Application.Interfaces;
using InfoID.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers every Application-layer service. Call from InfoID.App's
    /// startup alongside AddInfrastructure(). As you scaffold Module C
    /// onward, add each new I{X}Service/{X}Service pair here -- one line per
    /// service, same pattern.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IAppUserService, AppUserService>();
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<ILicenseService, LicenseService>();
        services.AddScoped<ITemplateService, TemplateService>();
        services.AddScoped<ICardholderService, CardholderService>();

        return services;
    }
}
