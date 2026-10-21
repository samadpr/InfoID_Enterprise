using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Trial;
using InfoID.LicenseManager.Application.Services;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure.Persistence;
using InfoID.LicenseManager.Infrastructure.Repositories;
using InfoID.LicenseManager.Infrastructure.Security;
using InfoID.LicenseManager.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfoID.LicenseManager.Tests.Integration;

/// <summary>One web request's worth of the REAL services and repositories over their own DbContext (like the app builds per
/// request), with only the outside world faked: e-mail, the clock and the license signing key.</summary>
public sealed class RealServices : IDisposable
{
    public RealServices(string connectionString, string role = AdminRoles.LicenseManager, long? adminUserId = null, FakeEmailSender? email = null, TimeProvider? clock = null)
    {
        Clock = clock ?? TimeProvider.System;
        Email = email ?? new FakeEmailSender();
        var user = new FakeCurrentUser { IsAuthenticated = role != "Anonymous", AdminUserId = adminUserId, Email = "manager@infoid.test", Role = role };
        Db = PostgresFixture.NewContext(connectionString, user);
        var options = new DbContextOptionsBuilder<LicenseManagerDbContext>().UseNpgsql(connectionString).Options;

        var unitOfWork = new UnitOfWork(Db);
        var audit = new AuditService(new AuditLogRepository(options, user, Db), user, Clock);
        var licenses = new LicenseRepository(Db);
        var customerRecords = new CustomerRecordRepository(Db);
        var activations = new ActivationRepository(Db);
        var blacklist = new BlacklistRepository(Db);
        var plans = new PlanRepository(Db);
        var requests = new LicenseRequestRepository(Db);
        var accounts = new CustomerAccountRepository(Db);
        var codes = new EmailCodeRepository(Db);
        var hasher = new Pbkdf2PasswordHasher();
        var factory = new LicenseCodeFactory(licenses, customerRecords, Clock);
        var mailer = new CustomerMailer(Email, audit, NullLogger<CustomerMailer>.Instance);

        Licenses = new LicenseService(licenses, factory, activations, unitOfWork, audit, user, Clock, NullLogger<LicenseService>.Instance);
        Activation = new ActivationService(licenses, blacklist, unitOfWork, new TestSecretProvider(), audit, user, Clock, NullLogger<ActivationService>.Instance);
        Online = new OnlineActivationService(licenses, blacklist, activations, unitOfWork, new FakeLicenseTokenSigner(), audit, Clock, NullLogger<OnlineActivationService>.Instance);
        Plans = new PlanService(plans, unitOfWork, audit, user);
        var verification = new VerificationCodeService(codes, unitOfWork, mailer, Clock);
        Requests = new LicenseRequestService(requests, accounts, plans, new SalesNotificationOptions(), factory, mailer, unitOfWork, audit, user, Clock, NullLogger<LicenseRequestService>.Instance);
        CustomerAdmin = new CustomerAdminService(accounts, verification, unitOfWork, audit, user, Clock, NullLogger<CustomerAdminService>.Instance);
        Accounts = new CustomerAccountService(accounts, requests, verification, hasher, unitOfWork, audit, user, Clock, NullLogger<CustomerAccountService>.Instance);
        Trials = new TrialService(new TrialRepository(Db), new TrialOptions(), Clock, NullLogger<TrialService>.Instance);
        Audit = audit;
    }

    public TimeProvider Clock { get; }
    public FakeEmailSender Email { get; }
    public LicenseManagerDbContext Db { get; }
    public LicenseService Licenses { get; }
    public ActivationService Activation { get; }
    public OnlineActivationService Online { get; }
    public PlanService Plans { get; }
    public LicenseRequestService Requests { get; }
    public CustomerAdminService CustomerAdmin { get; }
    public CustomerAccountService Accounts { get; }
    public TrialService Trials { get; }
    public AuditService Audit { get; }

    public void Dispose() => Db.Dispose();
}
