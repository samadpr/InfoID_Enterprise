using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Interfaces.Repositories;

/// <param name="FromUtc">Entries at or after this moment (null = no lower bound).</param>
/// <param name="ToUtcExclusive">Entries before this moment (null = no upper bound).</param>
/// <param name="Who">Part of the staff member's name or e-mail address.</param>
/// <param name="Action">Part of the action name, e.g. "Revoked".</param>
public sealed record AuditSearchCriteria(DateTime? FromUtc, DateTime? ToUtcExclusive, string? Who, string? Action, int Take);

public interface IAuditLogRepository
{
    /// <summary>Newest first, filtered in the database; typed %, _ and \ are searched literally.</summary>
    Task<IReadOnlyList<AdminAuditLog>> SearchAsync(AuditSearchCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends one entry to the hash chain. Runs in its own transaction, independent of any
    /// pending business changes, and serialises writers across every process sharing the
    /// database (web app, license API, desktop manager) so the chain stays strictly sequential.
    /// </summary>
    /// <param name="createEntry">Builds the entry from the hash of the current last row (null when empty).</param>
    Task AppendAsync(Func<string?, AdminAuditLog> createEntry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminAuditLog>> GetRecentAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>All rows in ascending id order, streamed so a large log is never loaded at once.</summary>
    IAsyncEnumerable<AdminAuditLog> StreamAllAsync(CancellationToken cancellationToken = default);
}
