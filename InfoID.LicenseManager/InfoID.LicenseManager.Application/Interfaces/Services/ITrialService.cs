using InfoID.LicenseManager.Application.DTOs.Trial;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>
/// Trial printing for signed-in customer accounts without an activation code: a fixed number of cards per account,
/// enforced here (the app only asks). Reserve before printing, then commit (printed) or release (failed/cancelled).
/// </summary>
public interface ITrialService
{
    Task<TrialStatusDto> GetStatusAsync(long accountId, CancellationToken cancellationToken = default);

    Task<TrialReserveResultDto> ReserveAsync(long accountId, int cards, string? machineFingerprint, string? appVersion, string? designName, CancellationToken cancellationToken = default);

    Task<TrialCompleteResultDto> CommitAsync(long accountId, Guid reservationKey, int cardsPrinted, CancellationToken cancellationToken = default);

    Task<TrialCompleteResultDto> ReleaseAsync(long accountId, Guid reservationKey, CancellationToken cancellationToken = default);
}
