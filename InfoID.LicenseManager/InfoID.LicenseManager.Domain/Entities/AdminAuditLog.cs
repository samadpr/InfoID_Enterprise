namespace InfoID.LicenseManager.Domain.Entities;

/// <summary>Module N AdminAuditLog (FR-ADM-3) + hash chain so edits are detectable
/// (same idea as the desktop AuditLog).</summary>
public class AdminAuditLog : EntityBase
{
    public long? AdminUserId { get; set; }
    public AdminUser? AdminUser { get; set; }
    public string Action { get; set; } = "";
    public string? IPAddress { get; set; }
    public DateTime ActionDate { get; set; }
    public string? DetailsJson { get; set; }
    public string? PreviousHash { get; set; }
    public string RecordHash { get; set; } = "";
}
