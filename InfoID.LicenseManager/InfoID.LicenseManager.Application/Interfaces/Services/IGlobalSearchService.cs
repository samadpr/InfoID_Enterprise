using InfoID.LicenseManager.Application.DTOs.Dashboard;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>One search box for activation codes, customers, license requests and plans. Uses the same services (and role checks) as the lists.</summary>
public interface IGlobalSearchService
{
    Task<GlobalSearchResultDto> SearchAsync(string? query, CancellationToken cancellationToken = default);
}
