using InfoID.LicenseManager.Application.DTOs.Accounts;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Web.Security;
using InfoID.Shared.Contracts.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InfoID.LicenseManager.Web.Api;

/// <summary>The app Editions page: the public plan list, and plan requests (signed in or not).</summary>
[Route("api/v1")]
[EnableRateLimiting(RateLimitPolicies.Account)]
public sealed class StoreApiController : ApiControllerBase
{
    private readonly IPlanService _plans;
    private readonly ILicenseRequestService _requests;
    private readonly ICustomerAccountService _accounts;

    public StoreApiController(IPlanService plans, ILicenseRequestService requests, ICustomerAccountService accounts)
    {
        _plans = plans;
        _requests = requests;
        _accounts = accounts;
    }

    [HttpGet("plans")]
    public async Task<List<InfoID.Shared.Contracts.Accounts.PlanDto>> Plans(string? product, CancellationToken ct)
    {
        var code = string.IsNullOrWhiteSpace(product) ? ProductCodes.InfoID : product.Trim().ToUpperInvariant();
        var plans = await _plans.GetActivePlansAsync(code, ct);
        return plans.Select(p => new InfoID.Shared.Contracts.Accounts.PlanDto
        {
            Id = p.Id,
            Name = p.Name,
            Tier = p.Tier,
            Tagline = p.Tagline,
            Features = (p.FeaturesText ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            DurationMonths = p.DurationMonths,
            DefaultMachines = p.DefaultMachines,
            MaxCardsPerPeriod = p.MaxCardsPerPeriod,
            CardLimitPeriod = p.CardLimitPeriod,
            PriceText = p.PriceText,
        }).ToList();
    }

    [HttpPost("requests")]
    public async Task<PlanRequestResponse> Submit(PlanRequestSubmission r, CancellationToken ct)
    {
        // A token that does not resolve is treated as "not signed in", not as an error.
        var signedIn = await GetSessionAsync(_accounts, ct);
        var result = await _requests.SubmitAsync(new SubmitLicenseRequestDto(
            r.PlanId, r.Machines, r.FullName, r.CompanyName, r.Email, r.Phone, r.Country, r.StateProvince, r.Message, r.MachineFingerprint, r.AppVersion), signedIn, ct);
        return new PlanRequestResponse { Success = result.Succeeded, Message = result.Message, RequestId = result.RequestId };
    }
}
