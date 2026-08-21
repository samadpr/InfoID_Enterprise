using InfoID.Domain.Common;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.ApprovalAuditModule;

/// <summary>
/// Append-only, hash-chained record of every user action across all modules (FR-REP-4, FR-REP-7, 5.2). Each row's hash includes the previous row's hash so tampering is detectable.
/// </summary>
public class AuditLog : BaseEntity
{
    public long? AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public string Module { get; set; } = string.Empty;  // e.g. Designer, Printing, Licensing
    public string Action { get; set; } = string.Empty;  // e.g. Create, Update, Delete, Print, Encode, Login
    public string? EntityName { get; set; }  // Name of the table affected
    public long? EntityId { get; set; }  // Id of the affected row (generic reference, not a hard FK)
    public string? BeforeValueJson { get; set; }
    public string? AfterValueJson { get; set; }
    public DateTime ActionDate { get; set; }
    public string? PreviousHash { get; set; }  // Hash of the prior AuditLog row
    public string RecordHash { get; set; } = string.Empty;  // Hash of this row's content + PreviousHash
}
