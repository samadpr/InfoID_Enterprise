using InfoID.LicenseManager.Application.DTOs.Dashboard;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Services;

namespace InfoID.LicenseManager.Application.Services;

public sealed class DashboardService : IDashboardService
{
    private readonly ILicenseRepository _licenses;
    private readonly IActivationRepository _activations;
    private readonly TimeProvider _time;

    public DashboardService(ILicenseRepository licenses, IActivationRepository activations, TimeProvider time)
    {
        _licenses = licenses;
        _activations = activations;
        _time = time;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(nowUtc);

        var counts = await _licenses.CountByStatusAsync(cancellationToken);
        return new DashboardSummaryDto(
            counts.Total,
            counts.Available,
            counts.Active,
            counts.Revoked,
            await _activations.CountActiveAsync(cancellationToken),
            await _licenses.CountExpiringAsync(today, 30, cancellationToken),
            await _licenses.CountExpiringAsync(today, 60, cancellationToken),
            await _licenses.CountExpiringAsync(today, 90, cancellationToken),
            await _activations.CountActivatedSinceAsync(nowUtc.AddDays(-30), cancellationToken));
    }
}
