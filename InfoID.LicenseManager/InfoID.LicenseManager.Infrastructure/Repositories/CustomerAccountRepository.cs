using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Customers;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Infrastructure.Repositories;

internal sealed class CustomerAccountRepository : ICustomerAccountRepository
{
    private const string LikeEscape = "\\";

    private readonly LicenseManagerDbContext _db;

    public CustomerAccountRepository(LicenseManagerDbContext db) => _db = db;

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        _db.CustomerAccounts.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerListItemDto>> SearchAsync(CustomerSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var query = _db.CustomerAccounts.AsNoTracking().AsQueryable();

        if (criteria.Text is { } text)
        {
            var like = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(a =>
                EF.Functions.ILike(a.FullName, like, LikeEscape) ||
                EF.Functions.ILike(a.Email, like, LikeEscape) ||
                (a.CompanyName != null && EF.Functions.ILike(a.CompanyName, like, LikeEscape)) ||
                (a.Phone != null && EF.Functions.ILike(a.Phone, like, LikeEscape)));
        }

        query = criteria.Filter switch
        {
            CustomerFilter.Verified => query.Where(a => a.EmailVerified && !a.Cancelled),
            CustomerFilter.NotVerified => query.Where(a => !a.EmailVerified && !a.Cancelled),
            CustomerFilter.Disabled => query.Where(a => a.Cancelled),
            CustomerFilter.Leads => query.Where(a => a.EmailVerified && !a.Cancelled
                && !_db.LicenseRequests.Any(r => r.CustomerAccountId == a.Id && !r.Cancelled)
                && !_db.LicenseCodes.Any(c => c.CustomerAccountId == a.Id && !c.Cancelled)),
            _ => query,
        };

        return await query
            .OrderByDescending(a => a.CreatedDate)
            .Take(criteria.Take)
            .Select(a => new CustomerListItemDto(
                a.Id, a.FullName, a.CompanyName, a.Email, a.Phone, a.Country, a.CreatedDate,
                a.EmailVerified, a.LastLoginDate, a.Cancelled,
                _db.LicenseRequests.Count(r => r.CustomerAccountId == a.Id && !r.Cancelled),
                _db.LicenseCodes.Count(c => c.CustomerAccountId == a.Id && !c.Cancelled)))
            .ToListAsync(cancellationToken);
    }

    public Task<CustomerAccount?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _db.CustomerAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AccountLicenseDto>> GetLicensesAsync(long accountId, CancellationToken cancellationToken = default)
    {
        var codes = await _db.LicenseCodes.AsNoTracking().Include(c => c.Activations)
            .Where(c => c.CustomerAccountId == accountId && !c.Cancelled)
            .OrderByDescending(c => c.CreatedDate)
            .ToListAsync(cancellationToken);

        return codes.Select(c => new AccountLicenseDto(
            ActivationCodeFormat.Display(c.ActivationCode), c.ProductCode, c.Tier, c.LicenseType, c.ExpiryDate, c.MaxActivations,
            c.ActiveActivations.Count(), c.Status, c.AllowOfflineActivation)).ToList();
    }

    public async Task<IReadOnlyList<AccountRequestDto>> GetRequestsAsync(long accountId, CancellationToken cancellationToken = default)
    {
        var requests = await _db.LicenseRequests.AsNoTracking().Include(r => r.Plan).Include(r => r.LicenseCode)
            .Where(r => r.CustomerAccountId == accountId && !r.Cancelled)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync(cancellationToken);

        return requests.Select(r => new AccountRequestDto(
            r.Id, r.Plan?.Name ?? "", r.Machines, r.Status, r.StatusReason, r.CreatedDate,
            r.LicenseCode is { } code ? ActivationCodeFormat.Display(code.ActivationCode) : null)).ToList();
    }

    public async Task RevokeActiveSessionsAsync(long accountId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var sessions = await _db.CustomerSessions
            .Where(s => s.CustomerAccountId == accountId && s.RevokedUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions) session.RevokedUtc = utcNow;
    }

    public Task<CustomerAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        _db.CustomerAccounts.FirstOrDefaultAsync(a => a.Email == normalizedEmail, cancellationToken);

    public void Add(CustomerAccount account) => _db.CustomerAccounts.Add(account);

    public void AddSession(CustomerSession session) => _db.CustomerSessions.Add(session);

    public Task<CustomerSession?> FindLiveSessionAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        _db.CustomerSessions.Include(s => s.CustomerAccount)
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash && s.RevokedUtc == null && !s.Cancelled, cancellationToken);
}
