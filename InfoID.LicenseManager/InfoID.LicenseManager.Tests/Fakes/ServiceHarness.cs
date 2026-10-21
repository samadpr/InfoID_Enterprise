using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Trial;
using InfoID.LicenseManager.Application.Services;
using InfoID.LicenseManager.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfoID.LicenseManager.Tests.Fakes;

/// <summary>The application services wired to in-memory repositories, acting as a chosen staff role.</summary>
public sealed class ServiceHarness
{
    public ServiceHarness(string role = AdminRoles.LicenseManager, TestSecretProvider? secrets = null)
    {
        Admins = new InMemoryAdminUserRepository();
        UnitOfWork = new FakeUnitOfWork();
        Licenses = new InMemoryLicenseRepository(UnitOfWork);
        Requests = new InMemoryLicenseRequestRepository(UnitOfWork) { Admins = Admins };
        Accounts = new InMemoryCustomerAccountRepository(Licenses, Requests);
        EmailCodes = new InMemoryEmailCodeRepository(Clock);
        Audit = new InMemoryAuditLogRepository(Admins);
        CurrentUser = new FakeCurrentUser { IsAuthenticated = true, AdminUserId = 7, Email = "staff@infoid.test", Role = role };
        Secrets = secrets ?? new TestSecretProvider();

        var auditService = new AuditService(Audit, CurrentUser, Clock);
        var factory = new LicenseCodeFactory(Licenses, Customers, Clock);
        var mailer = new CustomerMailer(Email, auditService, NullLogger<CustomerMailer>.Instance);
        Service = new LicenseService(Licenses, factory, Activations, UnitOfWork, auditService, CurrentUser, Clock, NullLogger<LicenseService>.Instance);
        Activation = new ActivationService(Licenses, Blacklist, UnitOfWork, Secrets, auditService, CurrentUser, Clock, NullLogger<ActivationService>.Instance);
        Simulator = new ActivationSimulatorService(Secrets, CurrentUser);
        PlanService = new PlanService(Plans, UnitOfWork, auditService, CurrentUser);
        Verification = new VerificationCodeService(EmailCodes, UnitOfWork, mailer, Clock);
        RequestService = new LicenseRequestService(Requests, Accounts, Plans, SalesOptions, factory, mailer, UnitOfWork, auditService, CurrentUser, Clock, NullLogger<LicenseRequestService>.Instance);
        Online = new OnlineActivationService(Licenses, Blacklist, Activations, UnitOfWork, Signer, auditService, Clock, NullLogger<OnlineActivationService>.Instance);
        AccountService = new CustomerAccountService(Accounts, Requests, Verification, Hasher, UnitOfWork, auditService, CurrentUser, Clock, NullLogger<CustomerAccountService>.Instance);
        TrialService = new TrialService(Trials, TrialOptions, Clock, NullLogger<TrialService>.Instance);
        Analytics = new DashboardAnalyticsService(new DashboardService(Licenses, Activations, Clock), Licenses, Activations, Requests, Accounts, Plans, auditService, Clock);
        CustomerService = new CustomerAdminService(Accounts, Verification, UnitOfWork, auditService, CurrentUser, Clock, NullLogger<CustomerAdminService>.Instance);
        GlobalSearch = new GlobalSearchService(Service, CustomerService, RequestService, PlanService);
    }

    public InMemoryAdminUserRepository Admins { get; }
    public FakeUnitOfWork UnitOfWork { get; }
    public InMemoryLicenseRepository Licenses { get; }
    public InMemoryLicenseRequestRepository Requests { get; }
    public InMemoryCustomerAccountRepository Accounts { get; }
    public InMemoryEmailCodeRepository EmailCodes { get; }
    public InMemoryAuditLogRepository Audit { get; }
    public InMemoryActivationRepository Activations { get; } = new();
    public InMemoryCustomerRecordRepository Customers { get; } = new();
    public InMemoryBlacklistRepository Blacklist { get; } = new();
    public InMemoryPlanRepository Plans { get; } = new();
    public FakeEmailSender Email { get; } = new();
    public FakeLicenseTokenSigner Signer { get; } = new();
    public FakePasswordHasher Hasher { get; } = new();
    public InMemoryTrialRepository Trials { get; } = new();
    public TrialOptions TrialOptions { get; } = new();
    public SalesNotificationOptions SalesOptions { get; } = new();
    public FakeCurrentUser CurrentUser { get; }
    public TestSecretProvider Secrets { get; }
    public TestClock Clock { get; } = new();

    public LicenseService Service { get; }
    public ActivationService Activation { get; }
    public ActivationSimulatorService Simulator { get; }
    public PlanService PlanService { get; }
    public VerificationCodeService Verification { get; }
    public LicenseRequestService RequestService { get; }
    public CustomerAdminService CustomerService { get; }
    public OnlineActivationService Online { get; }
    public CustomerAccountService AccountService { get; }
    public TrialService TrialService { get; }
    public DashboardAnalyticsService Analytics { get; }
    public GlobalSearchService GlobalSearch { get; }

    public IEnumerable<string> AuditedActions => Audit.Rows.Select(r => r.Action);
}
