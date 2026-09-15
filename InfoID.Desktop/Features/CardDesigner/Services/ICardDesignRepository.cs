using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Desktop.Features.CardDesigner.Models;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Persists CardDesignDocument through the existing Template/TemplateVersion entities
/// (Module C) rather than a new table set -- see the comments on Template's new fields
/// for how the document maps onto its columns.
/// </summary>
public interface ICardDesignRepository
{
    /// <summary>Insert (first save) or update (subsequent saves), based on
    /// document.PersistedTemplateId. Returns the Template.Id either way.</summary>
    Task<long> SaveAsync(CardDesignDocument document, CancellationToken ct = default);

    Task<CardDesignDocument?> LoadAsync(long templateId, CancellationToken ct = default);

    Task<IReadOnlyList<RecentDesignSummary>> GetRecentAsync(int maxCount = 20, CancellationToken ct = default);

    Task MarkOpenedAsync(long templateId, CancellationToken ct = default);

    Task SetPinnedAsync(long templateId, bool pinned, CancellationToken ct = default);

    /// <summary>Soft-deletes (Cancelled = true) -- InfoID never hard-deletes business rows.</summary>
    Task DeleteAsync(long templateId, CancellationToken ct = default);

    Task SaveVersionSnapshotAsync(long templateId, CardDesignDocument document, string? changeNote, CancellationToken ct = default);

    Task<IReadOnlyList<TemplateVersionSummary>> GetVersionsAsync(long templateId, CancellationToken ct = default);

    /// <summary>Full document content for one version snapshot, for Version History's
    /// "Restore" action (Part 60). Null if the version row doesn't exist or its JSON is
    /// somehow unreadable -- never throws for a corrupt/missing snapshot, since that
    /// would take down the whole Version History dialog over one bad row.</summary>
    Task<CardDesignDocument?> GetVersionSnapshotAsync(long versionId, CancellationToken ct = default);
}