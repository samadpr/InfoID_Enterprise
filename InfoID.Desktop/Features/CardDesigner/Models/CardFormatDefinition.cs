using InfoID.Desktop.Features.BlankCard.Models;

namespace InfoID.Desktop.Features.CardDesigner.Models;

/// <summary>
/// Centralized physical card format definition. This replaces scattering width/height
/// numbers through XAML/ViewModels (see BlankCard.Models.CardFormatOption, which stays
/// as the "blank card stock" catalog for Home > Blank Card, and now maps onto this for
/// dimensions). Every card design in the Designer is anchored to one of these.
///
/// Dimensions for CR79/90/100 are commonly-cited industry figures for those formats
/// (there is no single ISO spec for them the way CR80/ID-1 has one) -- CR80 itself uses
/// the exact SRS-specified 85.60 x 53.98 mm (ISO/IEC 7810 ID-1).
/// </summary>
public sealed class CardFormatDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required double WidthMm { get; init; }
    public required double HeightMm { get; init; }
    public required CardFormatCategory Category { get; init; }
    public required CardOrientation DefaultOrientation { get; init; }

    /// <summary>Whether the user can flip this format to the other orientation
    /// (width/height swap) in the designer. True for all formats today.</summary>
    public bool OrientationSupported { get; init; } = true;

    public double CornerRadiusMm { get; init; } = 3.18; // ISO/IEC 7810 ID-1 typical corner radius
    public double BleedMm { get; init; } = 3.0;
    public double SafeZoneMm { get; init; } = 3.0;

    public double PrintableWidthMm => WidthMm - (2 * SafeZoneMm);
    public double PrintableHeightMm => HeightMm - (2 * SafeZoneMm);

    public bool IsStandard => Category == CardFormatCategory.Standard;
    public bool IsCustom => Category == CardFormatCategory.Custom;

    /// <summary>Short code shown in pickers -- "CR80", "CR79", or the user's model name for customs.</summary>
    public required string FormatCode { get; init; }

    /// <summary>Returns a copy with width/height swapped -- used for the Landscape/Portrait toggle.</summary>
    public CardFormatDefinition WithOrientation(CardOrientation orientation)
    {
        var isLandscape = orientation == CardOrientation.Landscape;
        var longSide = System.Math.Max(WidthMm, HeightMm);
        var shortSide = System.Math.Min(WidthMm, HeightMm);

        return new CardFormatDefinition
        {
            Id = Id,
            Name = Name,
            Description = Description,
            WidthMm = isLandscape ? longSide : shortSide,
            HeightMm = isLandscape ? shortSide : longSide,
            Category = Category,
            DefaultOrientation = orientation,
            OrientationSupported = OrientationSupported,
            CornerRadiusMm = CornerRadiusMm,
            BleedMm = BleedMm,
            SafeZoneMm = SafeZoneMm,
            FormatCode = FormatCode,
        };
    }

    public enum CardFormatCategory
    {
        Standard,
        Custom,
    }
}