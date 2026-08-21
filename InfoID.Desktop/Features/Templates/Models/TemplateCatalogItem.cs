using Avalonia.Media;
using InfoID.Desktop.Features.BlankCard.Models;

namespace InfoID.Desktop.Features.Templates.Models;

/// <summary>
/// A browsable template shown in the template gallery (FR-DES-10). Today backed by
/// sample data via <see cref="Services.ITemplateCatalogService"/>; later the same shape
/// will be projected from InfoID.Application's Template DTOs (see
/// InfoID.Application/Dtos/TemplateModule).
/// </summary>
public sealed class TemplateCatalogItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Format { get; init; }
    public CardOrientation Orientation { get; init; } = CardOrientation.Landscape;
    public string? AccentColorHex { get; init; }
    public bool IsUserTemplate { get; init; }

    /// <summary>Pre-parsed brush for the preview tile, so the View never has to parse
    /// a hex string itself -- avoids any binding/type-conversion ambiguity.</summary>
    public IBrush AccentBrush => new SolidColorBrush(
        Color.TryParse(AccentColorHex, out var color) ? color : Color.Parse("#DA3025"));
}
