namespace InfoID.LicenseManager.Domain.Entities;

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
