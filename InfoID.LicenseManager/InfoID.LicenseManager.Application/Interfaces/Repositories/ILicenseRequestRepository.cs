using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Interfaces.Repositories;

/// <param name="Statuses">Null = any status; otherwise the listed statuses.</param>
public sealed record LicenseRequestSearchCriteria(IReadOnlyList<string>? Statuses, string? Text, int Take);

public interface ILicenseRequestRepository
{
    /// <summary>Untracked, newest first, with plan, license code and handler included.</summary>
    Task<IReadOnlyList<LicenseRequest>> SearchAsync(LicenseRequestSearchCriteria criteria, CancellationToken cancellationToken = default);

    Task<LicenseRequest?> FindDetailsAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracked read that first locks the request row (SELECT ... FOR UPDATE), so two staff members
    /// approving or rejecting the same request queue up and the second sees the first one's decision.
    /// Must be called inside <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>.
    /// </summary>
    Task<LicenseRequest?> FindForUpdateAsync(long id, CancellationToken cancellationToken = default);

    Task<int> CountNewAsync(CancellationToken cancellationToken = default);
    /// <summary>Live requests grouped by status (statuses without any are omitted).</summary>
    Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>New or in-progress requests of one e-mail address (flood protection).</summary>
    Task<int> CountOpenByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    void Add(LicenseRequest request);

    /// <summary>Tracked: requests sent without signing in from this e-mail, not yet linked to any account.</summary>
    Task<IReadOnlyList<LicenseRequest>> GetUnlinkedByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
}
