using CommunityToolkit.Mvvm.ComponentModel;
using InfoID.Desktop.Features.CardDesigner.Services;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

/// <summary>A pure mail-merge text placeholder bound to a cardholder column, rendered
/// like TextElement but semantically distinct so the layers panel and data-preview can
/// tell "static text" and "bound field" apart at a glance (Part 24).</summary>
public sealed partial class DataFieldElement : DesignerElement
{
    public override ElementType ElementType => ElementType.DataField;

    [ObservableProperty] private string _fieldKey = "FirstName";
    [ObservableProperty] private string _fontFamily = "Segoe UI";

    partial void OnFontFamilyChanged(string value) => RecentFontsTracker.Record(value);
    [ObservableProperty] private double _fontSize = 12;
    [ObservableProperty] private string _colorHex = "#000000";
    [ObservableProperty] private TextAlignmentX _horizontalAlignment = TextAlignmentX.Left;
}
