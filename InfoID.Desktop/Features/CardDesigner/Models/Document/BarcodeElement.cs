using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

// Appended, not inserted: same plain-integer-ordinal serialization rule noted on
// ShapeKind/PhotoMaskShape -- new members must always go at the end.
public enum BarcodeSymbology { Code128, Code39, Ean13, Upc, Itf, DataMatrix, Pdf417, Aztec, Ean8 }

public sealed partial class BarcodeElement : DesignerElement
{
    public override ElementType ElementType => ElementType.Barcode;

    [ObservableProperty] private BarcodeSymbology _symbology = BarcodeSymbology.Code128;
    /// <summary>Static value, or a "{{Field}}" binding via DataBindingExpression.</summary>
    [ObservableProperty] private string _value = "0000000000";
    [ObservableProperty] private bool _showHumanReadableText = true;
    [ObservableProperty] private string _foregroundColorHex = "#000000";
    [ObservableProperty] private string _backgroundColorHex = "#FFFFFF";
    [ObservableProperty] private double _quietZoneMm = 2;
}
