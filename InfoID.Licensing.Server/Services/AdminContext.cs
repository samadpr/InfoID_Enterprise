using InfoID.Licensing.Server.Entities;

namespace InfoID.Licensing.Server.Services;

/// <summary>The signed-in staff member. The desktop admin app sets it after login;
/// the future web API would set it per request. Services use it for permission checks,
/// CreatedBy/ModifiedBy and the audit log.</summary>
public sealed class AdminContext
{
    public AdminUser? CurrentAdmin { get; private set; }

    /// <summary>Machine name / IP of the workstation, recorded in the audit log (FR-ADM-3).</summary>
    public string? ClientAddress { get; set; }

    public bool IsSignedIn => CurrentAdmin is not null;

    public string? Email => CurrentAdmin?.Email;

    public void SignIn(AdminUser admin) => CurrentAdmin = admin;

    public void SignOut() => CurrentAdmin = null;

    /// <summary>FR-ADM-1 roles: Super Admin and License Manager create/edit licenses;
    /// Support Agent is view-only but may generate offline serial keys.</summary>
    public bool CanManageLicenses => CurrentAdmin?.Role is AdminRoles.SuperAdmin or AdminRoles.LicenseManager;

    public bool CanGenerateSerialKeys => CurrentAdmin is not null;

    public bool IsSuperAdmin => CurrentAdmin?.Role == AdminRoles.SuperAdmin;

    public void Require(bool allowed, string action)
    {
        if (!allowed)
            throw new UnauthorizedAccessException($"Your role ({CurrentAdmin?.Role ?? "not signed in"}) is not allowed to {action}.");
    }
}
