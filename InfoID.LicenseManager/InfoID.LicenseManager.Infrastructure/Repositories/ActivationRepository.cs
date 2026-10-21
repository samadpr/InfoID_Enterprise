using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Infrastructure.Repositories;

internal sealed class ActivationRepository : IActivationRepository
{
    private readonly LicenseManagerDbContext _db;

    public ActivationRepository(LicenseManagerDbContext db) => _db = db;

    public Task<int> CountActiveAsync(CancellationToken cancellationToken = default) =>
        _db.ActivationRecords.AsNoTracking()
            .CountAsync(a => a.Status == ActivationStatus.Active && !a.Cancelled, cancellationToken);

    public Task<int> CountActivatedSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        _db.ActivationRecords.AsNoTracking().CountAsync(a => a.ActivationDate >= sinceUtc, cancellationToken);

    public async Task<IReadOnlyList<DailyCount>> CountActivationsPerDayAsync(DateTime fromUtc, DateTime toUtcExclusive, CancellationToken cancellationToken = default)
    {
        var rows = await _db.ActivationRecords.AsNoTracking()
            .Where(a => a.ActivationDate >= fromUtc && a.ActivationDate < toUtcExclusive)
            .GroupBy(a => a.ActivationDate.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new DailyCount(DateOnly.FromDateTime(r.Day), r.Count)).ToList();
    }

    public void AddTransfers(IEnumerable<TransferHistory> transfers) => _db.TransferHistories.AddRange(transfers);
}
