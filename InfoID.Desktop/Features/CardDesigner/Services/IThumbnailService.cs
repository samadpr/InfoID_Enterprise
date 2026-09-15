using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Generates and locates PNG thumbnails for saved designs (Part 61). Stored as plain
/// files under the app's data root, keyed by Template.Id, rather than a new database
/// column/BLOB -- avoids a schema change for something that's a derived, regenerable
/// cache (delete the file and it's just regenerated on next save), consistent with
/// "schema changes must be justified" and "don't create unnecessary duplicate tables/
/// columns".
/// </summary>
public interface IThumbnailService
{
    /// <summary>Renders the front side of the document and saves it as a PNG for the
    /// given template id, overwriting any existing thumbnail. Returns the file path.</summary>
    Task<string> GenerateThumbnailAsync(CardDesignDocument document, long templateId, CancellationToken ct = default);

    /// <summary>Returns the thumbnail file path if one has been generated for this
    /// template, or null if none exists yet (e.g. a design that predates this feature,
    /// or was never successfully saved after it was added).</summary>
    string? GetThumbnailPath(long templateId);

    /// <summary>Same rendering as GenerateThumbnailAsync but returned in-memory rather
    /// than saved to a Template.Id-keyed file -- for file-backed designs (Part: .infoid
    /// import/export), which have no Template.Id to key a cached file under. Returns
    /// null if rendering fails for any reason (never throws).</summary>
    Task<IImage?> RenderInMemoryAsync(CardDesignDocument document, CancellationToken ct = default);
}
