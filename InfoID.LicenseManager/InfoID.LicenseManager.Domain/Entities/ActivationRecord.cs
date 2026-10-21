namespace InfoID.LicenseManager.Domain.Entities;

public static class ActivationStatus
{
    public const string Active = "Active";
    public const string Deactivated = "Deactivated";
    public const string Blacklisted = "Blacklisted";
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

    /// <summary>An online machine checked in: remember when, the terms now issued to it, and what it reported.</summary>
    public void RecordCheckIn(DateTime utcNow, DateOnly? issuedExpiry, int? cardsIssued, string? appVersion)
    {
        LastValidationDate = utcNow;
        IssuedExpiryDate = issuedExpiry;
        CardsIssuedReported = cardsIssued;
        if (!string.IsNullOrWhiteSpace(appVersion)) AppVersion = appVersion;
    }
}
