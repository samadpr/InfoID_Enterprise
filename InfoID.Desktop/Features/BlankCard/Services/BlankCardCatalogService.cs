using System.Collections.Generic;
using System.Threading.Tasks;
using InfoID.Desktop.Features.BlankCard.Models;

namespace InfoID.Desktop.Features.BlankCard.Services;

/// <summary>
/// Sample/in-memory catalog. Mirrors the format families described in the SRS
/// (Standard/Professional/Enterprise RFID tiers -- FR-RFD-1) so the page already feels
/// representative of the real product. Replace with a repository-backed implementation
/// once card-stock catalog management exists server-side.
/// </summary>
public sealed class BlankCardCatalogService : IBlankCardCatalogService
{
    private static readonly IReadOnlyList<string> Tags = new[]
    {
        "Common", "Contactless", "Mifare", "Ultralight", "Contact", "Magnetic",
        "2 Track", "3 Track", "Combo", "Specific", "Inkjet", "Postcard", "Business",
        "Envelope", "User models",
    };

    private static readonly IReadOnlyList<CardFormatOption> Formats = new List<CardFormatOption>
    {
        New("common-landscape", "Blank CR-80", CardOrientation.Landscape, "Common"),
        New("common-portrait", "Blank CR-80 (Portrait)", CardOrientation.Portrait, "Common"),
        New("generic-contact", "Generic Contact Chip", CardOrientation.Landscape, "Common", "Contact"),
        New("contactless", "Contactless", CardOrientation.Landscape, "Common", "Contactless"),
        New("mifare-classic", "Mifare Classic", CardOrientation.Landscape, "Common", "Contactless", "Mifare"),
        New("mifare-ultralight", "Mifare Ultralight", CardOrientation.Landscape, "Contactless", "Ultralight"),
        New("hico-magnetic", "HiCo Magnetic Stripe", CardOrientation.Landscape, "Common", "Magnetic"),
        New("loco-2track", "LoCo Magnetic (2 Track)", CardOrientation.Landscape, "Magnetic", "2 Track"),
        New("loco-3track", "LoCo Magnetic (3 Track)", CardOrientation.Landscape, "Magnetic", "3 Track"),
        New("combo-card", "Combo (Chip + Magnetic)", CardOrientation.Landscape, "Combo", "Contact", "Magnetic"),
        New("desfire", "DESFire EV2", CardOrientation.Landscape, "Contactless", "Specific"),
        New("business-card", "Business Card", CardOrientation.Landscape, "Business"),
        New("postcard", "Postcard", CardOrientation.Landscape, "Postcard"),
        New("inkjet", "Inkjet Printable", CardOrientation.Landscape, "Inkjet"),
        New("envelope", "Envelope", CardOrientation.Landscape, "Envelope"),
    };

    public Task<IReadOnlyList<string>> GetFilterTagsAsync() => Task.FromResult(Tags);

    public Task<IReadOnlyList<CardFormatOption>> GetFormatsAsync() =>
        Task.FromResult(Formats);

    private static CardFormatOption New(string id, string name, CardOrientation orientation, params string[] tags) =>
        new()
        {
            Id = id,
            Name = name,
            CardSizeName = "CR-80",
            Orientation = orientation,
            WidthMm = orientation == CardOrientation.Landscape ? 85.6 : 54.0,
            HeightMm = orientation == CardOrientation.Landscape ? 54.0 : 85.6,
            Tags = tags,
            PreviewLabel = tags.Length > 0 ? tags[0] : name,
        };
}
