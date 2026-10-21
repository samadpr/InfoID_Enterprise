using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Customers;
using InfoID.LicenseManager.Application.Interfaces.Notifications;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Tests.Fakes;

public sealed class InMemoryPlanRepository : IPlanRepository
{
    private long _nextId = 1;

    public List<Plan> Plans { get; } = new();
    public List<Product> Products { get; } = new();

    public Plan Seed(string name = "Standard", LicenseTier tier = LicenseTier.Standard, int? months = 12, int machines = 1, int? maxCards = null, int order = 1, bool active = true)
    {
        var plan = new Plan
        {
            Id = _nextId++, Name = name, Tier = tier.ToString(), DurationMonths = months, DefaultMachines = machines,
            MaxCardsPerPeriod = maxCards, CardLimitPeriod = "Monthly", SortOrder = order, IsActive = active,
        };
        Plans.Add(plan);
        return plan;
    }

    public Task<IReadOnlyList<Plan>> GetAllAsync(string productCode, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Plan>>(Plans.Where(p => p.ProductCode == productCode && !p.Cancelled).OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToList());

    public Task<Plan?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Plans.FirstOrDefault(p => p.Id == id));

    public void Add(Plan plan)
    {
        plan.Id = _nextId++;
        Plans.Add(plan);
    }

    public Task<bool> ProductExistsAsync(string productCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Products.Any(p => p.Code == productCode));

    public Task<IReadOnlyList<Plan>> GetActiveAsync(string productCode, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Plan>>(Plans.Where(p => p.ProductCode == productCode && p.IsActive && !p.Cancelled).OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToList());

    public Task<Plan?> FindActiveAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Plans.FirstOrDefault(p => p.Id == id && p.IsActive && !p.Cancelled));

    public void AddProduct(Product product) => Products.Add(product);

    public Task<bool> AnyPlansAsync(string productCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Plans.Any(p => p.ProductCode == productCode));

    public void AddRange(IEnumerable<Plan> plans)
    {
        foreach (var plan in plans) Add(plan);
    }
}

public sealed class InMemoryLicenseRequestRepository : ILicenseRequestRepository
{
    private readonly FakeUnitOfWork? _unitOfWork;
    private long _nextId = 1;

    public InMemoryLicenseRequestRepository(FakeUnitOfWork? unitOfWork = null) => _unitOfWork = unitOfWork;

    public List<LicenseRequest> Requests { get; } = new();
    public InMemoryAdminUserRepository? Admins { get; set; }

    public LicenseRequest Seed(Plan plan, string email = "buyer@acme.test", string fullName = "Bea Buyer", string? company = "Acme AS",
        int machines = 2, long? accountId = null, string status = LicenseRequestStatus.New)
    {
        var request = new LicenseRequest
        {
            Id = _nextId++, PlanId = plan.Id, Plan = plan, Email = email, FullName = fullName, CompanyName = company, Machines = machines,
            Country = "Norway", CustomerAccountId = accountId, Status = status, CreatedDate = TestData.NowUtc.AddHours(-_nextId),
        };
        Requests.Add(request);
        return request;
    }

    public Task<IReadOnlyList<LicenseRequest>> SearchAsync(LicenseRequestSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        IEnumerable<LicenseRequest> query = Requests.Where(r => !r.Cancelled);
        if (criteria.Statuses is { } statuses) query = query.Where(r => statuses.Contains(r.Status));
        if (criteria.Text is { } text)
        {
            bool Has(string? value) => value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
            query = query.Where(r => Has(r.FullName) || Has(r.Email) || Has(r.CompanyName) || Has(r.Phone));
        }

        foreach (var request in Requests.Where(r => r.HandledByAdminUserId is not null))
            request.HandledByAdminUser ??= Admins?.All.FirstOrDefault(a => a.Id == request.HandledByAdminUserId);
        return Task.FromResult<IReadOnlyList<LicenseRequest>>(query.OrderByDescending(r => r.CreatedDate).Take(criteria.Take).ToList());
    }

    public Task<LicenseRequest?> FindDetailsAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Requests.FirstOrDefault(r => r.Id == id && !r.Cancelled));

    public Task<LicenseRequest?> FindForUpdateAsync(long id, CancellationToken cancellationToken = default)
    {
        if (_unitOfWork is { InTransaction: false })
            throw new InvalidOperationException("A row-locking read was made outside a transaction, so the lock would not be held.");
        return Task.FromResult(Requests.FirstOrDefault(r => r.Id == id && !r.Cancelled));
    }

    public Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(Requests.Where(r => !r.Cancelled).GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count()));

    public Task<int> CountNewAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Requests.Count(r => r.Status == LicenseRequestStatus.New && !r.Cancelled));

    public Task<int> CountOpenByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        Task.FromResult(Requests.Count(r => r.Email == normalizedEmail && !r.Cancelled && r.IsOpen));

    public void Add(LicenseRequest request)
    {
        request.Id = _nextId++;
        request.CreatedDate = TestData.NowUtc;
        Requests.Add(request);
    }

    public Task<IReadOnlyList<LicenseRequest>> GetUnlinkedByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LicenseRequest>>(Requests.Where(r => r.Email == normalizedEmail && r.CustomerAccountId == null && !r.Cancelled).ToList());
}

public sealed class InMemoryCustomerAccountRepository : ICustomerAccountRepository
{
    private readonly InMemoryLicenseRepository _licenses;
    private readonly InMemoryLicenseRequestRepository _requests;
    private long _nextId = 1;

    public InMemoryCustomerAccountRepository(InMemoryLicenseRepository licenses, InMemoryLicenseRequestRepository requests)
    {
        _licenses = licenses;
        _requests = requests;
    }

    public List<CustomerAccount> Accounts { get; } = new();
    public List<CustomerSession> Sessions { get; } = new();

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Accounts.Count);

    public CustomerAccount Seed(string email, string fullName = "Cathy Customer", bool verified = true, bool disabled = false, string? company = null)
    {
        var account = new CustomerAccount
        {
            Id = _nextId++, Email = email, FullName = fullName, CompanyName = company, EmailVerified = verified, Cancelled = disabled,
            CreatedDate = TestData.NowUtc.AddDays(-_nextId), PasswordHash = "x",
        };
        Accounts.Add(account);
        return account;
    }

    private CustomerListItemDto ToItem(CustomerAccount a) => new(
        a.Id, a.FullName, a.CompanyName, a.Email, a.Phone, a.Country, a.CreatedDate, a.EmailVerified, a.LastLoginDate, a.Cancelled,
        _requests.Requests.Count(r => r.CustomerAccountId == a.Id && !r.Cancelled),
        _licenses.Codes.Count(c => c.CustomerAccountId == a.Id && !c.Cancelled));

    public Task<IReadOnlyList<CustomerListItemDto>> SearchAsync(CustomerSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        IEnumerable<CustomerListItemDto> rows = Accounts.Select(ToItem);
        if (criteria.Text is { } text)
        {
            bool Has(string? value) => value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
            rows = rows.Where(r => Has(r.FullName) || Has(r.Email) || Has(r.CompanyName) || Has(r.Phone));
        }

        rows = criteria.Filter switch
        {
            CustomerFilter.Verified => rows.Where(r => r.EmailVerified && !r.Disabled),
            CustomerFilter.NotVerified => rows.Where(r => !r.EmailVerified && !r.Disabled),
            CustomerFilter.Disabled => rows.Where(r => r.Disabled),
            CustomerFilter.Leads => rows.Where(r => r.IsLead),
            _ => rows,
        };
        return Task.FromResult<IReadOnlyList<CustomerListItemDto>>(rows.OrderByDescending(r => r.CreatedDate).Take(criteria.Take).ToList());
    }

    public Task<CustomerAccount?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Accounts.FirstOrDefault(a => a.Id == id));

    public Task<IReadOnlyList<AccountLicenseDto>> GetLicensesAsync(long accountId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AccountLicenseDto>>(_licenses.Codes.Where(c => c.CustomerAccountId == accountId && !c.Cancelled)
            .Select(c => new AccountLicenseDto(ActivationCodeFormat.Display(c.ActivationCode), c.ProductCode, c.Tier, c.LicenseType, c.ExpiryDate,
                c.MaxActivations, c.ActiveActivations.Count(), c.Status, c.AllowOfflineActivation)).ToList());

    public Task<IReadOnlyList<AccountRequestDto>> GetRequestsAsync(long accountId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AccountRequestDto>>(_requests.Requests.Where(r => r.CustomerAccountId == accountId && !r.Cancelled)
            .Select(r => new AccountRequestDto(r.Id, r.Plan?.Name ?? "", r.Machines, r.Status, r.StatusReason, r.CreatedDate,
                r.LicenseCode is { } c ? ActivationCodeFormat.Display(c.ActivationCode) : null)).ToList());

    public Task RevokeActiveSessionsAsync(long accountId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        foreach (var session in Sessions.Where(s => s.CustomerAccountId == accountId && s.RevokedUtc == null)) session.RevokedUtc = utcNow;
        return Task.CompletedTask;
    }

    public Task<CustomerAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        Task.FromResult(Accounts.FirstOrDefault(a => a.Email == normalizedEmail));

    public void Add(CustomerAccount account)
    {
        account.Id = _nextId++;
        account.CreatedDate = TestData.NowUtc;
        Accounts.Add(account);
    }

    public void AddSession(CustomerSession session)
    {
        session.CustomerAccountId = session.CustomerAccount?.Id ?? session.CustomerAccountId;
        Sessions.Add(session);
    }

    public Task<CustomerSession?> FindLiveSessionAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(Sessions.FirstOrDefault(s => s.TokenHash == tokenHash && s.RevokedUtc == null && !s.Cancelled));
}

public sealed class InMemoryEmailCodeRepository : IEmailCodeRepository
{
    private readonly TimeProvider _time;

    public InMemoryEmailCodeRepository(TimeProvider time) => _time = time;

    public List<EmailCode> Codes { get; } = new();

    public Task<IReadOnlyList<EmailCode>> GetUnusedAsync(long accountId, string purpose, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EmailCode>>(Codes.Where(c => c.CustomerAccountId == accountId && c.Purpose == purpose && c.UsedUtc == null).ToList());

    public void Add(EmailCode code)
    {
        code.CreatedDate = _time.GetUtcNow().UtcDateTime; // the real DbContext stamps this on save
        Codes.Add(code);
    }

    public Task<EmailCode?> FindLatestUnusedAsync(long accountId, string purpose, CancellationToken cancellationToken = default) =>
        Task.FromResult(Codes.Where(c => c.CustomerAccountId == accountId && c.Purpose == purpose && c.UsedUtc == null).OrderByDescending(c => c.CreatedDate).FirstOrDefault());
}

public sealed class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = new();

    /// <summary>When set, every send fails with this exception (a mail server that is down).</summary>
    public Exception? Failure { get; set; }

    public bool IsRealDelivery => false;

    public Task SendAsync(string to, string subject, string textBody, CancellationToken cancellationToken = default)
    {
        if (Failure is not null) throw Failure;
        Sent.Add((to, subject, textBody));
        return Task.CompletedTask;
    }
}
