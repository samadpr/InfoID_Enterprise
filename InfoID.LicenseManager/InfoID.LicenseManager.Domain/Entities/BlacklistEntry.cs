namespace InfoID.LicenseManager.Domain.Entities;

/// <summary>Added table: blocked machines/codes (FR-ADM-8, FR-ADM-15).</summary>
public class BlacklistEntry : EntityBase
{
    /// <summary>"Fingerprint" or "ActivationCode".</summary>
    public string EntryType { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Reason { get; set; }
}
