using InfoID.LicenseManager.Application.DTOs.Customers;
using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Application.DTOs.Plans;
using InfoID.LicenseManager.Application.DTOs.Requests;

namespace InfoID.LicenseManager.Application.DTOs.Dashboard;

/// <summary>Matches of the header search across the things staff look up. Each list is capped; the page links to the full lists.</summary>
public sealed record GlobalSearchResultDto(
    string Query,
    IReadOnlyList<LicenseListItemDto> Licenses,
    IReadOnlyList<CustomerListItemDto> Customers,
    IReadOnlyList<LicenseRequestDto> Requests,
    IReadOnlyList<PlanDto> Plans)
{
    public int Total => Licenses.Count + Customers.Count + Requests.Count + Plans.Count;

    public static GlobalSearchResultDto Empty(string query) => new(query, [], [], [], []);
}
