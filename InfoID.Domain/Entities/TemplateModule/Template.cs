using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.TemplateModule;

/// <summary>
/// A saved card design (front + back). Can be a normal working template or a read-only gallery template (FR-DES-10).
/// </summary>
public class Template : BaseEntity
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public long? BranchId { get; set; }  // Null = shared across all branches
    public Branch? Branch { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CardSize { get; set; } = string.Empty;  // CR-80 or other standard size
    public TemplateCategory? Category { get; set; }  // School / Corporate / Event / Hospital (for gallery)
    public string? FrontDesignJson { get; set; }  // Serialized canvas state — front
    public string? BackDesignJson { get; set; }  // Serialized canvas state — back
    public bool IsGalleryTemplate { get; set; }  // Default 0
    public int CurrentVersionNumber { get; set; }  // Default 1, increments with each save

    /// <summary>Card Designer document metadata that doesn't fit Template's existing
    /// columns (custom width/height/orientation/bleed/grid/schema version) -- a small
    /// JSON envelope rather than a schema explosion. FrontDesignJson/BackDesignJson
    /// (already on this entity) hold the serialized CardDesignSide for each side.</summary>
    public string? DocumentMetadataJson { get; set; }

    /// <summary>Drives the Home screen's Recent Cards list (Part 37/84) and the
    /// Designer's own recent-opens -- null means "never opened/created via the designer".</summary>
    public DateTime? LastOpenedDate { get; set; }

    /// <summary>User-pinned in Recent Cards (Part 37 "Pin").</summary>
    public bool IsPinnedRecent { get; set; }
}
