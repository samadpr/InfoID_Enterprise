using System.Collections.Generic;

namespace InfoID.Desktop.Features.BlankCard.Models; //(e.g. "Common CR-80", "Mifare Classic CR-80").
/// Tags are free-form strings rather than a fixed enum so new format families
/// (new chip types, new stock types, ...) can be added purely through catalog data --
/// see <see cref="Services.IBlankCardCatalogService"/> -- without changing the UI.
/// </summary>
public sealed class CardFormatOption
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string CardSizeName { get; init; } = "CR-80";
    public required CardOrientation Orientation { get; init; }
    public double WidthMm { get; init; } = 85.6;
    public double HeightMm { get; init; } = 54.0;
    public required IReadOnlyList<string> Tags { get; init; }
    public string? PreviewLabel { get; init; }
}
