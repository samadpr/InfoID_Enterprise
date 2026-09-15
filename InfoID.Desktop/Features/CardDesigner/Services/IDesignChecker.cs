using System.Collections.Generic;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Validates a card design for the print/production issues listed in Part 79 (elements
/// outside the printable area or crossing the safe zone, missing images, empty required
/// values, overlapping elements, and so on) so problems surface before print time
/// instead of on the printed card.
/// </summary>
public interface IDesignChecker
{
    /// <summary>Checks both sides of the document and returns every finding, worst
    /// severity first.</summary>
    IReadOnlyList<DesignIssue> Check(CardDesignDocument document);
}
