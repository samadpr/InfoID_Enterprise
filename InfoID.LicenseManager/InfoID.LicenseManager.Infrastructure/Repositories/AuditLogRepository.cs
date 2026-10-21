using System.Runtime.CompilerServices;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Infrastructure.Repositories;

internal sealed class AuditLogRepository : IAuditLogRepository
{
    /// <summary>The key InfoID.Licensing.Server already uses, so the web app, the license API and the
    /// desktop manager all queue behind one another when extending the chain.</summary>
    private const long ChainLockKey = 734000101;

    private readonly DbContextOptions<LicenseManagerDbContext> _options;
    private readonly ICurrentUser _currentUser;
    private readonly LicenseManagerDbContext _db;

    public AuditLogRepository(DbContextOptions<LicenseManagerDbContext> options, ICurrentUser currentUser, LicenseManagerDbContext db)
    {
        _options = options;
        _currentUser = currentUser;
        _db = db;
    }

    public async Task AppendAsync(Func<string?, AdminAuditLog> createEntry, CancellationToken cancellationToken = default)
    {
        // A context of its own: the entry commits in its own transaction no matter what the
        // request business changes do (a failed sign-in must still be recorded).
        await using var db = new LicenseManagerDbContext(_options, _currentUser);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({ChainLockKey})", cancellationToken);

        var previousHash = await db.AdminAuditLogs
            .OrderByDescending(a => a.Id)
            .Select(a => a.RecordHash)
            .FirstOrDefaultAsync(cancellationToken);

        db.AdminAuditLogs.Add(createEntry(previousHash));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminAuditLog>> SearchAsync(AuditSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        // Typed %, _ and \ are searched literally, not treated as wildcards.
        static string Like(string text) => "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

        var query = _db.AdminAuditLogs.AsNoTracking().Include(a => a.AdminUser).AsQueryable();
        if (criteria.FromUtc is { } from) query = query.Where(a => a.ActionDate >= from);
        if (criteria.ToUtcExclusive is { } to) query = query.Where(a => a.ActionDate < to);
        if (criteria.Action is { } action)
        {
            var like = Like(action);
            query = query.Where(a => EF.Functions.ILike(a.Action, like, "\\"));
        }

        if (criteria.Who is { } who)
        {
            var like = Like(who);
            query = query.Where(a => a.AdminUser != null && (EF.Functions.ILike(a.AdminUser.FullName, like, "\\") || EF.Functions.ILike(a.AdminUser.Email, like, "\\")));
        }

        return await query.OrderByDescending(a => a.Id).Take(criteria.Take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminAuditLog>> GetRecentAsync(int take, CancellationToken cancellationToken = default) =>
        await _db.AdminAuditLogs.AsNoTracking()
            .Include(a => a.AdminUser)
            .OrderByDescending(a => a.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async IAsyncEnumerable<AdminAuditLog> StreamAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var row in _db.AdminAuditLogs.AsNoTracking().OrderBy(a => a.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
            yield return row;
    }
}
