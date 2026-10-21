namespace InfoID.LicenseManager.Domain.Entities;

public static class AdminRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string LicenseManager = "LicenseManager";
    public const string SupportAgent = "SupportAgent";

    public static readonly string[] All = [SuperAdmin, LicenseManager, SupportAgent];

    /// <summary>FR-ADM-1: Super Admin and License Manager create, edit and revoke licenses;
    /// Support Agent is view-only (but may generate offline serial keys).</summary>
    public static bool CanManageLicenses(string? role) => role is SuperAdmin or LicenseManager;

    /// <summary>Every signed-in staff member may generate offline serial keys.</summary>
    public static bool CanGenerateSerialKeys(string? role) => role is not null && All.Contains(role);

    public static bool IsSuperAdmin(string? role) => role == SuperAdmin;
}

/// <summary>Module N AdminUser (FR-ADM-1/2) + lockout and TOTP fields.</summary>
public class AdminUser : EntityBase
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = AdminRoles.SupportAgent;
    public bool Is2FAEnabled { get; set; }
    /// <summary>Added: TOTP secret for 2FA (FR-ADM-2), stored encrypted. Next step.</summary>
    public string? TotpSecretEncrypted { get; set; }
    /// <summary>Added: brute-force protection.</summary>
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginDate { get; set; }

    public bool IsLockedOut(DateTime utcNow) => LockoutEndUtc is { } until && until > utcNow;

    /// <summary>Counts a wrong password; after <see cref="MaxFailedLogins"/> in a row the
    /// account locks for <see cref="LockoutDuration"/> and the counter restarts.</summary>
    public void RegisterFailedLogin(DateTime utcNow)
    {
        FailedLoginCount++;
        if (FailedLoginCount < MaxFailedLogins) return;

        LockoutEndUtc = utcNow.Add(LockoutDuration);
        FailedLoginCount = 0;
    }

    public void RegisterSuccessfulLogin(DateTime utcNow)
    {
        FailedLoginCount = 0;
        LockoutEndUtc = null;
        LastLoginDate = utcNow;
    }
}
