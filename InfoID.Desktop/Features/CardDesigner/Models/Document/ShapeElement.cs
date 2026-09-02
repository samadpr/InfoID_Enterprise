using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

public enum ShapeKind { Rectangle, Ellipse, Line, Arrow, Triangle, Polygon, Star }
public enum LineDashStyle { Solid, Dashed, Dotted }

public sealed partial class ShapeElement : DesignerElement
{
    public override ElementType ElementType => ElementType.Shape;

    [ObservableProperty] private ShapeKind _kind = ShapeKind.Rectangle;
    [ObservableProperty] private string _fillColorHex = "#DA3025";
    [ObservableProperty] private bool _fillEnabled = true;
    [ObservableProperty] private string _strokeColorHex = "#000000";
    [ObservableProperty] private double _strokeWidth;
    [ObservableProperty] private double _cornerRadius;
    [ObservableProperty] private double _shadowBlur;
    [ObservableProperty] private double _shadowOffsetX;
    [ObservableProperty] private double _shadowOffsetY;
    [ObservableProperty] private string _shadowColorHex = "#40000000";
    [ObservableProperty] private LineDashStyle _dashStyle = LineDashStyle.Solid;

    /// <summary>Used by Kind == Polygon. Triangle/Star ignore this (Triangle is always
    /// 3 sides; Star's point count is StarPoints below).</summary>
    [ObservableProperty] private int _polygonSides = 6;

    /// <summary>Used by Kind == Star.</summary>
    [ObservableProperty] private int _starPoints = 5;
    [ObservableProperty] private double _starInnerRadiusRatio = 0.5;

    /// <summary>Used by Kind == Line/Arrow -- Height is usually 0 (a line has no
    /// thickness of its own; StrokeWidth controls visual thickness), so Line/Arrow
    /// geometry is built from (0,0) to (Width,Height) instead of treating the element
    /// like a filled box the way Rectangle/Ellipse/Triangle/Polygon/Star do.</summary>
    [ObservableProperty] private bool _startArrowhead;
    [ObservableProperty] private bool _endArrowhead = true;
}
