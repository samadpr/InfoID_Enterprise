namespace InfoID.LicenseManager.Domain.Entities;

/// <summary>Module N SupportTicket.</summary>
public class SupportTicket : EntityBase
{
    public long CustomerRecordId { get; set; }
    public CustomerRecord? CustomerRecord { get; set; }
    public long? LicenseCodeId { get; set; }
    public LicenseCode? LicenseCode { get; set; }
    public string Subject { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "Open";
    public long? AssignedAdminUserId { get; set; }
    public AdminUser? AssignedAdminUser { get; set; }
}
