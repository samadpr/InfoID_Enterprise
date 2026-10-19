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
    /// <summary>Added: whether this code may be activated WITHOUT the online server
    /// (phone/QR page or manual request code). Off by default: most licenses activate
    /// online only, the more secure path. Staff can always issue a key manually.</summary>
    public bool AllowOfflineActivation { get; set; }
    /// <summary>Added: groups codes created together (FR-ADM-4 batch).</summary>
    public Guid? BatchId { get; set; }
    public DateTime? RevokedDate { get; set; }
    public long? CustomerRecordId { get; set; }
    public CustomerRecord? CustomerRecord { get; set; }
    public bool IsUsed { get; set; }
    public long? CreatedByAdminUserId { get; set; }
    public AdminUser? CreatedByAdminUser { get; set; }
    public string? Notes { get; set; }
    /// <summary>Added: which product this code licenses (multi-product platform).</summary>
    public string ProductCode { get; set; } = ProductCodes.InfoID;
    /// <summary>Added: the customer account this code belongs to ("My licenses" in the
    /// app). Set when a license request is approved; optional for codes sold directly.</summary>
    public long? CustomerAccountId { get; set; }
    public CustomerAccount? CustomerAccount { get; set; }

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
    /// <summary>Added: card count the machine reported at its last online check-in.</summary>
    public int? CardsIssuedReported { get; set; }
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

// ======================================================================================
// Customer platform (accounts, plans, license requests) -- shared by all products.
// ======================================================================================

public static class ProductCodes
{
    public const string InfoID = "INFOID";
}

/// <summary>A product sold through the platform (InfoID now, other solutions later).</summary>
public class Product : EntityBase
{
    /// <summary>Stable code used everywhere, e.g. "INFOID". Unique.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

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
}

public static class EmailCodePurpose
{
    public const string VerifyEmail = "VerifyEmail";
    public const string ResetPassword = "ResetPassword";
}

/// <summary>A one-time 6-digit code sent by email (verification / password reset).
/// Only a hash is stored; expires and allows limited attempts.</summary>
public class EmailCode : EntityBase
{
    public long CustomerAccountId { get; set; }
    public CustomerAccount? CustomerAccount { get; set; }
    public string Purpose { get; set; } = EmailCodePurpose.VerifyEmail;
    public string CodeHash { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime? UsedUtc { get; set; }
}

/// <summary>A signed-in app session. The app holds a random token; only its SHA-256
/// hash is stored, so a database leak doesn't expose usable tokens.</summary>
public class CustomerSession : EntityBase
{
    public long CustomerAccountId { get; set; }
    public CustomerAccount? CustomerAccount { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public string? CreatedFromIp { get; set; }
    public string? AppVersion { get; set; }
}

/// <summary>A plan customers can request (edition + duration + machines), managed in
/// License Manager and shown on the app's Editions page -- change plans without
/// releasing a new app.</summary>
public class Plan : EntityBase
{
    public string ProductCode { get; set; } = ProductCodes.InfoID;
    public string Name { get; set; } = "";
    /// <summary>Standard / Professional / Enterprise (InfoID.Licensing.Core.LicenseTier).</summary>
    public string Tier { get; set; } = "Standard";
    /// <summary>One-line audience description, e.g. "Small companies, occasional printing".</summary>
    public string? Tagline { get; set; }
    /// <summary>Feature list, one per line.</summary>
    public string? FeaturesText { get; set; }
    /// <summary>Subscription length in months; null = perpetual.</summary>
    public int? DurationMonths { get; set; } = 12;
    public int DefaultMachines { get; set; } = 1;
    public int? MaxCardsPerPeriod { get; set; }
    public string CardLimitPeriod { get; set; } = "Monthly";
    /// <summary>Display only, e.g. "Contact sales" -- payment happens outside the system.</summary>
    public string? PriceText { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class LicenseRequestStatus
{
    public const string New = "New";
    public const string InProgress = "InProgress";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

/// <summary>A customer's request for a plan (from the app). Staff confirm payment
/// outside the system, then Approve -- which creates the activation code.</summary>
public class LicenseRequest : EntityBase
{
    public string ProductCode { get; set; } = ProductCodes.InfoID;
    public long PlanId { get; set; }
    public Plan? Plan { get; set; }
    public long? CustomerAccountId { get; set; }
    public CustomerAccount? CustomerAccount { get; set; }
    public int Machines { get; set; } = 1;

    public string FullName { get; set; } = "";
    public string? CompanyName { get; set; }
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public string? StateProvince { get; set; }
    public string? Message { get; set; }

    public string Status { get; set; } = LicenseRequestStatus.New;
    public string? StatusReason { get; set; }
    public long? HandledByAdminUserId { get; set; }
    public AdminUser? HandledByAdminUser { get; set; }
    public DateTime? HandledDate { get; set; }
    /// <summary>The activation code created on approval.</summary>
    public long? LicenseCodeId { get; set; }
    public LicenseCode? LicenseCode { get; set; }

    /// <summary>The requesting PC (for support); not used for activation.</summary>
    public string? MachineFingerprint { get; set; }
    public string? AppVersion { get; set; }
}

// ======================================================================================
// Trial printing (customer accounts without an activation code).
// ======================================================================================

public static class TrialReservationStatus
{
    public const string Reserved = "Reserved";     // credits held while the app prints
    public const string Committed = "Committed";   // printed; CardsPrinted counted as used
    public const string Released = "Released";     // print failed or was cancelled
    public const string Expired = "Expired";       // the app never reported back (crash)
}

/// <summary>A customer account's trial printing allowance for one product. The server
/// is the only place the count lives, so reinstalling the app or deleting its data
/// can't reset it.</summary>
public class TrialUsage : EntityBase
{
    public long CustomerAccountId { get; set; }
    public CustomerAccount? CustomerAccount { get; set; }
    public string ProductCode { get; set; } = ProductCodes.InfoID;
    /// <summary>Cards this account may print in trial mode (copied from the server
    /// setting when the row is created; staff can raise it for one account).</summary>
    public int CardLimit { get; set; }
    /// <summary>Cards actually printed.</summary>
    public int CardsUsed { get; set; }
    /// <summary>Cards held by print jobs in progress. Used + Reserved never exceeds the limit.</summary>
    public int CardsReserved { get; set; }
    public DateTime? FirstPrintDate { get; set; }
    public DateTime? LastPrintDate { get; set; }
}

/// <summary>One trial print job: credits are reserved before printing, then committed
/// (printed) or released (failed/cancelled). Unreported reservations expire.</summary>
public class TrialReservation : EntityBase
{
    /// <summary>The id the app holds and sends back to commit or release.</summary>
    public Guid ReservationKey { get; set; }
    public long TrialUsageId { get; set; }
    public TrialUsage? TrialUsage { get; set; }
    public long CustomerAccountId { get; set; }
    public int Cards { get; set; }
    public int? CardsPrinted { get; set; }
    public string Status { get; set; } = TrialReservationStatus.Reserved;
    public DateTime ExpiresUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string? DesignName { get; set; }
    public string? MachineFingerprint { get; set; }
    public string? AppVersion { get; set; }
}
