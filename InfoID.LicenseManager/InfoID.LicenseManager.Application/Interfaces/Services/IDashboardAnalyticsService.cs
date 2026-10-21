using InfoID.LicenseManager.Application.DTOs.Dashboard;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>The richer dashboard: pending work, lifecycle mix, activation trend, requests, top customers and recent activity.</summary>
public interface IDashboardAnalyticsService
{
    /// <param name="range">Null means the last 30 days. It is clamped: never in the future, at most 366 days.</param>
    Task<DashboardOverviewDto> GetOverviewAsync(DashboardRangeDto? range = null, CancellationToken cancellationToken = default);

    /// <summary>New plus in-progress license requests (the badge in the page header).</summary>
    Task<int> GetPendingRequestCountAsync(CancellationToken cancellationToken = default);
}
