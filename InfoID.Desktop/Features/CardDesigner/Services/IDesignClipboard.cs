using System.Collections.Generic;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Shared in-memory clipboard for designer elements. Deliberately a singleton service
/// (not per-tab state) so copy/paste works across tabs and across front/back sides, per
/// Part 29 ("Paste: same document, different document, front to back, back to front").
/// Stores a JSON snapshot (via the same polymorphic DesignerElement serialization the
/// future .infoid export uses) so pasted elements are always independent clones, never
/// shared references with their source.
/// </summary>
public interface IDesignClipboard
{
    bool HasContent { get; }
    void SetContent(IReadOnlyList<DesignerElement> elements);
    IReadOnlyList<DesignerElement> GetClones();
}