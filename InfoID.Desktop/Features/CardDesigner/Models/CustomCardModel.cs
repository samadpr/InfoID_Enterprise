using System;

namespace InfoID.Desktop.Features.CardDesigner.Models;

/// <summary>
/// A user-saved custom card model from the "Create my own card" dialog
/// (Save card to my models). Persisted via ICustomCardModelStore, listed under
/// Format > My Models in the designer, per FR-DES-9 / Part 39 of the designer spec.
/// </summary>
public sealed class CustomCardModel
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required double WidthMm { get; set; }
    public required double HeightMm { get; set; }
    public required double CornerRadiusMm { get; set; }
    public required Features.BlankCard.Models.CardOrientation Orientation { get; set; }
    public DateTime CreatedDate { get; init; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }
}