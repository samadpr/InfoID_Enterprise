using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>One design-checker finding (Part 79). Carries the actual element reference
/// (not just an id to look up) so "click an issue to select the affected element"
/// (Part 79's own requirement) is a direct reference, not a second lookup pass.
/// Document-level issues (nothing to select) leave Element null.</summary>
public sealed class DesignIssue
{
    public required DesignIssueSeverity Severity { get; init; }
    public required string Message { get; init; }
    public DesignerElement? Element { get; init; }
    public CardSide Side { get; init; }
}
