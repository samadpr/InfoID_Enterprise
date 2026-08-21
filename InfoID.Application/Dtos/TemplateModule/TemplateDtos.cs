using InfoID.Domain.Common.Enums;

namespace InfoID.Application.Dtos.TemplateModule;

/// <summary>
/// Full record for the designer to load into the canvas. FrontDesignJson/
/// BackDesignJson are opaque to the Application layer -- they're whatever the
/// Desktop project's CardDesignSerializer produced (see
/// InfoID.Desktop/Models/CardDesigner). Application only stores/returns the
/// blob; it never parses it, so the canvas's element schema can evolve
/// without an Application-layer migration.
/// </summary>
public class TemplateDto
{
    public long Id { get; set; }
    public long OrganizationId { get; set; }
    public long? BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CardSize { get; set; } = "CR-80";
    public TemplateCategory? Category { get; set; }
    public string? FrontDesignJson { get; set; }
    public string? BackDesignJson { get; set; }
    public bool IsGalleryTemplate { get; set; }
    public int CurrentVersionNumber { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
}

/// <summary>Lightweight row for template pickers/lists (the tab strip, "My
/// Templates" gallery) -- skips the two design JSON blobs, which can be
/// sizeable, since list views never render canvas content.</summary>
public class TemplateSummaryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CardSize { get; set; } = "CR-80";
    public TemplateCategory? Category { get; set; }
    public bool IsGalleryTemplate { get; set; }
    public int CurrentVersionNumber { get; set; }
    public DateTime ModifiedDate { get; set; }
}

public class CreateTemplateRequest
{
    public long OrganizationId { get; set; }
    public long? BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CardSize { get; set; } = "CR-80";
    public TemplateCategory? Category { get; set; }

    /// <summary>Optional starting design (e.g. copied from a gallery preset or an
    /// existing template via Duplicate) so a new template doesn't always have to
    /// start blank.</summary>
    public string? FrontDesignJson { get; set; }
    public string? BackDesignJson { get; set; }
}

public class RenameTemplateRequest
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Persists the canvas. Bumps CurrentVersionNumber and writes a
/// TemplateVersion snapshot row every save (FR-DES-9/FR-DES-13 versioning),
/// same spirit as "autosave" but user-triggered from the designer's Save
/// button rather than a timer.</summary>
public class UpdateTemplateDesignRequest
{
    public long Id { get; set; }
    public string CardSize { get; set; } = "CR-80";
    public string? FrontDesignJson { get; set; }
    public string? BackDesignJson { get; set; }
    public string? ChangeNote { get; set; }
}
