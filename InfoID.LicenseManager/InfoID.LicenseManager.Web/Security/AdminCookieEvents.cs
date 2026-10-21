using System.Security.Claims;
using InfoID.LicenseManager.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace InfoID.LicenseManager.Web.Security;

/// <summary>
/// Re-checks the signed-in staff member on every request. A cookie alone is not enough: if the
/// account was disabled, locked, or its role changed since sign-in, the session ends immediately
/// instead of living on until the cookie expires.
/// </summary>
public sealed class AdminCookieEvents : CookieAuthenticationEvents
{
    private readonly IAuthService _auth;

    public AdminCookieEvents(IAuthService auth) => _auth = auth;

    /// <summary>A signed-in user whose role is not enough gets a plain 403 (rendered as the friendly
    /// "Access denied" page), the same answer the application layer's own role check produces.</summary>
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var idText = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idText, out var adminId))
        {
            await Reject(context);
            return;
        }

        var current = await _auth.GetActiveAdminAsync(adminId, context.HttpContext.RequestAborted);
        if (current is null || current.Role != principal!.FindFirstValue(ClaimTypes.Role))
            await Reject(context);
    }

    private static async Task Reject(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
