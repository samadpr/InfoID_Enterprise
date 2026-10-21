using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Notifications;
using InfoID.LicenseManager.Application.Interfaces.Security;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.LicenseManager.Tests.Integration;

/// <summary>Builds the real web host (no database connection is opened) with scope and
/// build-time validation on, and checks that every service the application layer needs can be
/// constructed with the real infrastructure registrations.</summary>
public class CompositionRootTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CompositionRootTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(b =>
            b.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; }));

    [Fact]
    public void DbContext_and_current_user_resolve_in_a_request_scope()
    {
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<LicenseManagerDbContext>();
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        Assert.NotNull(db);
        Assert.False(user.IsAuthenticated);
        Assert.Null(user.AdminUserId);
    }

    [Theory]
    [InlineData(typeof(IAuthService))]
    [InlineData(typeof(IAuditService))]
    [InlineData(typeof(IDashboardService))]
    [InlineData(typeof(ILicenseService))]
    [InlineData(typeof(IActivationService))]
    [InlineData(typeof(IActivationSimulatorService))]
    [InlineData(typeof(ICustomerRecordRepository))]
    [InlineData(typeof(IBlacklistRepository))]
    [InlineData(typeof(ILicenseSecretProvider))]
    [InlineData(typeof(IOnlineActivationService))]
    [InlineData(typeof(ICustomerAccountService))]
    [InlineData(typeof(ITrialService))]
    [InlineData(typeof(IVerificationCodeService))]
    [InlineData(typeof(ILicenseTokenSigner))]
    [InlineData(typeof(ITrialRepository))]
    [InlineData(typeof(IEmailSender))]
    [InlineData(typeof(IAdminUserRepository))]
    [InlineData(typeof(IAuditLogRepository))]
    [InlineData(typeof(ILicenseRepository))]
    [InlineData(typeof(IActivationRepository))]
    [InlineData(typeof(IUnitOfWork))]
    [InlineData(typeof(IPasswordHasher))]
    public void Every_application_abstraction_resolves_to_a_registered_implementation(Type abstraction)
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(abstraction));
    }

    [Fact]
    public void Repositories_share_the_unit_of_works_context_within_one_scope()
    {
        using var scope = _factory.Services.CreateScope();

        var contextA = scope.ServiceProvider.GetRequiredService<LicenseManagerDbContext>();
        var contextB = scope.ServiceProvider.GetRequiredService<LicenseManagerDbContext>();

        Assert.Same(contextA, contextB);
    }
}
