using InfoID.Application.Dtos.TemplateModule;
using InfoID.Shared.Common;

namespace InfoID.Application.Interfaces;

/// <summary>
/// Module C (Card Designer). Templates are the saved front/back designs the
/// Card Designer canvas loads into and saves out of -- see Domain Model
/// Module C: Template / TemplateElement / TemplateVersion.
/// </summary>
public interface ITemplateService
{
    Task<TemplateDto?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<TemplateSummaryDto>> GetByOrganizationAsync(long organizationId, CancellationToken ct = default);

    Task<Result<long>> CreateAsync(CreateTemplateRequest request, CancellationToken ct = default);

    Task<Result> RenameAsync(RenameTemplateRequest request, CancellationToken ct = default);

    /// <summary>Saves canvas state (front/back JSON) and writes a TemplateVersion
    /// snapshot -- the designer's "Save" action.</summary>
    Task<Result> UpdateDesignAsync(UpdateTemplateDesignRequest request, CancellationToken ct = default);

    /// <summary>Clones a template (including both sides' current design) under a
    /// new name -- "Save as" / "Duplicate" in the designer, and how a gallery
    /// preset becomes an editable working template (FR-DES-10).</summary>
    Task<Result<long>> DuplicateAsync(long id, string newName, CancellationToken ct = default);

    Task<Result> SoftDeleteAsync(long id, CancellationToken ct = default);
}
