using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.VisitorModule;

/// <summary>
/// Pre-registration submitted via web link, pending host approval (FR-VIS-2).
/// </summary>
public class VisitorPreRegistration : BaseEntity
{
    public long? VisitorBadgeId { get; set; }  // Set once approved and badge is issued
    public VisitorBadge? VisitorBadge { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public long? HostAppUserId { get; set; }
    public AppUser? HostAppUser { get; set; }
    public DateTime? ExpectedVisitDate { get; set; }
    public VisitorApprovalStatus ApprovalStatus { get; set; }  // Pending / Approved / Rejected
}
