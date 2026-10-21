namespace InfoID.LicenseManager.Domain.Entities;

/// <summary>A customer's account (sign up in the app). One account for all products.
/// Login is optional: activation codes always work without an account.</summary>
public class CustomerAccount : EntityBase
{
    public string FullName { get; set; } = "";
    public string? CompanyName { get; set; }
    /// <summary>Lower-case, trimmed. Unique.</summary>
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public string PasswordHash { get; set; } = "";
    public bool EmailVerified { get; set; }
    public DateTime? EmailVerifiedDate { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginDate { get; set; }
    /// <summary>Accepted privacy policy / terms at sign-up.</summary>
    public DateTime? TermsAcceptedDate { get; set; }
    /// <summary>Vendor-side organization record, linked when a request is approved.</summary>
    public long? CustomerRecordId { get; set; }
    public CustomerRecord? CustomerRecord { get; set; }

    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public bool IsLockedOut(DateTime utcNow) => LockoutEndUtc is { } until && until > utcNow;

    /// <summary>Counts a wrong password; after <see cref="MaxFailedLogins"/> in a row the account locks for
    /// <see cref="LockoutDuration"/> and the counter restarts.</summary>
    public void RegisterFailedLogin(DateTime utcNow)
    {
        FailedLoginCount++;
        if (FailedLoginCount < MaxFailedLogins) return;

        LockoutEndUtc = utcNow.Add(LockoutDuration);
        FailedLoginCount = 0;
    }

    public void ClearLoginFailures()
    {
        FailedLoginCount = 0;
        LockoutEndUtc = null;
    }

    /// <summary>A disabled account is stored with the soft-delete flag: it cannot sign in, its e-mail stays
    /// reserved, and enabling it restores it.</summary>
    public bool IsDisabled => Cancelled;

    public void Disable() => Cancelled = true;

    public void Enable() => Cancelled = false;
}
