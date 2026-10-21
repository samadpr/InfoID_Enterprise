using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Interfaces.Repositories;

public interface IAdminUserRepository
{
    /// <summary>True when at least one non-cancelled staff account exists.</summary>
    Task<bool> AnyActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Tracked, so the caller can record login attempts and save through the unit of work.</summary>
    Task<AdminUser?> FindActiveByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    /// <summary>Untracked read used to re-validate a signed-in session.</summary>
    Task<AdminUser?> FindActiveByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// First-run setup: inserts <paramref name="admin"/> only if no staff account exists, as one
    /// atomic step (two simultaneous setups cannot both succeed). Saves immediately.
    /// </summary>
    /// <returns>False when an administrator already existed.</returns>
    Task<bool> TryAddFirstAdminAsync(AdminUser admin, CancellationToken cancellationToken = default);
}
