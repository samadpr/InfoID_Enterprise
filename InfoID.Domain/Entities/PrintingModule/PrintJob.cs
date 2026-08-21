using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.ApprovalAuditModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Entities.TemplateModule;

namespace InfoID.Domain.Entities.PrintingModule;

/// <summary>
/// One print run — a single card, a batch, or a reprint (FR-PRN-1 to FR-PRN-4).
/// </summary>
public class PrintJob : BaseEntity
{
    public long? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public long PrinterProfileId { get; set; }
    public PrinterProfile? PrinterProfile { get; set; }
    public long? TemplateId { get; set; }
    public Template? Template { get; set; }
    public long? ApprovalRecordId { get; set; }  // Digital sign-off required before batch printing (FR-ENT-1)
    public ApprovalRecord? ApprovalRecord { get; set; }
    public long RequestedByUserId { get; set; }
    public AppUser? RequestedByUser { get; set; }
    public PrintJobType JobType { get; set; }  // Single / Batch / Reprint
    public PrintJobStatus Status { get; set; }  // Queued / Running / Paused / Completed / Cancelled / Failed
    public int TotalCards { get; set; }  // Default 0
    public int SuccessCount { get; set; }  // Default 0
    public int FailureCount { get; set; }  // Default 0
    public DateTime? StartedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
}
