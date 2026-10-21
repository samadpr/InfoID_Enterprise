using InfoID.LicenseManager.Application.DTOs.Dashboard;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>License Dashboard numbers (FR-ADM-5).</summary>
public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
