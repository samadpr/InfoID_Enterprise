using AppTrialStatus = InfoID.LicenseManager.Application.DTOs.Trial.TrialStatusDto;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.Security;
using InfoID.Shared.Contracts.Accounts;
using ContractTrialStatus = InfoID.Shared.Contracts.Accounts.TrialStatusDto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InfoID.LicenseManager.Web.Api;

/// <summary>Trial printing for signed-in accounts without an activation code. The server owns the count: reserve before
/// printing, then commit (printed) or release. Every call needs a valid bearer token.</summary>
[Route("api/v1/trial")]
[EnableRateLimiting(RateLimitPolicies.Trial)]
public sealed class TrialApiController : ApiControllerBase
{
    private readonly ICustomerAccountService _accounts;
    private readonly ITrialService _trials;

    public TrialApiController(ICustomerAccountService accounts, ITrialService trials)
    {
        _accounts = accounts;
        _trials = trials;
    }

    [HttpGet("")]
    public async Task<ActionResult<ContractTrialStatus>> Status(CancellationToken ct)
    {
        if (await GetSessionAsync(_accounts, ct) is not { } session) return Unauthorized();
        return ToDto(await _trials.GetStatusAsync(session.AccountId, ct), new ContractTrialStatus { Success = true });
    }

    [HttpPost("reserve")]
    public async Task<ActionResult<TrialReserveResponse>> Reserve(TrialReserveRequest r, CancellationToken ct)
    {
        if (await GetSessionAsync(_accounts, ct) is not { } session) return Unauthorized();
        var result = await _trials.ReserveAsync(session.AccountId, r.Cards, r.MachineFingerprint, r.AppVersion, r.DesignName, ct);
        return ToDto(result.Status, new TrialReserveResponse
        {
            Success = result.Succeeded,
            Message = result.Error,
            ReservationId = result.ReservationKey,
            ExpiresUtc = result.ExpiresUtc,
        });
    }

    [HttpPost("commit")]
    public async Task<ActionResult<ContractTrialStatus>> Commit(TrialCompleteRequest r, CancellationToken ct)
    {
        if (await GetSessionAsync(_accounts, ct) is not { } session) return Unauthorized();
        var result = await _trials.CommitAsync(session.AccountId, r.ReservationId, r.CardsPrinted, ct);
        return ToDto(result.Status, new ContractTrialStatus { Success = result.Succeeded, Message = result.Error });
    }

    [HttpPost("release")]
    public async Task<ActionResult<ContractTrialStatus>> Release(TrialCompleteRequest r, CancellationToken ct)
    {
        if (await GetSessionAsync(_accounts, ct) is not { } session) return Unauthorized();
        var result = await _trials.ReleaseAsync(session.AccountId, r.ReservationId, ct);
        return ToDto(result.Status, new ContractTrialStatus { Success = result.Succeeded, Message = result.Error });
    }

    private static T ToDto<T>(AppTrialStatus status, T dto) where T : ContractTrialStatus
    {
        dto.CardLimit = status.Limit;
        dto.CardsUsed = status.Used;
        dto.CardsReserved = status.Reserved;
        dto.CardsRemaining = status.Remaining;
        return dto;
    }
}

