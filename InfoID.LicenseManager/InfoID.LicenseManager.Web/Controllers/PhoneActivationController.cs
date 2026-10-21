using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.Security;
using InfoID.LicenseManager.Web.ViewModels.Phone;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InfoID.LicenseManager.Web.Controllers;

/// <summary>
/// Offline activation by phone: InfoID shows a QR code that opens <c>/a?c=&lt;request code&gt;</c>; this page answers with
/// the serial key in large letters, or a clear error. Anonymous and rate limited; only works for licenses staff
/// allowed to activate offline. The request code is read from the query but never logged.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Phone)]
public sealed class PhoneActivationController : Controller
{
    private readonly IActivationService _activation;

    public PhoneActivationController(IActivationService activation) => _activation = activation;

    [HttpGet("/a")]
    public async Task<IActionResult> Index(string? c, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(c))
            return View(PhoneActivationViewModel.Failure("This link is incomplete. Scan the QR code in InfoID again."));

        var result = await _activation.IssueForPhoneAsync(c, cancellationToken);
        return View(result.Succeeded
            ? PhoneActivationViewModel.Success(result.Value!)
            : PhoneActivationViewModel.Failure(result.Errors[0]));
    }
}
