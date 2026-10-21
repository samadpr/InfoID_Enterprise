using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Interfaces.Repositories;

public interface IEmailCodeRepository
{
    /// <summary>Tracked: the account's not-yet-used codes for one purpose.</summary>
    Task<IReadOnlyList<EmailCode>> GetUnusedAsync(long accountId, string purpose, CancellationToken cancellationToken = default);

    void Add(EmailCode code);

    /// <summary>Tracked: the newest not-yet-used code for one purpose.</summary>
    Task<EmailCode?> FindLatestUnusedAsync(long accountId, string purpose, CancellationToken cancellationToken = default);
}
