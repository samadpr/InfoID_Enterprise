using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Interfaces.Repositories;

/// <summary>Number of activations that started on one UTC day.</summary>
public sealed record DailyCount(DateOnly Day, int Count);

public interface IActivationRepository
{
    /// <summary>Activations per UTC day from <paramref name="fromUtc"/> up to (not including) <paramref name="toUtcExclusive"/>; days without any are omitted.</summary>
    Task<IReadOnlyList<DailyCount>> CountActivationsPerDayAsync(DateTime fromUtc, DateTime toUtcExclusive, CancellationToken cancellationToken = default);

    /// <summary>Machines currently holding an active activation.</summary>
    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

    Task<int> CountActivatedSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Stores the records of machines freed from a code (FR-LIC-10, FR-ADM-8).</summary>
    void AddTransfers(IEnumerable<TransferHistory> transfers);
}
