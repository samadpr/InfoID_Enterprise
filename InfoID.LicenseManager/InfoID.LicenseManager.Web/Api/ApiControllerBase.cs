using InfoID.LicenseManager.Application.DTOs.Accounts;
using InfoID.LicenseManager.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InfoID.LicenseManager.Web.Api;

/// <summary>
/// Base of the client-facing JSON API used by InfoID.Desktop. Stateless: no cookies, no staff sign-in, so the CSRF
/// check does not apply (a browser has no credential to ride on); callers are identified by an activation code or a
/// customer bearer token, and every endpoint is rate limited. The routes, JSON shapes and status codes are exactly
/// those of the original InfoID.LicenseApi, because installed desktop apps depend on them.
/// </summary>
[ApiController]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The "Authorization: Bearer ..." token, if the request has one.</summary>
    protected string? BearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
    }

    /// <summary>The verified account behind the bearer token, or null (not signed in, expired, disabled).</summary>
    protected Task<AccountSessionDto?> GetSessionAsync(ICustomerAccountService accounts, CancellationToken cancellationToken) =>
        accounts.GetSessionAsync(BearerToken(), cancellationToken);
}
