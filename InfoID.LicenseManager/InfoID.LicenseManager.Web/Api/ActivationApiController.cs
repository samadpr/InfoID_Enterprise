using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.Security;
using InfoID.Shared.Contracts.Licensing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InfoID.LicenseManager.Web.Api;

/// <summary>Online activation, check-in and deactivation (FR-LIC-5, FR-LIC-13, FR-LIC-10).</summary>
[Route("api/v1")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class ActivationApiController : ApiControllerBase
{
    private readonly IOnlineActivationService _activation;

    public ActivationApiController(IOnlineActivationService activation) => _activation = activation;

    [HttpPost("activate")]
    public async Task<OnlineActivationResponse> Activate(OnlineActivationRequest request, CancellationToken cancellationToken)
    {
        var result = await _activation.ActivateAsync(request.ActivationCode, request.MachineFingerprint, request.AppVersion, cancellationToken);
        return new OnlineActivationResponse { Success = result.Succeeded, License = result.License, ErrorMessage = result.Error };
    }

    [HttpPost("checkin")]
    public async Task<LicenseCheckInResponse> CheckIn(LicenseCheckInRequest request, CancellationToken cancellationToken)
    {
        var result = await _activation.CheckInAsync(request.ActivationCode, request.MachineFingerprint, request.CardsIssuedThisPeriod, request.AppVersion, cancellationToken);
        return new LicenseCheckInResponse { Status = result.Status, License = result.License, Message = result.Error };
    }

    [HttpPost("deactivate")]
    public async Task<LicenseDeactivationResponse> Deactivate(LicenseDeactivationRequest request, CancellationToken cancellationToken)
    {
        var result = await _activation.DeactivateAsync(request.ActivationCode, request.MachineFingerprint, cancellationToken);
        return new LicenseDeactivationResponse { Success = result.Succeeded, ErrorMessage = result.Succeeded ? null : result.Errors[0] };
    }
}
