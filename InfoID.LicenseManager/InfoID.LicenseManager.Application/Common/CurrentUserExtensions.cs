using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Common;

/// <summary>Role checks the services apply regardless of how they were called (FR-ADM-1).</summary>
public static class CurrentUserExtensions
{
    public static void RequireManageLicenses(this ICurrentUser user, string action) =>
        Require(user, AdminRoles.CanManageLicenses(user.Role), action);

    /// <summary>Any signed-in staff role (read access and support actions).</summary>
    public static void RequireStaff(this ICurrentUser user, string action) =>
        Require(user, user.IsAuthenticated && AdminRoles.All.Contains(user.Role ?? ""), action);

    public static void RequireSerialKeyGeneration(this ICurrentUser user) =>
        Require(user, AdminRoles.CanGenerateSerialKeys(user.Role), "generate serial keys");

    public static void RequireSuperAdmin(this ICurrentUser user, string action) =>
        Require(user, AdminRoles.IsSuperAdmin(user.Role), action);

    private static void Require(ICurrentUser user, bool allowed, string action)
    {
        if (!allowed)
            throw new ForbiddenException($"Role {user.Role ?? "(not signed in)"} is not allowed to {action}.");
    }
}
