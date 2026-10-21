using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Infrastructure.Repositories;

internal sealed class LicenseRequestRepository : ILicenseRequestRepository
{
    private const string LikeEscape = "\\";

    private readonly LicenseManagerDbContext _db;

    public LicenseRequestRepository(LicenseManagerDbContext db) => _db = db;

    public async Task<IReadOnlyList<LicenseRequest>> SearchAsync(LicenseRequestSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var query = WithDetails(_db.LicenseRequests.AsNoTracking()).Where(r => !r.Cancelled);

        if (criteria.Statuses is { } statuses)
            query = query.Where(r => statuses.Contains(r.Status));

        if (criteria.Text is { } text)
        {
            // Typed %, _ and \ are searched literally, not treated as wildcards.
            var like = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(r =>
                EF.Functions.ILike(r.FullName, like, LikeEscape) ||
                EF.Functions.ILike(r.Email, like, LikeEscape) ||
                (r.CompanyName != null && EF.Functions.ILike(r.CompanyName, like, LikeEscape)) ||
                (r.Phone != null && EF.Functions.ILike(r.Phone, like, LikeEscape)));
        }

        return await query.OrderByDescending(r => r.CreatedDate).Take(criteria.Take).ToListAsync(cancellationToken);
    }

    public Task<LicenseRequest?> FindDetailsAsync(long id, CancellationToken cancellationToken = default) =>
        WithDetails(_db.LicenseRequests.AsNoTracking()).FirstOrDefaultAsync(r => r.Id == id && !r.Cancelled, cancellationToken);

    public async Task<LicenseRequest?> FindForUpdateAsync(long id, CancellationToken cancellationToken = default)
    {
        var exists = await _db.LicenseRequests.AsNoTracking().AnyAsync(r => r.Id == id && !r.Cancelled, cancellationToken);
        if (!exists) return null;

        // Lock first, then read: what is seen is whatever the previous lock holder committed.
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"LicenseRequest\" WHERE \"Id\" = {id} FOR UPDATE", cancellationToken);
        return await _db.LicenseRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public Task<int> CountNewAsync(CancellationToken cancellationToken = default) =>
        _db.LicenseRequests.CountAsync(r => r.Status == LicenseRequestStatus.New && !r.Cancelled, cancellationToken);

    public async Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.LicenseRequests.AsNoTracking()
            .Where(r => !r.Cancelled)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Status, r => r.Count);
    }

    private static IQueryable<LicenseRequest> WithDetails(IQueryable<LicenseRequest> query) =>
        query.Include(r => r.Plan).Include(r => r.LicenseCode).Include(r => r.HandledByAdminUser);

    public Task<int> CountOpenByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        _db.LicenseRequests.CountAsync(r => r.Email == normalizedEmail && !r.Cancelled &&
            (r.Status == LicenseRequestStatus.New || r.Status == LicenseRequestStatus.InProgress), cancellationToken);

    public void Add(LicenseRequest request) => _db.LicenseRequests.Add(request);

    public async Task<IReadOnlyList<LicenseRequest>> GetUnlinkedByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        await _db.LicenseRequests.Include(r => r.LicenseCode)
            .Where(r => r.Email == normalizedEmail && r.CustomerAccountId == null && !r.Cancelled)
            .ToListAsync(cancellationToken);
}
