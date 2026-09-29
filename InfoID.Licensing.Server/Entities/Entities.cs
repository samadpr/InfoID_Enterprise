namespace InfoID.Licensing.Server.Entities;

/// <summary>Common base fields (Domain Model section 1). Dates are UTC (PostgreSQL
/// timestamptz); CreatedDate/ModifiedDate/By are set automatically by LicenseDbContext.</summary>
public abstract class EntityBase
{
    public long Id { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public bool Cancelled { get; set; }
}

public static class AdminRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string LicenseManager = "LicenseManager";
    public const string SupportAgent = "SupportAgent";

    public static readonly string[] All = [SuperAdmin, LicenseManager, SupportAgent];
}

public static class LicenseCodeStatus
{
    public const string Available = "Available";   // created, never activated
    public const string Active = "Active";         // at least one active machine
    public const string Revoked = "Revoked";
}

public static class ActivationStatus
{
    public const string Active = "Active";
    public const string Deactivated = "Deactivated";
    public const string Blacklisted = "Blacklisted";
}

/// <summary>Module N AdminUser (FR-ADM-1/2) + lockout and TOTP fields.</summary>
public class AdminUser : EntityBase
{
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
}

/// <summary>Module N CustomerRecord (FR-ADM-10).</summary>
public class CustomerRecord : EntityBase
{
    public string OrganizationName { get; set; } = "";
    public string? OrganizationType { get; set; }
    public string? Country { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }

    public List<LicenseCode> LicenseCodes { get; set; } = new();
}

/// <summary>Module N LicenseCode (FR-ADM-4) + fields the SRS needs that the table
/// lacked: status, fixed expiry, limit period, activation slots, batch id.</summary>
public class LicenseCode : EntityBase
{
    /// <summary>Normalized 16-character code (no dashes). Unique.</summary>
    public string ActivationCode { get; set; } = "";
    public string Tier { get; set; } = "Standard";
    public string LicenseType { get; set; } = "Subscription";
    public int? MaxCardsPerPeriod { get; set; }
    /// <summary>Added: Monthly / Yearly.</summary>
    public string CardLimitPeriod { get; set; } = "Monthly";
    /// <summary>Added: subscription end date (null for perpetual).</summary>
    public DateOnly? ExpiryDate { get; set; }
    /// <summary>Added: how many machines may be active at once. Default 1.</summary>
    public int MaxActivations { get; set; } = 1;
    /// <summary>Added: Available / Active / Revoked.</summary>
    public string Status { get; set; } = LicenseCodeStatus.Available;
    /// <summary>Added: groups codes created together (FR-ADM-4 batch).</summary>
    public Guid? BatchId { get; set; }
    public DateTime? RevokedDate { get; set; }
    public long? CustomerRecordId { get; set; }
    public CustomerRecord? CustomerRecord { get; set; }
    public bool IsUsed { get; set; }
    public long? CreatedByAdminUserId { get; set; }
    public AdminUser? CreatedByAdminUser { get; set; }
    public string? Notes { get; set; }

    public List<ActivationRecord> Activations { get; set; } = new();
}

/// <summary>Module N ActivationRecord (FR-ADM-6/7) + the terms issued and
/// validation/deactivation tracking.</summary>
public class ActivationRecord : EntityBase
{
    public long LicenseCodeId { get; set; }
    public LicenseCode? LicenseCode { get; set; }
    /// <summary>80-bit fingerprint hash, hex.</summary>
    public string MachineFingerprint { get; set; } = "";
    public string? SerialKeyIssued { get; set; }
    public string? SoftCode { get; set; }
    public string ActivationMode { get; set; } = "Offline";
    public string Status { get; set; } = ActivationStatus.Active;
    public DateTime ActivationDate { get; set; }
    /// <summary>Added: terms actually baked into the issued key (may differ from the
    /// code's defaults when overridden at generation time, FR-ADM-6).</summary>
    public string IssuedTier { get; set; } = "Standard";
    public DateOnly? IssuedExpiryDate { get; set; }
    public DateTime? DeactivatedDate { get; set; }
    public DateTime? LastValidationDate { get; set; }
    public string? AppVersion { get; set; }
    public long? GeneratedByAdminUserId { get; set; }
    public AdminUser? GeneratedByAdminUser { get; set; }
}

/// <summary>Module N TransferHistory (FR-LIC-10, FR-ADM-8).</summary>
public class TransferHistory : EntityBase
{
    public long LicenseCodeId { get; set; }
    public LicenseCode? LicenseCode { get; set; }
    public long? OldActivationRecordId { get; set; }
    public ActivationRecord? OldActivationRecord { get; set; }
    public long? NewActivationRecordId { get; set; }
    public ActivationRecord? NewActivationRecord { get; set; }
    public DateTime TransferDate { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Module N SupportTicket.</summary>
public class SupportTicket : EntityBase
{
    public long CustomerRecordId { get; set; }
    public CustomerRecord? CustomerRecord { get; set; }
    public long? LicenseCodeId { get; set; }
    public LicenseCode? LicenseCode { get; set; }
    public string Subject { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "Open";
    public long? AssignedAdminUserId { get; set; }
    public AdminUser? AssignedAdminUser { get; set; }
}

/// <summary>Module N AdminAuditLog (FR-ADM-3) + hash chain so edits are detectable
/// (same idea as the desktop AuditLog).</summary>
public class AdminAuditLog : EntityBase
{
    public long? AdminUserId { get; set; }
    public AdminUser? AdminUser { get; set; }
    public string Action { get; set; } = "";
    public string? IPAddress { get; set; }
    public DateTime ActionDate { get; set; }
    public string? DetailsJson { get; set; }
    public string? PreviousHash { get; set; }
    public string RecordHash { get; set; } = "";
}

/// <summary>Module N FraudAlert (FR-ADM-14).</summary>
public class FraudAlert : EntityBase
{
    public long? ActivationRecordId { get; set; }
    public ActivationRecord? ActivationRecord { get; set; }
    public string AlertType { get; set; } = "";
    public string Severity { get; set; } = "Low";
    public DateTime DetectedDate { get; set; }
    public bool IsResolved { get; set; }
}

/// <summary>Module N EmailTemplate (FR-ADM-11).</summary>
public class EmailTemplate : EntityBase
{
    public string Name { get; set; } = "";
    public string Subject { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public string TemplateType { get; set; } = "";
}

/// <summary>Added table: blocked machines/codes (FR-ADM-8, FR-ADM-15).</summary>
public class BlacklistEntry : EntityBase
{
    /// <summary>"Fingerprint" or "ActivationCode".</summary>
    public string EntryType { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Reason { get; set; }
}
