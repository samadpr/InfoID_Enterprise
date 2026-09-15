using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

// New members must always be appended at the end, never inserted/reordered: ShapeKind
// serializes as a plain integer ordinal (no [JsonConverter]/string enum anywhere in this
// codebase), so an already-saved design's shape would silently reinterpret as the wrong
// kind if an earlier member's ordinal ever shifted.
public enum ShapeKind { Rectangle, Ellipse, Line, Arrow, Triangle, Polygon, Star, RightTriangle, Parallelogram }
public enum LineDashStyle { Solid, Dashed, Dotted }

public sealed partial class ShapeElement : DesignerElement
{
    public override ElementType ElementType => ElementType.Shape;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLineOrArrow))]
    [NotifyPropertyChangedFor(nameof(IsRectangleKind))]
    [NotifyPropertyChangedFor(nameof(IsPolygonKind))]
    [NotifyPropertyChangedFor(nameof(IsStarKind))]
    [NotifyPropertyChangedFor(nameof(IsArrowKind))]
    private ShapeKind _kind = ShapeKind.Rectangle;

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

    /// <summary>Used by Kind == Line/Arrow only. A bounding box has two diagonals, and
    /// Width/Height alone can only pick one of them (top-left to bottom-right); this
    /// flag selects the other (bottom-left to top-right) -- see ShapeRenderer.Draw's
    /// Line/Arrow cases and DrawArrow. Needed so the Line tool (CardDesignTabViewModel.
    /// InsertDrawnLine) can faithfully reproduce a line the user actually dragged
    /// upward-and-rightward (or downward-and-leftward) instead of silently flattening
    /// every drag direction onto the same diagonal.</summary>
    [ObservableProperty] private bool _lineFlipped;

    /// <summary>Drives the Properties panel's Kind-conditional sections (Views/
    /// CardDesignerView.axaml's ShapeElement DataTemplate) -- there's no per-element
    /// ViewModel in this codebase for a converter to reach through, so these live
    /// directly on the model, same as everywhere else DesignerElement/ShapeElement
    /// already exposes plain data for direct XAML binding.</summary>
    public bool IsLineOrArrow => Kind is ShapeKind.Line or ShapeKind.Arrow;
    public bool IsRectangleKind => Kind == ShapeKind.Rectangle;
    public bool IsPolygonKind => Kind == ShapeKind.Polygon;
    public bool IsStarKind => Kind == ShapeKind.Star;
    public bool IsArrowKind => Kind == ShapeKind.Arrow;
}
