using InfoID.Application.Dtos.VisitorModule;

namespace InfoID.Application.Interfaces;

/// <summary>
/// Visitor Management (SRS 4.12, badge workflow): visitor registrations imported from a
/// file or a Google Form, or entered by hand, kept until deleted, and printed as badges
/// through the Card Designer. Print status comes from Print history (cards issued to the
/// visitor ID), so printing itself is not duplicated here.
/// </summary>
public interface IVisitorService
{
    Task<IReadOnlyList<VisitorDto>> GetVisitorsAsync(VisitorFilter filter, CancellationToken ct = default);

    Task<IReadOnlyList<VisitorDto>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>Adds one visitor entered by hand. Throws InvalidOperationException with a
    /// readable message when the input has errors.</summary>
    Task<VisitorDto> AddAsync(VisitorInput input, CancellationToken ct = default);

    /// <summary>Updates a visitor. Returns the previous photo path when the photo was
    /// replaced or removed, so the caller can delete the old file.</summary>
    Task<(VisitorDto Visitor, string? ReplacedPhotoPath)> UpdateAsync(long id, VisitorInput input, CancellationToken ct = default);

    /// <summary>Deletes visitors permanently (personal data). Returns their photo paths so
    /// the caller can delete the files.</summary>
    Task<IReadOnlyList<string>> DeleteAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>Which of these source keys were imported before (for "Already imported").</summary>
    Task<IReadOnlySet<string>> FindImportedSourceKeysAsync(IReadOnlyCollection<string> sourceKeys, CancellationToken ct = default);

    /// <summary>Imports the valid rows, skipping ones imported before (by source key) and
    /// repeated rows within the request. Each new visitor gets the next visitor ID.</summary>
    Task<VisitorImportResult> ImportAsync(VisitorImportRequest request, CancellationToken ct = default);

    Task<VisitorSettingsDto> GetSettingsAsync(CancellationToken ct = default);

    Task SaveSettingsAsync(VisitorSettingsDto settings, CancellationToken ct = default);
}
