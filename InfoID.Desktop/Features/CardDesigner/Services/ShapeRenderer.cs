using System;
using Avalonia;
using Avalonia.Media;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Single source of truth for drawing a ShapeElement into a DrawingContext, for every
/// ShapeKind. Extracted out of CardCanvasView (the live, interactive canvas) and
/// ThumbnailRenderer (Recent Cards thumbnails + Print Preview) so there is exactly one
/// place this logic lives -- those two previously carried byte-for-byte duplicated copies
/// of this exact code, and ThumbnailRenderer's own doc comment records that the
/// duplication already caused a real bug once (a Star rendering as a plain rectangle in
/// thumbnails/print preview because only one of the two copies was updated). Both call
/// sites now just call Draw() below.
/// </summary>
public static class ShapeRenderer
{
    /// <summary>Builds a clip geometry for the "true" mask shapes -- ones that aren't
    /// expressible as a plain rect/RoundedRect (which callers apply themselves via
    /// context.PushClip, no Geometry object needed) -- reusing the exact same point
    /// math this class already uses to DRAW a Hexagon/Star ShapeElement, so a mask and
    /// an actual hexagon/star shape are always geometrically identical. Returns null
    /// for Rectangle/RoundedRectangle, which callers handle themselves.</summary>
    public static Geometry? BuildMaskClipGeometry(Rect rect, PhotoMaskShape maskShape) => maskShape switch
    {
        PhotoMaskShape.Circle => new EllipseGeometry(rect),
        PhotoMaskShape.Hexagon => BuildPolygonGeometry(PolygonPoints(6, rect, startAngleDeg: -90)),
        PhotoMaskShape.Star => BuildPolygonGeometry(StarPoints(5, rect, 0.5)),
        _ => null,
    };

    public static void Draw(DrawingContext context, Rect rect, ShapeElement shape)
    {
        IBrush? fill = shape.FillEnabled ? ParseBrush(shape.FillColorHex) : null;
        double[]? dashes = shape.DashStyle switch
        {
            LineDashStyle.Dashed => new[] { 4.0, 2.0 },
            LineDashStyle.Dotted => new[] { 1.0, 2.0 },
            _ => null,
        };
        Pen? pen = shape.StrokeWidth > 0
            ? new Pen(ParseBrush(shape.StrokeColorHex), shape.StrokeWidth, dashStyle: dashes is null ? null : new DashStyle(dashes, 0))
            : null;

        switch (shape.Kind)
        {
            case ShapeKind.Ellipse:
                context.DrawEllipse(fill, pen, rect);
                break;

            case ShapeKind.Line:
                context.DrawLine(pen ?? new Pen(ParseBrush(shape.StrokeColorHex), 1), rect.TopLeft, new Point(rect.Right, rect.Bottom));
                break;

            case ShapeKind.Arrow:
                DrawArrow(context, rect, shape, pen ?? new Pen(ParseBrush(shape.StrokeColorHex), 1));
                break;

            case ShapeKind.Triangle:
                context.DrawGeometry(fill, pen, BuildPolygonGeometry(PolygonPoints(3, rect, startAngleDeg: -90)));
                break;

            case ShapeKind.RightTriangle:
                context.DrawGeometry(fill, pen, BuildPolygonGeometry(RightTrianglePoints(rect)));
                break;

            case ShapeKind.Polygon:
                context.DrawGeometry(fill, pen, BuildPolygonGeometry(PolygonPoints(Math.Max(3, shape.PolygonSides), rect, startAngleDeg: -90)));
                break;

            case ShapeKind.Parallelogram:
                context.DrawGeometry(fill, pen, BuildPolygonGeometry(ParallelogramPoints(rect)));
                break;

            case ShapeKind.Star:
                context.DrawGeometry(fill, pen, BuildPolygonGeometry(StarPoints(Math.Max(3, shape.StarPoints), rect, shape.StarInnerRadiusRatio)));
                break;

            default: // Rectangle
                if (shape.CornerRadius > 0)
                {
                    context.DrawRectangle(fill, pen, new RoundedRect(rect, shape.CornerRadius));
                }
                else
                {
                    context.DrawRectangle(fill, pen, rect);
                }
                break;
        }
    }

    private static void DrawArrow(DrawingContext context, Rect rect, ShapeElement shape, Pen pen)
    {
        var start = rect.TopLeft;
        var end = new Point(rect.Right, rect.Bottom);
        context.DrawLine(pen, start, end);

        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var headLen = Math.Max(4, pen.Thickness * 4);
        const double headAngle = Math.PI / 7;

        if (shape.EndArrowhead) DrawArrowhead(context, pen, end, angle, headLen, headAngle);
        if (shape.StartArrowhead) DrawArrowhead(context, pen, start, angle + Math.PI, headLen, headAngle);
    }

    private static void DrawArrowhead(DrawingContext context, Pen pen, Point tip, double angle, double length, double spread)
    {
        var p1 = new Point(tip.X - length * Math.Cos(angle - spread), tip.Y - length * Math.Sin(angle - spread));
        var p2 = new Point(tip.X - length * Math.Cos(angle + spread), tip.Y - length * Math.Sin(angle + spread));
        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(p1, isFilled: true);
            gc.LineTo(tip);
            gc.LineTo(p2);
            gc.EndFigure(false);
        }
        context.DrawGeometry(pen.Brush, pen, geo);
    }

    /// <summary>Evenly-spaced points around an ellipse inscribed in rect -- used for
    /// Triangle (3 points) and Polygon (n points; Diamond/Pentagon/Hexagon/Heptagon/
    /// Octagon are all just Polygon with PolygonSides 4/5/6/7/8 -- see ShapeCatalog).
    /// startAngleDeg=-90 puts the first point at the top, matching how a triangle/
    /// hexagon/etc. is conventionally drawn upright rather than tipped onto a flat
    /// edge; for 4 sides this places points at top/right/bottom/left, i.e. a diamond,
    /// not an axis-aligned square.</summary>
    private static Point[] PolygonPoints(int sides, Rect rect, double startAngleDeg)
    {
        var cx = rect.X + rect.Width / 2;
        var cy = rect.Y + rect.Height / 2;
        var rx = rect.Width / 2;
        var ry = rect.Height / 2;
        var points = new Point[sides];
        for (var i = 0; i < sides; i++)
        {
            var angle = (startAngleDeg + 360.0 * i / sides) * Math.PI / 180.0;
            points[i] = new Point(cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle));
        }
        return points;
    }

    private static Point[] StarPoints(int points, Rect rect, double innerRadiusRatio)
    {
        var cx = rect.X + rect.Width / 2;
        var cy = rect.Y + rect.Height / 2;
        var rx = rect.Width / 2;
        var ry = rect.Height / 2;
        var inner = Math.Clamp(innerRadiusRatio, 0.05, 0.95);
        var result = new Point[points * 2];
        for (var i = 0; i < points * 2; i++)
        {
            var angle = (-90 + 180.0 * i / points) * Math.PI / 180.0;
            var r = i % 2 == 0 ? 1.0 : inner;
            result[i] = new Point(cx + rx * r * Math.Cos(angle), cy + ry * r * Math.Sin(angle));
        }
        return result;
    }

    /// <summary>Right angle at the bottom-left corner of the bounding box.</summary>
    private static Point[] RightTrianglePoints(Rect rect) => new[]
    {
        rect.TopLeft,
        rect.BottomLeft,
        rect.BottomRight,
    };

    /// <summary>Classic "flowchart I/O" slant: top edge shifted right relative to the
    /// bottom edge by a fixed fraction of the width.</summary>
    private static Point[] ParallelogramPoints(Rect rect)
    {
        var skew = rect.Width * 0.25;
        return new[]
        {
            new Point(rect.X + skew, rect.Y),
            new Point(rect.Right, rect.Y),
            new Point(rect.Right - skew, rect.Bottom),
            new Point(rect.X, rect.Bottom),
        };
    }

    private static StreamGeometry BuildPolygonGeometry(Point[] points)
    {
        var geo = new StreamGeometry();
        using var gc = geo.Open();
        gc.BeginFigure(points[0], isFilled: true);
        for (var i = 1; i < points.Length; i++) gc.LineTo(points[i]);
        gc.EndFigure(true);
        return geo;
    }

    private static IBrush ParseBrush(string? hex)
    {
        try { return new SolidColorBrush(Color.Parse(hex ?? "#000000")); }
        catch { return Brushes.Black; }
    }
}
