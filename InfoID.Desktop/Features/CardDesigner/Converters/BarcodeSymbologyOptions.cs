using System.Collections.Generic;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>The Barcode Properties panel's Symbology ComboBox ItemsSource -- every
/// BarcodeSymbology value, in the same order as the enum (which is itself append-only;
/// see BarcodeElement.cs's own comment). SelectedItem binds directly to the element's
/// Symbology property (same enum type), so this needs no converter -- the ComboBox's
/// default ToString() rendering of each enum member (Code128, Ean13, DataMatrix, ...)
/// is already readable enough for this list.</summary>
public static class BarcodeSymbologyOptions
{
    public static readonly IReadOnlyList<BarcodeSymbology> All = new[]
    {
        BarcodeSymbology.Code128,
        BarcodeSymbology.Code39,
        BarcodeSymbology.Ean13,
        BarcodeSymbology.Ean8,
        BarcodeSymbology.Upc,
        BarcodeSymbology.Itf,
        BarcodeSymbology.DataMatrix,
        BarcodeSymbology.Pdf417,
        BarcodeSymbology.Aztec,
    };
}
