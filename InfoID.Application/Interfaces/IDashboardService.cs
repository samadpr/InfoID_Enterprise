using InfoID.Application.Dtos.Dashboard;

namespace InfoID.Application.Interfaces;

/// <summary>Dashboard metrics (FR-DSH-1) computed from the local Print history tables.</summary>
public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(DashboardQuery query, CancellationToken ct = default);
}
