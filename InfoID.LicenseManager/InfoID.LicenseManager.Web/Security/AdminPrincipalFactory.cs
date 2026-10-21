using System.Globalization;
using System.Security.Claims;
using InfoID.LicenseManager.Application.DTOs.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace InfoID.LicenseManager.Web.Security;

public static class AdminPrincipalFactory
{
    public static ClaimsPrincipal Create(AuthenticatedAdminDto admin)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, admin.FullName),
                new Claim(ClaimTypes.Email, admin.Email),
                new Claim(ClaimTypes.Role, admin.Role),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return new ClaimsPrincipal(identity);
    }
}
