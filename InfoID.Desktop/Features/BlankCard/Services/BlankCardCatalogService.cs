using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InfoID.Desktop.Features.BlankCard.Models;
using InfoID.Desktop.Features.CardDesigner.Models;
using InfoID.Desktop.Features.CardDesigner.Services;

namespace InfoID.Desktop.Features.BlankCard.Services;

/// <summary>
/// Sample/in-memory catalog. Mirrors the format families described in the SRS
/// (Standard/Professional/Enterprise RFID tiers -- FR-RFD-1) so the page already feels
/// representative of the real product. Replace with a repository-backed implementation
/// once card-stock catalog management exists server-side.
///
/// Priority 2 update: this catalog originally only ever produced CR-80-sized tiles --
/// every single format option, including "Postcard", "Envelope" and "Inkjet Printable",
/// used the same 85.6 x 54mm CR-80 dimensions regardless of what it was actually
/// depicting. CR79/CR90/CR100 tiles are now included, sourced from
/// ICardFormatCatalogService.GetStandardFormats() rather than re-typing the same
/// millimeter figures a second time in this file (that service already carries a doc
/// comment on where those numbers come from and is the one place they're meant to
/// live). "Postcard" and "Envelope" now use real, sourced physical dimensions for what
/// those things actually are (US 4x6in postcard and ISO DL envelope respectively) --
/// see the constants below for sources. "Inkjet Printable" and "Business Card" are left
/// at CR-80 dimensions deliberately, not by omission: inkjet-printable ID card blanks
/// sold for direct-to-card inkjet printers (as opposed to inkjet *paper* products) are
/// themselves standard CR-80-sized PVC stock, and many regions' business cards (UK/EU
/// especially, 85x55mm) are close enough to CR-80 that re-deriving a separate figure
/// wasn't judged worth the risk of guessing at a "the" business card size when none
/// universally exists.
/// </summary>
public sealed class BlankCardCatalogService : IBlankCardCatalogService
{
    private readonly ICardFormatCatalogService _formatCatalog;

    // Sourced dimensions for the two format families that were previously and
    // incorrectly reusing CR-80's 85.6 x 54mm:
    //  - Postcard: US standard postcard, 4 x 6 in = 101.6 x 152.4 mm (also the
    //    USPS/Canada Post First-Class postcard-rate size).
    //  - Envelope: ISO 269 "DL" envelope, 110 x 220 mm -- the standard business/A4
    //    letter envelope internationally.
    private const double PostcardShortMm = 101.6;
    private const double PostcardLongMm = 152.4;
    private const double EnvelopeShortMm = 110.0;
    private const double EnvelopeLongMm = 220.0;

    private static readonly IReadOnlyList<string> Tags = new[]
    {
        "Common", "Contactless", "Mifare", "Ultralight", "Contact", "Magnetic",
        "2 Track", "3 Track", "Combo", "Specific", "Inkjet", "Postcard", "Business",
        "Envelope", "User models",
    };

    public BlankCardCatalogService(ICardFormatCatalogService formatCatalog)
    {
        _formatCatalog = formatCatalog;
    }

    public Task<IReadOnlyList<string>> GetFilterTagsAsync() => Task.FromResult(Tags);

    public Task<IReadOnlyList<CardFormatOption>> GetFormatsAsync() =>
        Task.FromResult(BuildFormats());

    private IReadOnlyList<CardFormatOption> BuildFormats()
    {
        var standard = _formatCatalog.GetStandardFormats();
        CardFormatDefinition ByCode(string code) => standard.First(f => f.FormatCode == code);

        var cr79 = ByCode("CR79");
        var cr80 = ByCode("CR80");
        var cr90 = ByCode("CR90");
        var cr100 = ByCode("CR100");

        // ICardFormatCatalogService.FormatCode is the bare "CR80"/"CR79"/etc used
        // internally; the Blank Card page's existing Format dropdown and tile labels
        // use the hyphenated "CR-80" form (see the screenshot this was built against
        // and BlankCardViewModel.FormatOptions). Re-hyphenating here keeps that
        // existing display convention intact while still sourcing the width/height
        // numbers themselves from the single shared catalog.
        static string Hyphenated(string formatCode) => "CR-" + formatCode["CR".Length..];

        return new List<CardFormatOption>
        {
            // --- CR-80 (unchanged from before) ---
            NewSized("common-landscape", "Blank CR-80", CardOrientation.Landscape, cr80.WidthMm, cr80.HeightMm, Hyphenated(cr80.FormatCode), "Common"),
            NewSized("common-portrait", "Blank CR-80 (Portrait)", CardOrientation.Portrait, cr80.HeightMm, cr80.WidthMm, Hyphenated(cr80.FormatCode), "Common"),
            NewSized("generic-contact", "Generic Contact Chip", CardOrientation.Landscape, cr80.WidthMm, cr80.HeightMm, Hyphenated(cr80.FormatCode), "Common", "Contact"),
            NewSized("contactless", "Contactless", CardOrientation.Landscape, cr80.WidthMm, cr80.HeightMm, Hyphenated(cr80.FormatCode), "Common", "Contactless"),
            NewSized("mifare-classic", "Mifare Classic", CardOrientation.Landscape, cr80.WidthMm, cr80.HeightMm, Hyphenated(cr80.FormatCode), "Common", "Contactless", "Mifare"),
            NewSized("mifare-ultralight", "Mifare Ultralight", CardOrientation.Landscape, cr80.WidthMm, cr80.HeightMm, Hyphenated(cr80.FormatCode), "Contactless", "Ultralight"),
            NewSized("hico-magnetic", "HiCo Magnetic Stripe", CardOrientation.Landscape, cr80.WidthMm, cr80.HeightMm, Hyphenated(cr80.FormatCode), "Common", "Magnetic"),
            NewSized("loco-2track", "LoCo Magnetic (2 Track)", CardOrientation.Landscape, cr80.WidthMm, cr80.HeightMm, Hyphenated(cr80.FormatCode), "Magnetic", "2 Track"),
            NewSized("loco-3track", "LoCo Magnetic (3 Track)", CardOrientation.Landscape, cr80.WidthMm, cr80.HeightMm, Hyphenated(cr80.FormatCode), "Magnetic", "3 Track"),
            NewSized("combo-card", "Combo (Chip + Magnetic)", CardOrientation.Landscape, cr80.WidthMm, cr80.HeightMm, Hyphenated(cr80.FormatCode), "Combo", "Contact", "Magnetic"),
            NewSized("desfire", "DESFire EV2", CardOrientation.Landscape, cr80.WidthMm, cr80.HeightMm, Hyphenated(cr80.FormatCode), "Contactless", "Specific"),
            NewSized("business-card", "Business Card", CardOrientation.Landscape, cr80.WidthMm, cr80.HeightMm, Hyphenated(cr80.FormatCode), "Business"),
            NewSized("inkjet", "Inkjet Printable", CardOrientation.Landscape, cr80.WidthMm, cr80.HeightMm, Hyphenated(cr80.FormatCode), "Inkjet"),

            // --- CR-79 / CR-90 / CR-100 (Priority 2 addition) ---
            // Dimensions come straight from ICardFormatCatalogService.GetStandardFormats()
            // -- the same numbers the Card Designer's own "Create my own card" / format
            // picker uses -- so there is exactly one place in the codebase these
            // millimeter figures are defined.
            NewSized("cr79-landscape", "Blank CR-79", CardOrientation.Landscape, cr79.WidthMm, cr79.HeightMm, Hyphenated(cr79.FormatCode), "Common"),
            NewSized("cr90-landscape", "Blank CR-90", CardOrientation.Landscape, cr90.WidthMm, cr90.HeightMm, Hyphenated(cr90.FormatCode), "Common"),
            NewSized("cr100-landscape", "Blank CR-100", CardOrientation.Landscape, cr100.WidthMm, cr100.HeightMm, Hyphenated(cr100.FormatCode), "Common"),

            // --- Non-card-stock formats (Priority 2 fix: real dimensions, not CR-80's) ---
            NewSized("postcard", "Postcard", CardOrientation.Landscape, PostcardLongMm, PostcardShortMm, "Postcard", "Postcard"),
            NewSized("envelope", "Envelope", CardOrientation.Landscape, EnvelopeLongMm, EnvelopeShortMm, "Envelope", "Envelope"),
        };
    }

    private static CardFormatOption NewSized(
        string id, string name, CardOrientation orientation, double widthMm, double heightMm, string cardSizeName, params string[] tags) =>
        new()
        {
            Id = id,
            Name = name,
            CardSizeName = cardSizeName,
            Orientation = orientation,
            WidthMm = widthMm,
            HeightMm = heightMm,
            Tags = tags,
            PreviewLabel = tags.Length > 0 ? tags[0] : name,
        };
}
