using InfoID.Licensing.Core.Codecs;
using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Tests.Fakes;

public sealed class InMemoryAdminUserRepository : IAdminUserRepository
{
    private readonly List<AdminUser> _admins = new();
    private long _nextId = 1;

    public IReadOnlyList<AdminUser> All => _admins;

    public AdminUser Seed(string email, string password, string role = AdminRoles.SuperAdmin, string fullName = "Test Admin")
    {
        var admin = new AdminUser { Id = _nextId++, Email = email, FullName = fullName, Role = role, PasswordHash = "fake$" + password };
        _admins.Add(admin);
        return admin;
    }

    public Task<bool> AnyActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_admins.Any(a => !a.Cancelled));

    public Task<AdminUser?> FindActiveByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        Task.FromResult(_admins.FirstOrDefault(a => a.Email == normalizedEmail && !a.Cancelled));

    public Task<AdminUser?> FindActiveByIdAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_admins.FirstOrDefault(a => a.Id == id && !a.Cancelled));

    public Task<bool> TryAddFirstAdminAsync(AdminUser admin, CancellationToken cancellationToken = default)
    {
        lock (_admins)
        {
            if (_admins.Any(a => !a.Cancelled)) return Task.FromResult(false);
            admin.Id = _nextId++;
            _admins.Add(admin);
            return Task.FromResult(true);
        }
    }
}

public sealed class InMemoryAuditLogRepository : IAuditLogRepository
{
    private readonly List<AdminAuditLog> _rows = new();
    private readonly InMemoryAdminUserRepository? _admins;

    public InMemoryAuditLogRepository(InMemoryAdminUserRepository? admins = null) => _admins = admins;

    public List<AdminAuditLog> Rows => _rows;

    public Task AppendAsync(Func<string?, AdminAuditLog> createEntry, CancellationToken cancellationToken = default)
    {
        lock (_rows)
        {
            var entry = createEntry(_rows.LastOrDefault()?.RecordHash);
            entry.Id = _rows.Count + 1;
            _rows.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AdminAuditLog>> SearchAsync(AuditSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        foreach (var row in _rows.Where(r => r.AdminUserId is not null))
            row.AdminUser ??= _admins?.All.FirstOrDefault(a => a.Id == row.AdminUserId);

        bool Has(string? value, string text) => value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
        var query = _rows.AsEnumerable();
        if (criteria.FromUtc is { } from) query = query.Where(r => r.ActionDate >= from);
        if (criteria.ToUtcExclusive is { } to) query = query.Where(r => r.ActionDate < to);
        if (criteria.Action is { } action) query = query.Where(r => Has(r.Action, action));
        if (criteria.Who is { } who) query = query.Where(r => r.AdminUser is not null && (Has(r.AdminUser.FullName, who) || Has(r.AdminUser.Email, who)));
        return Task.FromResult<IReadOnlyList<AdminAuditLog>>(query.OrderByDescending(r => r.Id).Take(criteria.Take).ToList());
    }

    public Task<IReadOnlyList<AdminAuditLog>> GetRecentAsync(int take, CancellationToken cancellationToken = default)
    {
        foreach (var row in _rows.Where(r => r.AdminUserId is not null))
            row.AdminUser ??= _admins?.All.FirstOrDefault(a => a.Id == row.AdminUserId);

        return Task.FromResult<IReadOnlyList<AdminAuditLog>>(_rows.OrderByDescending(r => r.Id).Take(take).ToList());
    }

    public async IAsyncEnumerable<AdminAuditLog> StreamAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var row in _rows.OrderBy(r => r.Id)) yield return row;
        await Task.CompletedTask;
    }
}

/// <summary>Behaves like the database-backed repository for what the services rely on: ids are assigned on add,
/// row-locking reads must happen inside a transaction, and searches filter the same fields.</summary>
public sealed class InMemoryLicenseRepository : ILicenseRepository
{
    private readonly FakeUnitOfWork? _unitOfWork;
    private LicenseStatusCounts? _counts;
    private long _nextId = 1;

    public InMemoryLicenseRepository(FakeUnitOfWork? unitOfWork = null) => _unitOfWork = unitOfWork;

    public List<LicenseCode> Codes { get; } = new();

    /// <summary>Counts reported to the dashboard; computed from <see cref="Codes"/> unless a test sets them.</summary>
    public LicenseStatusCounts Counts
    {
        get => _counts ?? new LicenseStatusCounts(
            Codes.Count(c => !c.Cancelled),
            Codes.Count(c => !c.Cancelled && c.Status == LicenseCodeStatus.Available),
            Codes.Count(c => !c.Cancelled && c.Status == LicenseCodeStatus.Active),
            Codes.Count(c => !c.Cancelled && c.Status == LicenseCodeStatus.Revoked));
        set => _counts = value;
    }

    /// <summary>Expiring-code counts by window length in days (overrides the computed value).</summary>
    public Dictionary<int, int> Expiring { get; } = new();

    public List<(DateOnly Today, int Days)> ExpiringCalls { get; } = new();

    public LicenseSearchCriteria? LastSearch { get; private set; }

    public List<long> LockedIds { get; } = new();

    public LicenseCode Add(LicenseCode code)
    {
        code.Id = _nextId++;
        code.CreatedDate = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        Codes.Add(code);
        return code;
    }

    public Task<LicenseLifecycleCounts> CountLifecycleAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        var live = Codes.Where(c => !c.Cancelled).ToList();
        bool IsExpired(LicenseCode c) => !c.IsRevoked && c.ExpiryDate is { } e && e < today;
        return Task.FromResult(new LicenseLifecycleCounts(
            live.Count(c => !c.IsRevoked && !IsExpired(c) && c.Status == LicenseCodeStatus.Available),
            live.Count(c => !c.IsRevoked && !IsExpired(c) && c.Status == LicenseCodeStatus.Active),
            live.Count(IsExpired),
            live.Count(c => c.IsRevoked)));
    }

    public Task<IReadOnlyList<TopCustomerRow>> GetTopCustomersAsync(int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TopCustomerRow>>(Codes
            .Where(c => !c.Cancelled && c.CustomerRecord is { OrganizationName: { Length: > 0 } })
            .GroupBy(c => c.CustomerRecord!.OrganizationName.ToLowerInvariant())
            .Select(g => new TopCustomerRow(g.Min(c => c.CustomerRecord!.OrganizationName), g.Count(), g.Sum(c => c.ActiveActivations.Count())))
            .OrderByDescending(r => r.Licenses).ThenBy(r => r.Name)
            .Take(take)
            .ToList());

    public Task<LicenseStatusCounts> CountByStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(Counts);

    public Task<int> CountExpiringAsync(DateOnly today, int days, CancellationToken cancellationToken = default)
    {
        ExpiringCalls.Add((today, days));
        if (Expiring.TryGetValue(days, out var fixedCount)) return Task.FromResult(fixedCount);

        var last = today.AddDays(days);
        return Task.FromResult(Codes.Count(c => !c.Cancelled && c.Status != LicenseCodeStatus.Revoked && c.ExpiryDate >= today && c.ExpiryDate <= last));
    }

    public Task<IReadOnlyList<LicenseListItemDto>> SearchAsync(LicenseSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        LastSearch = criteria;
        IEnumerable<LicenseCode> query = Codes.Where(c => !c.Cancelled);
        if (criteria.BatchId is { } batch) query = query.Where(c => c.BatchId == batch);
        if (criteria.Status is { } status) query = query.Where(c => c.Status == status);
        if (criteria.Text is { } text)
        {
            bool Has(string? value) => value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
            query = query.Where(c =>
                (criteria.CodeFragment is { Length: >= 4 } fragment && c.ActivationCode.Contains(fragment, StringComparison.Ordinal)) ||
                Has(c.CustomerRecord?.OrganizationName) || Has(c.CustomerRecord?.ContactEmail) ||
                c.Activations.Any(a => Has(a.SerialKeyIssued) || Has(a.MachineFingerprint)));
        }

        IReadOnlyList<LicenseListItemDto> rows = query.OrderByDescending(c => c.Id).Take(criteria.Take).Select(c => new LicenseListItemDto(
            c.Id, c.ActivationCode, c.Tier, c.LicenseType, c.ExpiryDate, c.MaxCardsPerPeriod, c.Status,
            c.ActiveActivations.Count(), c.MaxActivations, c.CustomerRecord?.OrganizationName, c.CreatedDate, c.AllowOfflineActivation)).ToList();
        return Task.FromResult(rows);
    }

    public Task<LicenseCode?> FindDetailsAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Codes.FirstOrDefault(c => c.Id == id && !c.Cancelled));

    public Task<LicenseCode?> FindByActivationCodeAsync(string normalizedCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Codes.FirstOrDefault(c => c.ActivationCode == normalizedCode && !c.Cancelled));

    public Task<LicenseCode?> FindForUpdateAsync(long id, CancellationToken cancellationToken = default)
    {
        var code = Codes.FirstOrDefault(c => c.Id == id && !c.Cancelled);
        return Task.FromResult(Lock(code));
    }

    public Task<LicenseCode?> FindForUpdateByActivationCodeAsync(string normalizedCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Lock(Codes.FirstOrDefault(c => c.ActivationCode == normalizedCode && !c.Cancelled)));

    public Task<IReadOnlySet<string>> GetExistingActivationCodesAsync(IReadOnlyCollection<string> candidates, CancellationToken cancellationToken = default)
    {
        IReadOnlySet<string> existing = Codes.Select(c => c.ActivationCode).Where(candidates.Contains).ToHashSet();
        return Task.FromResult(existing);
    }

    /// <summary>When set, storing new codes fails with this exception (a database error part-way through a workflow).</summary>
    public Exception? FailOnAdd { get; set; }

    public void AddRange(IEnumerable<LicenseCode> codes)
    {
        if (FailOnAdd is not null) throw FailOnAdd;
        foreach (var code in codes) Add(code);
    }

    private LicenseCode? Lock(LicenseCode? code)
    {
        if (_unitOfWork is { InTransaction: false })
            throw new InvalidOperationException("A row-locking read was made outside a transaction, so the lock would not be held.");
        if (code is not null) LockedIds.Add(code.Id);
        return code;
    }

    /// <summary>Normalised form used by tests to look a code up.</summary>
    public static string Normalize(string code) => CrockfordBase32.Normalize(code);
}

public sealed class InMemoryActivationRepository : IActivationRepository
{
    public int Active { get; set; }
    public int Recent { get; set; }
    public DateTime? RequestedSince { get; private set; }
    public List<TransferHistory> Transfers { get; } = new();

    public Task<int> CountActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Active);

    public Task<int> CountActivatedSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        RequestedSince = sinceUtc;
        return Task.FromResult(Recent);
    }

    /// <summary>Activation days reported to the dashboard trend (a test sets them).</summary>
    public List<DailyCount> PerDay { get; } = new();

    /// <summary>Every window asked for, in call order (the chart window first, then the period before it).</summary>
    public List<(DateTime From, DateTime ToExclusive)> RequestedRanges { get; } = new();

    public Task<IReadOnlyList<DailyCount>> CountActivationsPerDayAsync(DateTime fromUtc, DateTime toUtcExclusive, CancellationToken cancellationToken = default)
    {
        RequestedRanges.Add((fromUtc, toUtcExclusive));
        return Task.FromResult<IReadOnlyList<DailyCount>>(PerDay
            .Where(d => d.Day.ToDateTime(TimeOnly.MinValue) >= fromUtc.Date && d.Day.ToDateTime(TimeOnly.MinValue) < toUtcExclusive).ToList());
    }

    public void AddTransfers(IEnumerable<TransferHistory> transfers) => Transfers.AddRange(transfers);
}

public sealed class InMemoryCustomerRecordRepository : ICustomerRecordRepository
{
    public List<CustomerRecord> Customers { get; } = new();

    public Task<CustomerRecord?> FindMatchingAsync(string organizationName, string? normalizedEmail, CancellationToken cancellationToken = default) =>
        Task.FromResult(Customers.FirstOrDefault(c => !c.Cancelled &&
            (normalizedEmail is not null ? c.ContactEmail == normalizedEmail : c.OrganizationName == organizationName)));

    public void Add(CustomerRecord customer) => Customers.Add(customer);
}

public sealed class InMemoryBlacklistRepository : IBlacklistRepository
{
    public HashSet<string> Fingerprints { get; } = new();
    public HashSet<string> ActivationCodes { get; } = new();

    public Task<bool> IsBlacklistedAsync(string fingerprintHex, string normalizedActivationCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Fingerprints.Contains(fingerprintHex) || ActivationCodes.Contains(normalizedActivationCode));
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }
    public int Commits { get; private set; }
    public int Rollbacks { get; private set; }
    public bool InTransaction { get; private set; }
    public string? Actor { get; private set; }

    public void UseActor(string actor) => Actor = actor;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        return Task.FromResult(0);
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (InTransaction) return await work(cancellationToken);

        InTransaction = true;
        try
        {
            var result = await work(cancellationToken);
            Commits++;
            return result;
        }
        catch
        {
            Rollbacks++;
            throw;
        }
        finally
        {
            InTransaction = false;
        }
    }
}
