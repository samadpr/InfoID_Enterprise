namespace InfoID.LicenseManager.Domain.Entities;

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
