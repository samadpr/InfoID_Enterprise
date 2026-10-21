using InfoID.LicenseManager.Application.DTOs.Audit;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>Append-only, hash-chained admin audit log (FR-ADM-3).</summary>
public interface IAuditService
{
    /// <param name="action">Stable action name, e.g. "LicenseCode.Revoked".</param>
    /// <param name="details">Serialised to JSON. Must never contain passwords, keys or serial keys.</param>
    /// <param name="adminUserIdOverride">Who acted, when the caller is not yet the signed-in user (sign-in itself).</param>
    Task LogAsync(string action, object? details = null, long? adminUserIdOverride = null, CancellationToken cancellationToken = default);

    /// <summary>The entries matching the filters, newest first (at most 1,000).</summary>
    Task<IReadOnlyList<AuditLogEntryDto>> SearchAsync(AuditSearchDto search, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditLogEntryDto>> GetRecentAsync(int take = 200, CancellationToken cancellationToken = default);

    /// <summary>Returns null if the whole chain is intact, otherwise the id of the first broken row.</summary>
    Task<long?> VerifyChainAsync(CancellationToken cancellationToken = default);
}
