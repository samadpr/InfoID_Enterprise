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

    /// <summary>Fixed value or a database field (see CodeValueBinding).</summary>
    [ObservableProperty] private CodeValueSource _sourceType = CodeValueSource.Fixed;

    /// <summary>The database column encoded when SourceType is Database.</summary>
    [ObservableProperty] private string _databaseColumn = string.Empty;

    /// <summary>Text added before / after the field value, e.g. "EMP-" + "1001".</summary>
    [ObservableProperty] private string _prefix = string.Empty;
    [ObservableProperty] private string _suffix = string.Empty;

    /// <summary>Choosing Fixed drops a "combine fields" expression, so the typed Value is
    /// what gets encoded. (Loading a design never triggers this: Fixed is the default.)</summary>
    partial void OnSourceTypeChanged(CodeValueSource value)
    {
        if (value == CodeValueSource.Fixed) DataBindingExpression = null;
    }
    [ObservableProperty] private bool _showHumanReadableText = true;
    [ObservableProperty] private string _foregroundColorHex = "#000000";
    [ObservableProperty] private string _backgroundColorHex = "#FFFFFF";
    [ObservableProperty] private double _quietZoneMm = 2;
}
