namespace InfoID.Desktop.Features.BlankCard.Models;

/// <summary>Unit for card size / radius fields. Only Millimeters is implemented today; the
/// dialog and this model are shaped so Centimeters/Inches/Pixels can be added later.</summary>
public enum SizeUnit
{
    Millimeters,
    Centimeters,
    Inches,
    Pixels,
}

/// <summary>Result produced by the "Create my own card" dialog once the user confirms.</summary>
public sealed class CustomCardSizeResult
{
    public required double Width { get; init; }
    public required double Height { get; init; }
    public SizeUnit Unit { get; init; } = SizeUnit.Millimeters;
    public required CardOrientation Orientation { get; init; }
    public double CornerRadius { get; init; }
    public bool SaveToMyModels { get; init; }
    public string? ModelName { get; init; }
}
