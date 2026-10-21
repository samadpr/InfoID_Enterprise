using InfoID.LicenseManager.Application.DTOs.Customers;
using InfoID.LicenseManager.Application.DTOs.Dashboard;
using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Application.DTOs.Requests;
using InfoID.LicenseManager.Application.Interfaces.Services;

namespace InfoID.LicenseManager.Application.Services;

public sealed class GlobalSearchService : IGlobalSearchService
{
    public const int MinLength = 2;
    public const int MaxLength = 100;
    public const int PerKind = 8;

    private readonly ILicenseService _licenses;
    private readonly ICustomerAdminService _customers;
    private readonly ILicenseRequestService _requests;
    private readonly IPlanService _plans;

    public GlobalSearchService(ILicenseService licenses, ICustomerAdminService customers, ILicenseRequestService requests, IPlanService plans)
    {
        _licenses = licenses;
        _customers = customers;
        _requests = requests;
        _plans = plans;
    }

    public async Task<GlobalSearchResultDto> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        var text = (query ?? "").Trim();
        if (text.Length > MaxLength) text = text[..MaxLength];
        if (text.Length < MinLength) return GlobalSearchResultDto.Empty(text);

        var licenses = await _licenses.SearchAsync(new LicenseSearchDto(text, Take: PerKind), cancellationToken);
        var customers = await _customers.SearchAsync(new CustomerSearchDto(CustomerFilter.All, text, PerKind), cancellationToken);
        var requests = await _requests.SearchAsync(new LicenseRequestSearchDto(RequestStatusFilter.All, text, PerKind), cancellationToken);
        var plans = (await _plans.GetAllAsync(cancellationToken))
            .Where(p => Has(p.Name, text) || Has(p.Tier, text) || Has(p.Tagline, text))
            .Take(PerKind)
            .ToList();

        return new GlobalSearchResultDto(text, licenses, customers, requests, plans);
    }

    private static bool Has(string? value, string text) => value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
}
