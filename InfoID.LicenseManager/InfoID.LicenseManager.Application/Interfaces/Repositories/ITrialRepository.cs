using InfoID.LicenseManager.Application.DTOs.Trial;

namespace InfoID.LicenseManager.Application.Interfaces.Repositories;

/// <summary>
/// Trial printing storage. Every limit check is ONE conditional database update ("add N to Reserved only if
/// Used + Reserved + N is within the limit"), which PostgreSQL applies atomically per row, so any number of
/// simultaneous print requests can never exceed the limit. The server is the only place the count lives, so
/// reinstalling the app cannot reset it.
/// </summary>
public interface ITrialRepository
{
    /// <summary>The account's allowance row, created with <paramref name="defaultLimit"/> on first use (two first
    /// requests at once create it once).</summary>
    Task<long> EnsureUsageAsync(long accountId, string productCode, int defaultLimit, CancellationToken cancellationToken = default);

    /// <summary>Gives back credits held by reservations that were never reported on and have expired.</summary>
    Task<int> ExpireStaleAsync(long usageId, DateTime utcNow, CancellationToken cancellationToken = default);

    Task<TrialStatusDto> ReadStatusAsync(long usageId, CancellationToken cancellationToken = default);

    /// <summary>Atomically holds <paramref name="cards"/> credits and records the reservation. False when the limit would be exceeded.</summary>
    Task<bool> TryReserveAsync(long usageId, long accountId, int cards, Guid reservationKey, DateTime expiresUtc, DateTime utcNow,
        string? designName, string? machineFingerprint, string? appVersion, CancellationToken cancellationToken = default);

    /// <summary>Counts the cards actually printed. Idempotent: committing twice counts once. A reservation that already
    /// expired still counts what was printed. Null when the reservation is not this account's.</summary>
    Task<long?> CommitAsync(long accountId, Guid reservationKey, int cardsPrinted, DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>The print failed or was cancelled: gives the credits back. Idempotent. Null when the reservation is not this account's.</summary>
    Task<long?> ReleaseAsync(long accountId, Guid reservationKey, DateTime utcNow, CancellationToken cancellationToken = default);
}
