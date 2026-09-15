using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

// Appended, not inserted: same plain-integer-ordinal serialization rule as ShapeKind
// (see its own comment in ShapeElement.cs) -- new members must always go at the end.
public enum PhotoMaskShape { Rectangle, RoundedRectangle, Circle, Hexagon, Star }

/// <summary>Cardholder photo slot -- distinct from ImageElement because it carries
/// crop/mask state and is meant to bind to a cardholder data field (Part 20).</summary>
public sealed partial class PhotoElement : DesignerElement
{
    public override ElementType ElementType => ElementType.Photo;

    [ObservableProperty] private string? _assetReference;
    [ObservableProperty] private PhotoMaskShape _maskShape = PhotoMaskShape.Rectangle;
    [ObservableProperty] private double _cornerRadius;
    [ObservableProperty] private double _cropX;
    [ObservableProperty] private double _cropY;
    [ObservableProperty] private double _cropZoom = 1.0;
    [ObservableProperty] private string _backgroundColorHex = "#F0F0F0";
}
