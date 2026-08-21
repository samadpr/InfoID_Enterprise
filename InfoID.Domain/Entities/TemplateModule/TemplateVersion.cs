using InfoID.Domain.Common;

namespace InfoID.Domain.Entities.TemplateModule;

/// <summary>
/// A version snapshot of a template, enabling versioning/rollback (FR-DES-9, FR-DES-13).
/// </summary>
public class TemplateVersion : BaseEntity
{
    public long TemplateId { get; set; }
    public Template? Template { get; set; }
    public int VersionNumber { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;  // Full front+back design at time of save
    public string? ChangeNote { get; set; }
}
