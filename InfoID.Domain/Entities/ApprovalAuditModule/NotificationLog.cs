using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.ApprovalAuditModule;

/// <summary>
/// Desktop toast notifications: card expiry, approval requests, print-job completion/failure, license renewal (FR-MOD-5, FR-LIC-15).
/// </summary>
public class NotificationLog : BaseEntity
{
    public long? AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public NotificationType NotificationType { get; set; }  // CardExpiry / ApprovalRequest / PrintJobComplete / LicenseRenewal
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }  // Default 0
    public DateTime SentDate { get; set; }
}
