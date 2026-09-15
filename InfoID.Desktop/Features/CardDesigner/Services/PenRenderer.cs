using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Single source of truth for drawing a PenElement (freehand Pen-tool stroke) into a
/// DrawingContext -- shared by CardCanvasView (live canvas) and ThumbnailRenderer (Recent
/// Cards thumbnails + Print Preview), the same "one shared renderer" pattern ShapeRenderer
/// and BarcodeRenderer already use in this codebase, specifically to avoid the class of
/// bug ThumbnailRenderer's own history already recorded once (a Star shape rendering
/// wrong in thumbnails because two copies of its drawing code drifted apart).
/// </summary>
public static class PenRenderer
{
    public static void Draw(DrawingContext context, Rect rect, PenElement pen)
    {
        if (pen.PointsFraction.Count < 2) return;

        var points = new List<Point>(pen.PointsFraction.Count);
        foreach (var fraction in pen.PointsFraction)
        {
            points.Add(ToDevicePoint(rect, fraction));
        }

        var geometry = new StreamGeometry();
        using (var gc = geometry.Open())
        {
            gc.BeginFigure(points[0], isFilled: false);

            if (points.Count == 2)
            {
                gc.LineTo(points[1]);
            }
            else
            {
                // Fit a smooth curve through the captured points (Catmull-Rom, converted
                // to a cubic Bezier per segment) instead of connecting them with straight
                // LineTo segments. Points are now captured several mm apart (see
                // CardCanvasView's MinPenPointSpacingMm -- close, node-editable spacing
                // was the actual ask), so a straight-segment polyline would render as a
                // visibly faceted/polygonal line rather than the smooth ink stroke a Pen
                // tool is supposed to produce; this is the standard technique real
                // drawing/vector tools use for exactly that reason.
                for (var i = 0; i < points.Count - 1; i++)
                {
                    var p0 = i == 0 ? points[i] : points[i - 1];
                    var p1 = points[i];
                    var p2 = points[i + 1];
                    var p3 = i + 2 < points.Count ? points[i + 2] : points[i + 1];

                    var control1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                    var control2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);
                    gc.CubicBezierTo(control1, control2, p2);
                }
            }

            gc.EndFigure(false);
        }

        var strokePen = new Pen(ParseBrush(pen.StrokeColorHex), pen.StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, strokePen, geometry);
    }

    private static Point ToDevicePoint(Rect rect, PenPoint fraction) =>
        new(rect.X + fraction.X * rect.Width, rect.Y + fraction.Y * rect.Height);

    private static IBrush ParseBrush(string hex) =>
        Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : Brushes.Black;
}
