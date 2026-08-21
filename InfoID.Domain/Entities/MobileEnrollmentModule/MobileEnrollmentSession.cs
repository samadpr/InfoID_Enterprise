using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.CardholderModule;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.MobileEnrollmentModule;

/// <summary>
/// One self-enrollment session started by scanning a QR code; may create/update a Cardholder once synced.
/// </summary>
public class MobileEnrollmentSession : BaseEntity
{
    public long? CardholderId { get; set; }  // Set once the record is created/matched
    public Cardholder? Cardholder { get; set; }
    public long? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public string SessionToken { get; set; } = string.Empty;
    public string? QrCodeValue { get; set; }
    public MobileEnrollmentStatus Status { get; set; }  // Pending / Submitted / Synced / Expired
    public DateTime? SubmittedDate { get; set; }
    public DateTime? SyncedDate { get; set; }
    public bool IsKioskMode { get; set; }  // Default 0 (FR-MOB-3)
}
