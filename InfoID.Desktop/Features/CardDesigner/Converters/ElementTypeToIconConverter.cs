using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Views.Icons;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>Layers panel icon-first redesign (Part 19): each row shows a small icon for
/// its element instead of the raw enum name as button text. Centralized here (one
/// switch) rather than duplicated per-DataTemplate so every layer row -- current and any
/// future ones -- stays in sync automatically.
///
/// Binds against the whole DesignerElement (not just ElementType) so a ShapeElement can
/// get its actual ShapeKind-specific icon (Star vs Rectangle vs Hexagon, ...) instead of
/// every shape showing the same generic Rectangle icon regardless of what it actually
/// is (Part 90). Several of ShapeCatalog's named presets share one ShapeKind with
/// different parameters (Rounded Rectangle/Circle/Diamond/Pentagon/Hexagon/Heptagon/
/// Octagon/Double Arrow/Burst -- see ShapeCatalog's own doc comment), so ShapeIconFor
/// below inspects those parameters too, not just Kind, to show the specific icon that
/// actually matches what's on the canvas.</summary>
public sealed class ElementTypeToIconConverter : IValueConverter
{
    public static readonly ElementTypeToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ShapeElement shape => ShapeIconFor(shape),
            TextElement => DesignerIcons.Text,
            DateTimeElement dateTime => DateTimeIconFor(dateTime),
            ImageElement => DesignerIcons.Image,
            PhotoElement => DesignerIcons.Photo,
            SignatureElement => DesignerIcons.Signature,
            BarcodeElement => DesignerIcons.Barcode,
            QrCodeElement => DesignerIcons.QrCode,
            DataFieldElement => DesignerIcons.DataField,
            DesignerElement el when el.ElementType == ElementType.Group => DesignerIcons.Group,
            _ => DesignerIcons.LayerGeneric,
        };

    private static Geometry ShapeIconFor(ShapeElement shape) => shape.Kind switch
    {
        ShapeKind.Ellipse => Math.Abs(shape.Width - shape.Height) < 0.01 ? DesignerIcons.Circle : DesignerIcons.Ellipse,
        ShapeKind.Line => DesignerIcons.Line,
        ShapeKind.Arrow => shape.StartArrowhead && shape.EndArrowhead ? DesignerIcons.DoubleArrow : DesignerIcons.Arrow,
        ShapeKind.Triangle => DesignerIcons.Triangle,
        ShapeKind.RightTriangle => DesignerIcons.RightTriangle,
        ShapeKind.Parallelogram => DesignerIcons.Parallelogram,
        ShapeKind.Polygon => shape.PolygonSides switch
        {
            4 => DesignerIcons.Diamond,
            5 => DesignerIcons.Pentagon,
            6 => DesignerIcons.Hexagon,
            7 => DesignerIcons.Heptagon,
            8 => DesignerIcons.Octagon,
            _ => DesignerIcons.Polygon,
        },
        ShapeKind.Star => shape.StarPoints >= 10 ? DesignerIcons.Burst : DesignerIcons.Star,
        _ => shape.CornerRadius > 0 ? DesignerIcons.RoundedRectangle : DesignerIcons.Rectangle, // Rectangle
    };

    private static Geometry DateTimeIconFor(DateTimeElement dateTime) => dateTime.DisplayFormat switch
    {
        DateTimeDisplayFormat.Date => DesignerIcons.Calendar,
        DateTimeDisplayFormat.Time => DesignerIcons.Clock,
        DateTimeDisplayFormat.DateTime => DesignerIcons.CalendarClock,
        _ => DesignerIcons.Calendar,
    };
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
