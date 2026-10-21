namespace InfoID.LicenseManager.Application.Common;

/// <summary>
/// The caller of the current operation, supplied by the host (the web app reads it from
/// the signed-in staff member's claims; background work and API calls have no staff user).
/// Used for permission checks, CreatedBy/ModifiedBy stamping and the audit trail, so
/// the application and infrastructure layers never touch HttpContext.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>AdminUser.Id of the signed-in staff member, null when not staff.</summary>
    long? AdminUserId { get; }

    /// <summary>Staff e-mail, written to CreatedBy/ModifiedBy and the audit log.</summary>
    string? Email { get; }

    /// <summary>One of <c>AdminRoles</c>, null when not staff.</summary>
    string? Role { get; }

    /// <summary>Caller's network address, recorded in the audit log (FR-ADM-3).</summary>
    string? ClientAddress { get; }
}
