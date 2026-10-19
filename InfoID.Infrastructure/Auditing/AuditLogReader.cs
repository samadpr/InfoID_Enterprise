using InfoID.Domain.Entities.ApprovalAuditModule;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Infrastructure.Auditing;

public sealed class AuditQuery
{
    /// <summary>Inclusive start, UTC.</summary>
    public DateTime? FromUtc { get; init; }
    /// <summary>Exclusive end, UTC.</summary>
    public DateTime? ToUtc { get; init; }
    public string? Module { get; init; }
    /// <summary>Matched against description, user and item name (case-insensitive).</summary>
    public string? Search { get; init; }
    /// <summary>Paging: only entries older than this id (newest first).</summary>
    public long? BeforeId { get; init; }
    public int Take { get; init; } = 300;
}

public sealed class AuditVerification
{
    /// <summary>True when every entry and the chain verified (an absent anchor alone
    /// doesn't make this false; see <see cref="AnchorMissing"/>).</summary>
    public bool Intact { get; init; }
    public int EntriesChecked { get; init; }
    public long? ProblemEntryId { get; init; }
    public DateTime? ProblemEntryDateUtc { get; init; }
    /// <summary>No anchor file (first use, or it was deleted): removal of the most
    /// recent entries can't be checked.</summary>
    public bool AnchorMissing { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime CheckedAtUtc { get; init; } = DateTime.UtcNow;
}

public interface IAuditLogReader
{
    /// <summary>Entries matching the query, newest first.</summary>
    Task<IReadOnlyList<AuditLog>> QueryAsync(AuditQuery query, CancellationToken ct = default);

    /// <summary>Walks the whole chain and checks every entry (FR-REP-7).</summary>
    Task<AuditVerification> VerifyAsync(IProgress<double>? progress = null, CancellationToken ct = default);
}

public sealed class AuditLogReader : IAuditLogReader
{
    private readonly InfoIdDbContext _db;
    private readonly AuditTrailOptions _options;

    public AuditLogReader(InfoIdDbContext db, AuditTrailOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<IReadOnlyList<AuditLog>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        IQueryable<AuditLog> q = _db.AuditLogs.AsNoTracking();
        if (query.FromUtc is { } from) q = q.Where(a => a.ActionDate >= from);
        if (query.ToUtc is { } to) q = q.Where(a => a.ActionDate < to);
        if (!string.IsNullOrWhiteSpace(query.Module)) q = q.Where(a => a.Module == query.Module);
        if (query.BeforeId is { } before) q = q.Where(a => a.Id < before);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Typed text is matched literally: % and _ are escaped for LIKE.
            var pattern = "%" + query.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            q = q.Where(a => EF.Functions.Like(a.Description ?? "", pattern, "\\")
                             || EF.Functions.Like(a.CreatedBy ?? "", pattern, "\\")
                             || EF.Functions.Like(a.Action, pattern, "\\")
                             || EF.Functions.Like(a.AfterValueJson ?? "", pattern, "\\")
                             || EF.Functions.Like(a.BeforeValueJson ?? "", pattern, "\\"));
        }
        return await q.OrderByDescending(a => a.Id).Take(Math.Clamp(query.Take, 1, 5000)).ToListAsync(ct);
    }

    public async Task<AuditVerification> VerifyAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var total = await _db.AuditLogs.CountAsync(ct);
        var (anchor, anchorTampered) = AuditAnchor.Read(_options.AnchorPath);

        string? previousHash = null;
        long lastId = 0;
        var checkedCount = 0;
        var anchorSeen = false;

        await foreach (var entry in _db.AuditLogs.AsNoTracking().OrderBy(a => a.Id).AsAsyncEnumerable().WithCancellation(ct))
        {
            if (!string.Equals(entry.PreviousHash, previousHash, StringComparison.Ordinal))
                return Broken(entry, checkedCount,
                    checkedCount == 0
                        ? $"The oldest entries were removed: entry #{entry.Id} isn't the start of the log."
                        : $"Entries were removed or inserted just before entry #{entry.Id} ({Local(entry.ActionDate):dd MMM yyyy HH:mm}).");

            if (!AuditHasher.Matches(entry))
                return Broken(entry, checkedCount,
                    $"Entry #{entry.Id} ({Local(entry.ActionDate):dd MMM yyyy HH:mm}) was changed after it was written.");

            if (anchor is not null && entry.Id == anchor.LastId)
            {
                anchorSeen = true;
                if (!string.Equals(entry.RecordHash, anchor.LastHash, StringComparison.Ordinal))
                    return Broken(entry, checkedCount, $"Entry #{entry.Id} doesn't match the last recorded state of the log.");
            }

            previousHash = entry.RecordHash;
            lastId = entry.Id;
            checkedCount++;
            if (checkedCount % 500 == 0) progress?.Report(total == 0 ? 1 : checkedCount / (double)total);
        }
        progress?.Report(1);

        if (anchorTampered)
            return new AuditVerification
            {
                Intact = false, EntriesChecked = checkedCount,
                Message = "The audit log's anchor file was modified, so removal of the most recent entries can't be ruled out.",
            };

        if (anchor is not null && !anchorSeen)
            return new AuditVerification
            {
                Intact = false, EntriesChecked = checkedCount, ProblemEntryId = anchor.LastId,
                Message = checkedCount == 0
                    ? "All audit entries were removed."
                    : $"The most recent entries were removed: the log ends at #{lastId}, but it went up to #{anchor.LastId}.",
            };

        return new AuditVerification
        {
            Intact = true,
            EntriesChecked = checkedCount,
            AnchorMissing = anchor is null && checkedCount > 0,
            Message = checkedCount == 0
                ? "The audit log is empty."
                : anchor is null
                    ? $"All {checkedCount:N0} entries verified. (Removal of the newest entries can't be checked: the anchor file is missing.)"
                    : $"All {checkedCount:N0} entries verified. No changes or deletions found.",
        };
    }

    private static DateTime Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    private static AuditVerification Broken(AuditLog entry, int checkedCount, string message) => new()
    {
        Intact = false,
        EntriesChecked = checkedCount,
        ProblemEntryId = entry.Id,
        ProblemEntryDateUtc = entry.ActionDate,
        Message = message,
    };
}
