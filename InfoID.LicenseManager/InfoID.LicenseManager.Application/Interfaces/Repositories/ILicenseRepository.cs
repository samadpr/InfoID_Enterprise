using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Interfaces.Repositories;

/// <summary>Activation-code counts by lifecycle status (non-cancelled codes only).</summary>
public sealed record LicenseStatusCounts(int Total, int Available, int Active, int Revoked);

/// <summary>Every live code in exactly one bucket: revoked; expired (past its expiry date, not revoked); otherwise active or available.</summary>
public sealed record LicenseLifecycleCounts(int Available, int Active, int Expired, int Revoked);

/// <summary>Codes held by one customer record, with the machines currently active on them.</summary>
public sealed record TopCustomerRow(string Name, int Licenses, int ActiveMachines);

public interface ILicenseRepository
{
    Task<LicenseLifecycleCounts> CountLifecycleAsync(DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Customers with the most codes (customers without a name are skipped), most first.</summary>
    Task<IReadOnlyList<TopCustomerRow>> GetTopCustomersAsync(int take, CancellationToken cancellationToken = default);

    Task<LicenseStatusCounts> CountByStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Codes that are not revoked and expire from <paramref name="today"/> up to and
    /// including <paramref name="today"/> + <paramref name="days"/>.</summary>
    Task<int> CountExpiringAsync(DateOnly today, int days, CancellationToken cancellationToken = default);

    /// <summary>Search list rows (activation code still in its stored, normalised form).</summary>
    Task<IReadOnlyList<LicenseListItemDto>> SearchAsync(LicenseSearchCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>Untracked read for display: customer and activations included.</summary>
    Task<LicenseCode?> FindDetailsAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Untracked read by normalised activation code: customer and activations included.</summary>
    Task<LicenseCode?> FindByActivationCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracked read that first takes a database row lock on the code (SELECT ... FOR UPDATE), then loads
    /// it with its activations. Every operation that changes a code or its machine slots uses this so
    /// concurrent requests queue up instead of both passing the same slot check. Must be called inside
    /// <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>; the lock is released when it ends.
    /// </summary>
    Task<LicenseCode?> FindForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Same as <see cref="FindForUpdateAsync"/>, looked up by normalised activation code.</summary>
    Task<LicenseCode?> FindForUpdateByActivationCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);

    /// <summary>Which of the candidate codes already exist (so a freshly generated one is never reused).</summary>
    Task<IReadOnlySet<string>> GetExistingActivationCodesAsync(IReadOnlyCollection<string> candidates, CancellationToken cancellationToken = default);

    void AddRange(IEnumerable<LicenseCode> codes);
}
