using System.Security.Claims;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Web.Security;

public static class ClaimsPrincipalExtensions
{
    public static string? Role(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Role);

    /// <summary>Used by views to decide which buttons to offer; the server enforces the same rule on every action.</summary>
    public static bool CanManageLicenses(this ClaimsPrincipal user) => AdminRoles.CanManageLicenses(user.Role());

    public static bool IsSuperAdmin(this ClaimsPrincipal user) => AdminRoles.IsSuperAdmin(user.Role());
}
