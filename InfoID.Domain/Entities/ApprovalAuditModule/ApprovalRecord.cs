using InfoID.Domain.Common;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.ApprovalAuditModule;

/// <summary>
/// A digital sign-off with timestamp, e.g. before batch printing (FR-ENT-1, FR-MOD-6).
/// </summary>
public class ApprovalRecord : BaseEntity
{
    public long ApprovedByUserId { get; set; }
    public AppUser? ApprovedByUser { get; set; }
    public string Context { get; set; } = string.Empty;  // BatchPrint / Other compliance workflow
    public long? ContextReferenceId { get; set; }  // Generic reference id (e.g. a PrintJob.Id) — not a hard FK since context varies
    public DateTime SignedDate { get; set; }
    public string? SignatureDataEncrypted { get; set; }
}
