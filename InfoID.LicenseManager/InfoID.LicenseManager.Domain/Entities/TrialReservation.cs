namespace InfoID.LicenseManager.Domain.Entities;

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
