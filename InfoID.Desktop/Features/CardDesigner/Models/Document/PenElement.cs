using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

/// <summary>One point of a freehand Pen stroke, stored as a fraction (0..1) of the
/// element's own Width/Height -- not absolute millimeters. That's deliberate: it's the
/// same convention ShapeRenderer's polygon/star point-generation already uses (a point at
/// X=0.5 always means "halfway across the box"), so dragging PenElement's existing
/// bounding-box resize handles (the same 4-corner system every other element already has,
/// PenElement gets nothing bespoke here) rescales the whole stroke correctly instead of
/// leaving it pinned to stale absolute coordinates.</summary>
public readonly record struct PenPoint(double X, double Y);

/// <summary>A freehand line drawn with the Pen tool (CardCanvasView's click-drag
/// gesture -- see CardDesignTabViewModel.InsertDrawnPen). Deliberately a new element
/// type rather than reusing ShapeElement: ShapeElement's Kind=Line is a single straight
/// segment defined purely by its bounding box (rect.TopLeft to rect.BottomRight), which
/// has no way to represent an arbitrary multi-point stroke.</summary>
public sealed partial class PenElement : DesignerElement
{
    public override ElementType ElementType => ElementType.Pen;

    [ObservableProperty] private string _strokeColorHex = "#1F2328";
    [ObservableProperty] private double _strokeWidth = 1.5;

    /// <summary>At least 2 points once actually inserted -- CardDesignTabViewModel.
    /// InsertDrawnPen discards a shorter capture as an accidental click, the same way
    /// the Image Editor's crop-drag already discards sub-4px drags. Not enforced here on
    /// the model itself (an under-2-point collection is simply invalid, not something
    /// this type needs to guard against at construction time); PenRenderer treats "not
    /// enough points to draw a line" as "draw nothing" rather than throwing, so a
    /// hand-edited or corrupt file with too few points fails safe.</summary>
    public ObservableCollection<PenPoint> PointsFraction { get; set; } = new();
}
