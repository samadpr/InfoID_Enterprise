using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Infrastructure.Repositories;

internal sealed class LicenseRepository : ILicenseRepository
{
    private const string LikeEscape = "\\";

    private readonly LicenseManagerDbContext _db;

    public LicenseRepository(LicenseManagerDbContext db) => _db = db;

    public async Task<LicenseStatusCounts> CountByStatusAsync(CancellationToken cancellationToken = default)
    {
        var byStatus = await _db.LicenseCodes.AsNoTracking()
            .Where(c => !c.Cancelled)
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Of(string status) => byStatus.FirstOrDefault(s => s.Status == status)?.Count ?? 0;
        return new LicenseStatusCounts(byStatus.Sum(s => s.Count), Of(LicenseCodeStatus.Available), Of(LicenseCodeStatus.Active), Of(LicenseCodeStatus.Revoked));
    }

    public async Task<LicenseLifecycleCounts> CountLifecycleAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        var live = _db.LicenseCodes.AsNoTracking().Where(c => !c.Cancelled);
        var notRevoked = live.Where(c => c.Status != LicenseCodeStatus.Revoked);
        var expired = notRevoked.Where(c => c.ExpiryDate != null && c.ExpiryDate < today);
        var current = notRevoked.Where(c => c.ExpiryDate == null || c.ExpiryDate >= today);

        return new LicenseLifecycleCounts(
            Available: await current.CountAsync(c => c.Status == LicenseCodeStatus.Available, cancellationToken),
            Active: await current.CountAsync(c => c.Status == LicenseCodeStatus.Active, cancellationToken),
            Expired: await expired.CountAsync(cancellationToken),
            Revoked: await live.CountAsync(c => c.Status == LicenseCodeStatus.Revoked, cancellationToken));
    }

    public async Task<IReadOnlyList<TopCustomerRow>> GetTopCustomersAsync(int take, CancellationToken cancellationToken = default)
    {
        // A customer can have several records (every approved request makes its own), so they are grouped by name.
        var holders = await _db.LicenseCodes.AsNoTracking()
            .Where(c => !c.Cancelled && c.CustomerRecord != null && c.CustomerRecord.OrganizationName != "")
            .GroupBy(c => c.CustomerRecord!.OrganizationName.ToLower())
            .Select(g => new { Key = g.Key, Name = g.Min(c => c.CustomerRecord!.OrganizationName), Licenses = g.Count() })
            .OrderByDescending(g => g.Licenses).ThenBy(g => g.Name)
            .Take(take)
            .ToListAsync(cancellationToken);
        if (holders.Count == 0) return [];

        var keys = holders.Select(h => h.Key).ToList();
        var machines = await _db.ActivationRecords.AsNoTracking()
            .Where(a => a.Status == ActivationStatus.Active && !a.Cancelled && a.LicenseCode!.CustomerRecord != null
                        && keys.Contains(a.LicenseCode.CustomerRecord.OrganizationName.ToLower()))
            .GroupBy(a => a.LicenseCode!.CustomerRecord!.OrganizationName.ToLower())
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return holders.Select(h => new TopCustomerRow(h.Name, h.Licenses, machines.FirstOrDefault(m => m.Key == h.Key)?.Count ?? 0)).ToList();
    }

    public Task<int> CountExpiringAsync(DateOnly today, int days, CancellationToken cancellationToken = default)
    {
        var last = today.AddDays(days);
        return _db.LicenseCodes.AsNoTracking()
            .CountAsync(c => !c.Cancelled && c.Status != LicenseCodeStatus.Revoked && c.ExpiryDate >= today && c.ExpiryDate <= last, cancellationToken);
    }

    public async Task<IReadOnlyList<LicenseListItemDto>> SearchAsync(LicenseSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var query = _db.LicenseCodes.AsNoTracking().Where(c => !c.Cancelled);

        if (criteria.BatchId is { } batchId)
            query = query.Where(c => c.BatchId == batchId);

        if (criteria.Status is { } status)
            query = query.Where(c => c.Status == status);

        if (criteria.Text is { } text)
        {
            // Typed %, _ and \ are searched literally, not treated as wildcards.
            var like = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            var useCode = criteria.CodeFragment is { Length: >= 4 };
            var codeFragment = criteria.CodeFragment ?? "";
            query = query.Where(c =>
                (useCode && c.ActivationCode.Contains(codeFragment)) ||
                (c.CustomerRecord != null && (EF.Functions.ILike(c.CustomerRecord.OrganizationName, like, LikeEscape) ||
                                              EF.Functions.ILike(c.CustomerRecord.ContactEmail ?? "", like, LikeEscape))) ||
                c.Activations.Any(a => a.SerialKeyIssued != null && EF.Functions.ILike(a.SerialKeyIssued, like, LikeEscape)) ||
                c.Activations.Any(a => EF.Functions.ILike(a.MachineFingerprint, like, LikeEscape)));
        }

        return await query
            .OrderByDescending(c => c.Id)
            .Take(criteria.Take)
            .Select(c => new LicenseListItemDto(
                c.Id,
                c.ActivationCode,
                c.Tier,
                c.LicenseType,
                c.ExpiryDate,
                c.MaxCardsPerPeriod,
                c.Status,
                c.Activations.Count(a => a.Status == ActivationStatus.Active && !a.Cancelled),
                c.MaxActivations,
                c.CustomerRecord != null ? c.CustomerRecord.OrganizationName : null,
                c.CreatedDate,
                c.AllowOfflineActivation))
            .ToListAsync(cancellationToken);
    }

    public Task<LicenseCode?> FindDetailsAsync(long id, CancellationToken cancellationToken = default) =>
        _db.LicenseCodes.AsNoTracking()
            .Include(c => c.CustomerRecord)
            .Include(c => c.Activations)
            .FirstOrDefaultAsync(c => c.Id == id && !c.Cancelled, cancellationToken);

    public Task<LicenseCode?> FindByActivationCodeAsync(string normalizedCode, CancellationToken cancellationToken = default) =>
        _db.LicenseCodes.AsNoTracking()
            .Include(c => c.CustomerRecord)
            .Include(c => c.Activations)
            .FirstOrDefaultAsync(c => c.ActivationCode == normalizedCode && !c.Cancelled, cancellationToken);

    public async Task<LicenseCode?> FindForUpdateAsync(long id, CancellationToken cancellationToken = default)
    {
        var exists = await _db.LicenseCodes.AsNoTracking().AnyAsync(c => c.Id == id && !c.Cancelled, cancellationToken);
        return exists ? await LockAndLoadAsync(id, cancellationToken) : null;
    }

    public async Task<LicenseCode?> FindForUpdateByActivationCodeAsync(string normalizedCode, CancellationToken cancellationToken = default)
    {
        var id = await _db.LicenseCodes.AsNoTracking()
            .Where(c => c.ActivationCode == normalizedCode && !c.Cancelled)
            .Select(c => (long?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id is { } found ? await LockAndLoadAsync(found, cancellationToken) : null;
    }

    public async Task<IReadOnlySet<string>> GetExistingActivationCodesAsync(IReadOnlyCollection<string> candidates, CancellationToken cancellationToken = default)
    {
        var existing = await _db.LicenseCodes.AsNoTracking()
            .Where(c => candidates.Contains(c.ActivationCode))
            .Select(c => c.ActivationCode)
            .ToListAsync(cancellationToken);
        return existing.ToHashSet();
    }

    public void AddRange(IEnumerable<LicenseCode> codes) => _db.LicenseCodes.AddRange(codes);

    /// <summary>Takes the row lock first, then reads: the data seen is whatever the previous lock holder committed.</summary>
    private async Task<LicenseCode?> LockAndLoadAsync(long id, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"LicenseCode\" WHERE \"Id\" = {id} FOR UPDATE", cancellationToken);
        return await _db.LicenseCodes
            .Include(c => c.CustomerRecord)
            .Include(c => c.Activations)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }
}
