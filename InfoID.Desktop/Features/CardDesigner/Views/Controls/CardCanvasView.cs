using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using InfoID.Desktop.Features.CardDesigner.History;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.ViewModels;

namespace InfoID.Desktop.Features.CardDesigner.Views.Controls;

/// <summary>
/// The card design canvas -- a single custom-drawn Control rather than hundreds of
/// nested Avalonia controls per element, per Part 7 ("retained document model +
/// efficient rendering layer", not "the canvas as hundreds of nested ordinary
/// controls"). Reads directly from CardDesignTabViewModel.Document, draws every visible
/// element in one Render pass, and owns its own pointer/keyboard interaction (select,
/// drag-move, corner-handle resize, marquee multi-select, pan, zoom, delete, arrow-key
/// nudge).
///
/// During an active drag, element X/Y/Width/Height are mutated directly and the canvas
/// redraws itself immediately (no history entry, no serialization) -- the undo command
/// and dirty/autosave flag are only created once on PointerReleased, per Part 52's
/// "during drag: update in-memory document; after drag ends: create history command".
/// </summary>
public sealed class CardCanvasView : Control
{
    public static readonly StyledProperty<CardDesignTabViewModel?> TabProperty =
        AvaloniaProperty.Register<CardCanvasView, CardDesignTabViewModel?>(nameof(Tab));

    public CardDesignTabViewModel? Tab
    {
        get => GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    internal const double PixelsPerMm = 96.0 / 25.4;
    internal const double GapBetweenSidesMm = 12;

    /// <summary>Shared with RulerView so the ruler's tick marks can never drift from
    /// where the canvas actually draws the card -- both controls compute origin from
    /// their own Bounds.Width/Height plus the same tab.Zoom/PanX/PanY, which works
    /// because the ruler strips are laid out with the same width/height as the canvas
    /// (see CardDesignerView.axaml's Grid).</summary>
    internal static double ComputeOriginX(CardDesignTabViewModel tab, double controlWidth)
    {
        var scale = PixelsPerMm * tab.Zoom;
        var cardWpx = tab.Document.WidthMm * scale;
        var gapPx = GapBetweenSidesMm * scale;
        var totalWidth = tab.ActiveSideView == DesignerSideView.Both ? cardWpx * 2 + gapPx : cardWpx;
        return (controlWidth - totalWidth) / 2 + tab.PanX;
    }

    internal static double ComputeOriginY(CardDesignTabViewModel tab, double controlHeight)
    {
        var scale = PixelsPerMm * tab.Zoom;
        var cardHpx = tab.Document.HeightMm * scale;
        return (controlHeight - cardHpx) / 2 + tab.PanY;
    }

    private static readonly IBrush WorkspaceBackgroundBrush = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.Parse("#3B82F6"));
    private static readonly IBrush MarqueeFillBrush = new SolidColorBrush(Color.Parse("#333B82F6"));
    private static readonly IBrush GuideBrush = new SolidColorBrush(Color.Parse("#00B4D8"));
    private static readonly IBrush SmartGuideBrush = new SolidColorBrush(Color.Parse("#FF7A00"));
    private static readonly IBrush PlaceholderFillBrush = new SolidColorBrush(Color.Parse("#22FFFFFF"));
    private static readonly IBrush PlaceholderBorderBrush = Brushes.Gray;

    private enum DragMode { None, Move, ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight, Rotate, MarqueeSelect, Pan }

    private DragMode _dragMode = DragMode.None;
    private DesignerElement? _dragElement;
    private Point? _dragStartPointerPos;
    private Point _panStart;
    private Point _marqueeStart;
    private Rect? _marqueeRect;
    private bool _spacePressed;
    private double _dragStartRotation;
    private readonly Dictionary<DesignerElement, (double X, double Y, double W, double H)> _dragStartRects = new();

    // Smart guides (Part 20): lines currently "lit up" because the element being dragged
    // snapped to them this frame -- a ruler guide, the card's own center, or another
    // element's edge/center. Cleared whenever a drag isn't in progress.
    private const double SnapToleranceDevicePx = 6;
    private readonly List<(GuideOrientation Orientation, double Mm)> _activeSnapLines = new();

    // Decoded-bitmap cache for Image/Photo/Signature elements (Part 59: avoid re-decoding
    // from disk on every single Render() call, which would happen constantly during
    // pan/zoom/drag). Deliberately simple -- keyed by resolved full path, never evicted.
    // Fine for a single design session's realistic asset count; a longer-lived cache with
    // eviction is a reasonable follow-up if this ever becomes a real memory concern.
    private readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap?> _bitmapCache = new();

    public CardCanvasView()
    {
        Focusable = true;
        ClipToBounds = true;
        //Background = Brushes.Transparent; // ensures the whole control area is hit-testable
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TabProperty) return;

        if (change.OldValue is CardDesignTabViewModel oldTab)
        {
            oldTab.DocumentChanged -= OnTabChanged;
            oldTab.PropertyChanged -= OnTabPropertyChanged;
            oldTab.SelectedElements.CollectionChanged -= OnSelectionChanged;
        }

        if (change.NewValue is CardDesignTabViewModel newTab)
        {
            newTab.DocumentChanged += OnTabChanged;
            newTab.PropertyChanged += OnTabPropertyChanged;
            newTab.SelectedElements.CollectionChanged += OnSelectionChanged;
        }

        InvalidateVisual();
    }

    private void OnTabChanged(object? sender, EventArgs e) => InvalidateVisual();
    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();
    private void OnSelectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    /// <summary>Raised when the user double-clicks a TextElement on the canvas (Part 3 /
    /// 80). CardDesignerView owns the actual overlay TextBox (a plain Control like this
    /// one has no natural place to host a real editable text-input surface with caret/
    /// selection/IME support), so this control's job is only to detect the gesture and
    /// hand back which element -- positioning comes from GetElementScreenRect above.</summary>
    public event EventHandler<TextElement>? TextEditRequested;

    // ---------------------------------------------------------------- rendering ----

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(WorkspaceBackgroundBrush, new Rect(Bounds.Size));

        var tab = Tab;
        if (tab is null) return;

        var (scale, sideRects) = ComputeLayout(tab);

        foreach (var (side, rect) in sideRects)
        {
            DrawSide(context, tab, side, rect, scale);
        }

        if (_marqueeRect is { } marquee)
        {
            context.DrawRectangle(MarqueeFillBrush, new Pen(SelectionBrush, 1), marquee);
        }
    }

    private void DrawSide(DrawingContext context, CardDesignTabViewModel tab, CardSide side, Rect cardRect, double scale)
    {
        var doc = tab.Document;
        var radius = doc.CornerRadiusMm * scale;
        var roundedRect = new RoundedRect(cardRect, radius);

        var sideModel = doc.GetSide(side);
        var backgroundBrush = ResolveBackgroundBrush(sideModel.Background);

        context.DrawRectangle(backgroundBrush, new Pen(Brushes.Black, 1), roundedRect);

        using (context.PushClip(cardRect))
        {
            DrawGrid(context, cardRect, scale, doc);

            foreach (var element in sideModel.Elements.OrderBy(e => e.ZIndex))
            {
                // The element currently being edited inline (Part 3) is skipped here --
                // the overlay TextBox hosted by CardDesignerView is drawn on top of the
                // canvas at this element's position instead, so the user never sees the
                // static text and the live-editable text at once.
                if (ReferenceEquals(element, tab.InlineEditingElement)) continue;

                // Conditional visibility (Part 25/44): only actually hides elements in
                // Data Preview mode -- see IsElementVisibleNow's own doc comment for why
                // it's always true outside preview mode. (element.Visible, the manual
                // show/hide toggle, is already enforced inside DrawElement itself.)
                if (!tab.IsElementVisibleNow(element)) continue;

                DrawElement(context, cardRect, scale, element, tab);
            }
        }

        // Selection handles (including the rotation handle, which sits above the
        // element's top edge) are drawn OUTSIDE the card's clip region deliberately --
        // an element near the top of the card would otherwise have its rotation handle
        // clipped off and unreachable, which made rotation effectively unusable.
        if (side == tab.FocusedSide)
        {
            DrawSelectionOverlay(context, cardRect, scale, tab);
        }

        // Bleed / safe-area guides -- drawn outside the clip so they're always visible,
        // never part of the printable output (Part 14).
        if (doc.BleedMm > 0)
        {
            var bleedRect = cardRect.Inflate(doc.BleedMm * scale);
            context.DrawRectangle(null, new Pen(Brushes.OrangeRed, 1, dashStyle: DashStyle.Dash), bleedRect);
        }

        if (doc.SafeZoneMm > 0)
        {
            var safeRect = cardRect.Deflate(doc.SafeZoneMm * scale);
            context.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1, dashStyle: DashStyle.Dash), safeRect);
        }

        // Ruler guides + smart alignment guides (Part 20/21/23), only meaningful for
        // whichever side is currently being edited -- guides are a per-editing-context
        // aid, not part of the saved design, so they're drawn but never clipped into
        // the printable card content.
        if (side == tab.FocusedSide)
        {
            DrawGuides(context, cardRect, scale, tab);
        }
    }

    private void DrawGuides(DrawingContext context, Rect cardRect, double scale, CardDesignTabViewModel tab)
    {
        var guidePen = new Pen(GuideBrush, 1);
        foreach (var mm in tab.VerticalGuidesMm)
        {
            var x = cardRect.X + mm * scale;
            context.DrawLine(guidePen, new Point(x, cardRect.Y), new Point(x, cardRect.Bottom));
        }
        foreach (var mm in tab.HorizontalGuidesMm)
        {
            var y = cardRect.Y + mm * scale;
            context.DrawLine(guidePen, new Point(cardRect.X, y), new Point(cardRect.Right, y));
        }

        if (tab.GuidePreviewMm is { } previewMm)
        {
            var previewPen = new Pen(GuideBrush, 1, dashStyle: DashStyle.Dash);
            if (tab.GuidePreviewOrientation == GuideOrientation.Vertical)
            {
                var x = cardRect.X + previewMm * scale;
                context.DrawLine(previewPen, new Point(x, cardRect.Y), new Point(x, cardRect.Bottom));
            }
            else
            {
                var y = cardRect.Y + previewMm * scale;
                context.DrawLine(previewPen, new Point(cardRect.X, y), new Point(cardRect.Right, y));
            }
        }

        // Smart guides: highlighted only for the duration of an active snap (Part 20).
        var smartPen = new Pen(SmartGuideBrush, 1);
        foreach (var (orientation, mm) in _activeSnapLines)
        {
            if (orientation == GuideOrientation.Vertical)
            {
                var x = cardRect.X + mm * scale;
                context.DrawLine(smartPen, new Point(x, cardRect.Y), new Point(x, cardRect.Bottom));
            }
            else
            {
                var y = cardRect.Y + mm * scale;
                context.DrawLine(smartPen, new Point(cardRect.X, y), new Point(cardRect.Right, y));
            }
        }
    }

    private static IBrush ResolveBackgroundBrush(BackgroundSettings background) => background.Kind switch
    {
        BackgroundKind.Transparent => Brushes.Transparent,
        BackgroundKind.Gradient => new LinearGradientBrush
        {
            GradientStops =
            {
                new GradientStop(ParseColor(background.GradientStartHex ?? background.ColorHex), 0),
                new GradientStop(ParseColor(background.GradientEndHex ?? background.ColorHex), 1),
            },
        },
        // Image backgrounds render as their fallback color until the asset pipeline
        // (Part 50/51) resolves AssetReference to a loaded bitmap.
        _ => ParseBrush(background.ColorHex),
    };

    private static void DrawGrid(DrawingContext context, Rect cardRect, double scale, CardDesignDocument doc)
    {
        if (!doc.ShowGrid) return;
        var stepPx = doc.GridSizeMm * scale;
        if (stepPx < 4) return;

        var pen = new Pen(new SolidColorBrush(Color.Parse("#14000000")), 1);
        for (var x = cardRect.X; x <= cardRect.Right; x += stepPx)
        {
            context.DrawLine(pen, new Point(x, cardRect.Y), new Point(x, cardRect.Bottom));
        }
        for (var y = cardRect.Y; y <= cardRect.Bottom; y += stepPx)
        {
            context.DrawLine(pen, new Point(cardRect.X, y), new Point(cardRect.Right, y));
        }
    }

    private void DrawElement(DrawingContext context, Rect cardRect, double scale, DesignerElement element, CardDesignTabViewModel tab)
    {
        if (!element.Visible) return;

        var elementRect = ToDeviceRect(cardRect, scale, element);

        using var opacityScope = context.PushOpacity(Math.Clamp(element.Opacity, 0, 1));

        var center = elementRect.Center;
        var transform = Matrix.CreateTranslation(-center.X, -center.Y)
                        * Matrix.CreateRotation(element.Rotation * Math.PI / 180.0)
                        * Matrix.CreateTranslation(center.X, center.Y);
        using var transformScope = context.PushTransform(transform);

        switch (element)
        {
            case TextElement text:
                DrawText(context, elementRect, text, tab);
                break;
            case ShapeElement shape:
                DrawShape(context, elementRect, shape);
                break;
            case DataFieldElement field:
                DrawDataField(context, elementRect, field, tab);
                break;
            case ImageElement image:
                DrawImage(context, elementRect, image.AssetReference, image.FitMode, image.CornerRadius);
                break;
            case PhotoElement photo:
                DrawPhoto(context, elementRect, photo);
                break;
            case SignatureElement signature:
                DrawSignature(context, elementRect, signature);
                break;
            case BarcodeElement barcode:
                DrawBarcode(context, elementRect, barcode);
                break;
            default:
                DrawPlaceholder(context, elementRect, element);
                break;
        }
    }

    private static void DrawText(DrawingContext context, Rect rect, TextElement text, CardDesignTabViewModel tab)
    {
        var brush = ParseBrush(text.ColorHex);
        var weight = text.Bold ? FontWeight.Bold : FontWeight.Normal;
        var style = text.Italic ? FontStyle.Italic : FontStyle.Normal;
        var typeface = new Typeface(text.FontFamily, style, weight);

        // Data-bound text (Part 24): when a binding expression is set, the raw/design-
        // time content IS the expression itself (e.g. "{{FirstName}} {{LastName}}"),
        // kept visually distinguishable from ordinary static text; ResolveDisplayText
        // swaps in the evaluated value only while Data Preview is on.
        var displayText = string.IsNullOrEmpty(text.DataBindingExpression)
            ? text.Text
            : tab.ResolveDisplayText(text.DataBindingExpression, text.DataBindingExpression);

        var formatted = new FormattedText(
            string.IsNullOrEmpty(displayText) ? " " : displayText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            text.FontSize,
            brush)
        {
            MaxTextWidth = Math.Max(1, rect.Width),
            MaxTextHeight = Math.Max(1, rect.Height),
            TextAlignment = text.HorizontalAlignment switch
            {
                TextAlignmentX.Center => TextAlignment.Center,
                TextAlignmentX.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
        };

        var origin = rect.TopLeft;
        if (text.VerticalAlignment == TextAlignmentY.Middle)
        {
            origin = origin.WithY(rect.Center.Y - formatted.Height / 2);
        }
        else if (text.VerticalAlignment == TextAlignmentY.Bottom)
        {
            origin = origin.WithY(rect.Bottom - formatted.Height);
        }

        context.DrawText(formatted, origin);
    }

    private static void DrawDataField(DrawingContext context, Rect rect, DataFieldElement field, CardDesignTabViewModel tab)
    {
        var brush = ParseBrush(field.ColorHex);
        var typeface = new Typeface(field.FontFamily);
        var placeholder = $"{{{{{field.FieldKey}}}}}";
        var displayText = tab.ResolveDisplayText(placeholder, placeholder);

        var formatted = new FormattedText(displayText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, field.FontSize, brush)
        {
            MaxTextWidth = Math.Max(1, rect.Width),
        };
        context.DrawText(formatted, rect.TopLeft);
    }

    private Avalonia.Media.Imaging.Bitmap? LoadBitmap(string? assetReference)
    {
        var assetService = Tab?.AssetService;
        if (assetService is null || string.IsNullOrWhiteSpace(assetReference)) return null;

        var fullPath = assetService.ResolveToFullPath(assetReference);
        if (fullPath is null) return null;

        if (_bitmapCache.TryGetValue(fullPath, out var cached)) return cached;

        Avalonia.Media.Imaging.Bitmap? bitmap;
        try
        {
            bitmap = new Avalonia.Media.Imaging.Bitmap(fullPath);
        }
        catch (Exception)
        {
            bitmap = null; // corrupt/unsupported file -- render nothing rather than crash
        }
        _bitmapCache[fullPath] = bitmap;
        return bitmap;
    }

    /// <summary>Used by Image elements. Fit/Fill/Stretch match the semantics of CSS
    /// object-fit: Fit letterboxes to show the whole picture, Fill crops to cover the
    /// whole box, Stretch ignores aspect ratio entirely.</summary>
    private void DrawImage(DrawingContext context, Rect rect, string? assetReference, ImageFitMode fitMode, double cornerRadius)
    {
        using var clip = cornerRadius > 0
            ? context.PushClip(new RoundedRect(rect, Math.Min(cornerRadius, Math.Min(rect.Width, rect.Height) / 2)))
            : context.PushClip(rect);

        var bitmap = LoadBitmap(assetReference);
        if (bitmap is null)
        {
            context.DrawRectangle(PlaceholderFillBrush, new Pen(PlaceholderBorderBrush, 1, dashStyle: DashStyle.Dash), rect);
            return;
        }

        var sourceSize = bitmap.PixelSize.ToSizeWithDpi(bitmap.Dpi);
        Rect destRect;
        Rect sourceRect;

        switch (fitMode)
        {
            case ImageFitMode.Stretch:
                sourceRect = new Rect(sourceSize);
                destRect = rect;
                break;
            case ImageFitMode.Fill:
                var fillScale = Math.Max(rect.Width / sourceSize.Width, rect.Height / sourceSize.Height);
                var visibleW = rect.Width / fillScale;
                var visibleH = rect.Height / fillScale;
                sourceRect = new Rect((sourceSize.Width - visibleW) / 2, (sourceSize.Height - visibleH) / 2, visibleW, visibleH);
                destRect = rect;
                break;
            default: // Fit
                var fitScale = Math.Min(rect.Width / sourceSize.Width, rect.Height / sourceSize.Height);
                var fitW = sourceSize.Width * fitScale;
                var fitH = sourceSize.Height * fitScale;
                sourceRect = new Rect(sourceSize);
                destRect = new Rect(rect.X + (rect.Width - fitW) / 2, rect.Y + (rect.Height - fitH) / 2, fitW, fitH);
                break;
        }

        context.DrawImage(bitmap, sourceRect, destRect);
    }

    private void DrawPhoto(DrawingContext context, Rect rect, PhotoElement photo)
    {
        using var clip = photo.MaskShape switch
        {
            PhotoMaskShape.Circle => context.PushGeometryClip(new EllipseGeometry(rect)),
            PhotoMaskShape.RoundedRectangle => context.PushClip(new RoundedRect(rect, Math.Min(photo.CornerRadius, Math.Min(rect.Width, rect.Height) / 2))),
            _ => context.PushClip(rect),
        };

        context.FillRectangle(ParseBrush(photo.BackgroundColorHex), rect);

        var bitmap = LoadBitmap(photo.AssetReference);
        if (bitmap is null)
        {
            context.DrawRectangle(null, new Pen(PlaceholderBorderBrush, 1, dashStyle: DashStyle.Dash), rect);
            return;
        }

        var sourceSize = bitmap.PixelSize.ToSizeWithDpi(bitmap.Dpi);
        var fillScale = Math.Max(rect.Width / sourceSize.Width, rect.Height / sourceSize.Height) * Math.Max(photo.CropZoom, 0.1);
        var visibleW = rect.Width / fillScale;
        var visibleH = rect.Height / fillScale;
        var maxOffsetX = Math.Max(0, sourceSize.Width - visibleW);
        var maxOffsetY = Math.Max(0, sourceSize.Height - visibleH);
        var offsetX = Math.Clamp((sourceSize.Width - visibleW) / 2 + photo.CropX, 0, maxOffsetX);
        var offsetY = Math.Clamp((sourceSize.Height - visibleH) / 2 + photo.CropY, 0, maxOffsetY);
        var sourceRect = new Rect(offsetX, offsetY, Math.Min(visibleW, sourceSize.Width), Math.Min(visibleH, sourceSize.Height));

        context.DrawImage(bitmap, sourceRect, rect);
    }

    private void DrawSignature(DrawingContext context, Rect rect, SignatureElement signature)
    {
        var bitmap = LoadBitmap(signature.AssetReference);
        if (bitmap is null)
        {
            using var clip1 = context.PushClip(rect);
            context.DrawRectangle(null, new Pen(PlaceholderBorderBrush, 1, dashStyle: DashStyle.Dash), rect);
            return;
        }

        using var clip = context.PushClip(rect);
        var sourceSize = bitmap.PixelSize.ToSizeWithDpi(bitmap.Dpi);
        var fitScale = Math.Min(rect.Width / sourceSize.Width, rect.Height / sourceSize.Height);
        var fitW = sourceSize.Width * fitScale;
        var fitH = sourceSize.Height * fitScale;
        var destRect = new Rect(rect.X + (rect.Width - fitW) / 2, rect.Y + (rect.Height - fitH) / 2, fitW, fitH);
        context.DrawImage(bitmap, new Rect(sourceSize), destRect);

        // Signature "color" only applies as a flat tint over the bitmap's own alpha --
        // a full recolor (replacing black ink with an arbitrary color) needs pixel-level
        // processing this drawing pass deliberately doesn't do; documented in the phase
        // changelog rather than silently claimed as full recolor support.
    }

    private static void DrawShape(DrawingContext context, Rect rect, ShapeElement shape)
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
                context.DrawGeometry(fill, pen, BuildPolygonGeometry(rect, PolygonPoints(3, rect, startAngleDeg: -90)));
                break;

            case ShapeKind.Polygon:
                context.DrawGeometry(fill, pen, BuildPolygonGeometry(rect, PolygonPoints(Math.Max(3, shape.PolygonSides), rect, startAngleDeg: -90)));
                break;

            case ShapeKind.Star:
                context.DrawGeometry(fill, pen, BuildPolygonGeometry(rect, StarPoints(Math.Max(3, shape.StarPoints), rect, shape.StarInnerRadiusRatio)));
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
    /// Triangle (3 points) and Polygon (n points). startAngleDeg=-90 puts the first
    /// point at the top, matching how a triangle/hexagon/etc. is conventionally drawn
    /// upright rather than tipped onto a flat edge.</summary>
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

    private static StreamGeometry BuildPolygonGeometry(Rect rect, Point[] points)
    {
        var geo = new StreamGeometry();
        using var gc = geo.Open();
        gc.BeginFigure(points[0], isFilled: true);
        for (var i = 1; i < points.Length; i++) gc.LineTo(points[i]);
        gc.EndFigure(true);
        return geo;
    }

    /// <summary>Generic fallback for element types this pass doesn't have a specialized
    /// renderer for yet (Barcode/QR/Photo/Signature/Svg/...). Only appears if such an
    /// element already exists in a loaded document -- the toolbar in this phase only
    /// inserts Text/Rectangle/Ellipse, so no button in this build produces a
    /// placeholder-only element (Part 88).</summary>
    /// <summary>Deliberately NOT a real Code128/QR renderer yet. Generating a correct
    /// barcode requires an exact, correctness-critical symbol-pattern table (Code128) or
    /// a Reed-Solomon error-correction implementation (QR) -- getting either subtly wrong
    /// produces something that *looks* like a real barcode but silently fails to scan,
    /// which is worse than an honest placeholder (Part 81 says no fake barcode; a
    /// plausible-looking but non-functional one is arguably worse than an admitted
    /// placeholder). This shows the encoded value as text with a clear label instead of
    /// drawing fake bars, and is intentionally left for a follow-up once you've decided
    /// between a hand-rolled implementation (needs real scanner verification before you
    /// can trust it) or a small, well-maintained NuGet package (needs your confirmation
    /// per project policy) -- see the phase changelog.</summary>
    private static void DrawBarcode(DrawingContext context, Rect rect, BarcodeElement barcode)
    {
        context.DrawRectangle(PlaceholderFillBrush, new Pen(PlaceholderBorderBrush, 1, dashStyle: DashStyle.Dash), rect);
        var typeface = new Typeface("Segoe UI");
        var label = new FormattedText($"[{barcode.Symbology}]", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 9, Brushes.Gray);
        context.DrawText(label, rect.TopLeft + new Vector(4, 4));
        var valueText = new FormattedText(barcode.Value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, ParseBrush(barcode.ForegroundColorHex));
        context.DrawText(valueText, rect.TopLeft + new Vector(4, rect.Height / 2 - 6));
    }

    private static void DrawPlaceholder(DrawingContext context, Rect rect, DesignerElement element)
    {
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#22FFFFFF")), new Pen(Brushes.Gray, 1, dashStyle: DashStyle.Dash), rect);
        var formatted = new FormattedText(element.ElementType.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.Gray);
        context.DrawText(formatted, rect.TopLeft + new Vector(4, 4));
    }

    private void DrawSelectionOverlay(DrawingContext context, Rect cardRect, double scale, CardDesignTabViewModel tab)
    {
        foreach (var element in tab.SelectedElements)
        {
            var rect = ToDeviceRect(cardRect, scale, element);
            context.DrawRectangle(null, new Pen(SelectionBrush, 1.5, dashStyle: DashStyle.Dash), rect);
        }

        if (tab.SelectedElements.Count == 1)
        {
            var rect = ToDeviceRect(cardRect, scale, tab.SelectedElements[0]);
            foreach (var handle in GetHandleRects(rect))
            {
                context.DrawRectangle(Brushes.White, new Pen(SelectionBrush, 1), handle);
            }

            var rotationHandleCenter = new Point(rect.Center.X, rect.Top - 24);
            context.DrawLine(new Pen(SelectionBrush, 1), new Point(rect.Center.X, rect.Top), rotationHandleCenter);
            context.DrawEllipse(SelectionBrush, new Pen(Brushes.White, 1.5), rotationHandleCenter, 7, 7);
        }
    }

    private static IEnumerable<Rect> GetHandleRects(Rect r)
    {
        const double h = 8;
        yield return new Rect(r.TopLeft.X - h / 2, r.TopLeft.Y - h / 2, h, h);
        yield return new Rect(r.TopRight.X - h / 2, r.TopRight.Y - h / 2, h, h);
        yield return new Rect(r.BottomLeft.X - h / 2, r.BottomLeft.Y - h / 2, h, h);
        yield return new Rect(r.BottomRight.X - h / 2, r.BottomRight.Y - h / 2, h, h);
    }

    // ------------------------------------------------------------------- layout ----

    private (double Scale, Dictionary<CardSide, Rect> SideRects) ComputeLayout(CardDesignTabViewModel tab)
    {
        var doc = tab.Document;
        var scale = PixelsPerMm * tab.Zoom;
        var cardWpx = doc.WidthMm * scale;
        var cardHpx = doc.HeightMm * scale;
        var gapPx = GapBetweenSidesMm * scale;

        var originX = ComputeOriginX(tab, Bounds.Width);
        var originY = ComputeOriginY(tab, Bounds.Height);

        var rects = new Dictionary<CardSide, Rect>();
        switch (tab.ActiveSideView)
        {
            case DesignerSideView.Front:
                rects[CardSide.Front] = new Rect(originX, originY, cardWpx, cardHpx);
                break;
            case DesignerSideView.Back:
                rects[CardSide.Back] = new Rect(originX, originY, cardWpx, cardHpx);
                break;
            default:
                rects[CardSide.Front] = new Rect(originX, originY, cardWpx, cardHpx);
                rects[CardSide.Back] = new Rect(originX + cardWpx + gapPx, originY, cardWpx, cardHpx);
                break;
        }

        return (scale, rects);
    }

    private static Rect ToDeviceRect(Rect cardRect, double scale, DesignerElement el) =>
        new(cardRect.X + el.X * scale, cardRect.Y + el.Y * scale, el.Width * scale, el.Height * scale);

    /// <summary>Checks the dragged element's leading edge, center, and trailing edge
    /// (on one axis) against every candidate snap target for that axis -- ruler guides,
    /// the card's own center line, and every other unlocked element's leading edge/
    /// center/trailing edge on the same side -- and returns the delta (mm) needed to
    /// snap onto the closest one within tolerance, if any. Records which line matched
    /// into _activeSnapLines so Render() can draw it highlighted while the snap holds.</summary>
    private bool TrySnapAxisImpl(
        CardDesignTabViewModel tab, double toleranceMm, GuideOrientation orientation, DesignerElement dragging,
        double leading, double center, double trailing, out double delta)
    {
        delta = 0;
        var best = double.MaxValue;
        var found = false;
        var matchedTarget = 0.0;

        var guides = orientation == GuideOrientation.Vertical ? tab.VerticalGuidesMm : tab.HorizontalGuidesMm;
        var cardCenter = orientation == GuideOrientation.Vertical ? tab.Document.WidthMm / 2 : tab.Document.HeightMm / 2;

        var targets = new List<double>(guides) { cardCenter };
        foreach (var other in tab.FocusedSideModel.Elements)
        {
            if (ReferenceEquals(other, dragging) || _dragStartRects.ContainsKey(other)) continue;
            var bounds = GetVisualBoundsStatic(other);
            if (orientation == GuideOrientation.Vertical)
            {
                targets.Add(bounds.Left); targets.Add(bounds.Left + bounds.Width / 2); targets.Add(bounds.Right);
            }
            else
            {
                targets.Add(bounds.Top); targets.Add(bounds.Top + bounds.Height / 2); targets.Add(bounds.Bottom);
            }
        }

        foreach (var target in targets)
        {
            foreach (var edge in new[] { leading, center, trailing })
            {
                var dist = Math.Abs(edge - target);
                if (dist <= toleranceMm && dist < best)
                {
                    best = dist;
                    delta = target - edge;
                    matchedTarget = target;
                    found = true;
                }
            }
        }

        if (found) _activeSnapLines.Add((orientation, matchedTarget));
        return found;
    }

    private double TrySnapAxis(
        CardDesignTabViewModel tab, double toleranceMm, GuideOrientation orientation, DesignerElement dragging,
        double leading, double center, double trailing, out bool applied)
    {
        applied = TrySnapAxisImpl(tab, toleranceMm, orientation, dragging, leading, center, trailing, out var delta);
        return delta;
    }

    /// <summary>Same rotated-AABB math as CardDesignTabViewModel.GetVisualBounds, kept
    /// here too since the canvas doesn't reference the ViewModels namespace's private
    /// helper. Duplicated deliberately rather than making the ViewModel method public
    /// just for this -- it's a small, self-contained formula.</summary>
    private static Rect GetVisualBoundsStatic(DesignerElement el)
    {
        if (el.Rotation == 0) return new Rect(el.X, el.Y, el.Width, el.Height);
        var cx = el.X + el.Width / 2;
        var cy = el.Y + el.Height / 2;
        var rad = el.Rotation * Math.PI / 180.0;
        var cos = Math.Abs(Math.Cos(rad));
        var sin = Math.Abs(Math.Sin(rad));
        var rotatedW = el.Width * cos + el.Height * sin;
        var rotatedH = el.Width * sin + el.Height * cos;
        return new Rect(cx - rotatedW / 2, cy - rotatedH / 2, rotatedW, rotatedH);
    }

    /// <summary>Control-local screen rect for an element, for hosts (CardDesignerView)
    /// that need to position an overlay control -- e.g. the inline text-edit TextBox
    /// (Part 3) -- exactly on top of it. Returns null if the element's side isn't
    /// currently visible (e.g. editing was requested on the back while viewing "Front
    /// only"). Deliberately reuses the same ComputeLayout/ToDeviceRect math the renderer
    /// itself uses so the overlay can never drift out of sync with the drawn element.
    /// Ignores rotation -- a rotated text element's overlay will not itself be rotated,
    /// a known limitation of this first version of inline editing.</summary>
    public Rect? GetElementScreenRect(DesignerElement element)
    {
        if (Tab is not { } tab) return null;
        var side = tab.Document.Front.Elements.Contains(element) ? CardSide.Front
                 : tab.Document.Back.Elements.Contains(element) ? CardSide.Back
                 : (CardSide?)null;
        if (side is null) return null;

        var (scale, sideRects) = ComputeLayout(tab);
        if (!sideRects.TryGetValue(side.Value, out var cardRect)) return null;
        return ToDeviceRect(cardRect, scale, element);
    }

    /// <summary>Converts a point in this control's own coordinates into mm on the
    /// currently focused side, or null if that side isn't laid out (e.g. document not
    /// loaded). Used by CardDesignerView.axaml.cs to feed the ruler cursor-position
    /// markers (Part 22) from the canvas's own pointer-move, since the ruler strips
    /// themselves rarely have the mouse directly over them while designing.</summary>
    public Point? ScreenPointToFocusedMm(Point canvasLocalPoint)
    {
        if (Tab is not { } tab) return null;
        var (scale, sideRects) = ComputeLayout(tab);
        if (!sideRects.TryGetValue(tab.FocusedSide, out var cardRect)) return null;
        return ToMm(cardRect, scale, canvasLocalPoint);
    }

    private static Point ToMm(Rect cardRect, double scale, Point devicePoint) =>
        new((devicePoint.X - cardRect.X) / scale, (devicePoint.Y - cardRect.Y) / scale);

    private static IBrush ParseBrush(string hex)
    {
        try { return new SolidColorBrush(Color.Parse(hex)); }
        catch { return Brushes.Black; }
    }

    private static Color ParseColor(string hex)
    {
        try { return Color.Parse(hex); }
        catch { return Colors.Gray; }
    }

    // -------------------------------------------------------------- interaction ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var tab = Tab;
        if (tab is null) return;

        var point = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsMiddleButtonPressed || _spacePressed || tab.PanToolActive)
        {
            _dragMode = DragMode.Pan;
            _dragStartPointerPos = point;
            _panStart = new Point(tab.PanX, tab.PanY);
            e.Pointer.Capture(this);
            return;
        }

        var (scale, sideRects) = ComputeLayout(tab);
        var hit = sideRects.FirstOrDefault(kv => kv.Value.Contains(point));
        if (hit.Value == default)
        {
            tab.ClearSelection();
            InvalidateVisual();
            return;
        }

        var side = hit.Key;
        var cardRect = hit.Value;
        tab.FocusedSide = side;

        if (tab.SelectedElements.Count == 1 && tab.Document.GetSide(side).Elements.Contains(tab.SelectedElements[0]))
        {
            var selected = tab.SelectedElements[0];
            var elementRect = ToDeviceRect(cardRect, scale, selected);
            var handle = HitTestHandle(elementRect, point);
            if (handle != DragMode.None)
            {
                _dragMode = handle;
                _dragElement = selected;
                _dragStartPointerPos = point;
                _dragStartRotation = selected.Rotation;
                _dragStartRects.Clear();
                _dragStartRects[selected] = (selected.X, selected.Y, selected.Width, selected.Height);
                e.Pointer.Capture(this);
                return;
            }
        }

        var mmPoint = ToMm(cardRect, scale, point);

        // Double-click directly on a guide line (and not on an element sitting on top
        // of it) removes that guide -- the delete affordance for guides (Part 21/23).
        if (e.ClickCount == 2 && side == tab.FocusedSide)
        {
            var toleranceMm = 3.0 / scale;
            var beforeV = tab.VerticalGuidesMm.Count;
            var beforeH = tab.HorizontalGuidesMm.Count;
            tab.RemoveVerticalGuideNear(mmPoint.X, toleranceMm);
            tab.RemoveHorizontalGuideNear(mmPoint.Y, toleranceMm);
            if (tab.VerticalGuidesMm.Count != beforeV || tab.HorizontalGuidesMm.Count != beforeH)
            {
                InvalidateVisual();
                return;
            }
        }

        var elements = tab.Document.GetSide(side).Elements;
        var hitElement = elements
            .Where(el => el.Visible && !el.Locked)
            .OrderByDescending(el => el.ZIndex)
            .FirstOrDefault(el => new Rect(el.X, el.Y, el.Width, el.Height).Contains(mmPoint));

        // Double-click a text element -> hand off to the inline editor instead of
        // starting a drag (Part 3 / 80). Single-click still just selects, matching the
        // "single click = select, double click = edit" behaviour requested.
        if (e.ClickCount == 2 && hitElement is TextElement textElement && !textElement.Locked)
        {
            tab.SelectOnly(textElement);
            TextEditRequested?.Invoke(this, textElement);
            InvalidateVisual();
            return;
        }

        var additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (hitElement is not null)
        {
            if (additive)
            {
                tab.ToggleSelection(hitElement);
            }
            else if (!tab.SelectedElements.Contains(hitElement))
            {
                tab.SelectOnly(hitElement);
            }

            if (!hitElement.Locked && tab.SelectedElements.Contains(hitElement))
            {
                _dragMode = DragMode.Move;
                _dragElement = hitElement;
                _dragStartPointerPos = point;
                _dragStartRects.Clear();
                foreach (var sel in tab.SelectedElements)
                {
                    _dragStartRects[sel] = (sel.X, sel.Y, sel.Width, sel.Height);
                }
                e.Pointer.Capture(this);
            }
        }
        else
        {
            if (!additive) tab.ClearSelection();
            _dragMode = DragMode.MarqueeSelect;
            _marqueeStart = point;
            _marqueeRect = new Rect(point, new Size(0, 0));
            e.Pointer.Capture(this);
        }

        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var tab = Tab;
        if (tab is null || _dragMode == DragMode.None) return;

        var point = e.GetPosition(this);

        if (_dragMode == DragMode.Pan)
        {
            var delta = point - _dragStartPointerPos!.Value;
            tab.PanX = _panStart.X + delta.X;
            tab.PanY = _panStart.Y + delta.Y;
            InvalidateVisual();
            return;
        }

        var (scale, sideRects) = ComputeLayout(tab);
        if (!sideRects.TryGetValue(tab.FocusedSide, out var cardRect)) return;

        if (_dragMode == DragMode.MarqueeSelect)
        {
            var x = Math.Min(_marqueeStart.X, point.X);
            var y = Math.Min(_marqueeStart.Y, point.Y);
            var w = Math.Abs(point.X - _marqueeStart.X);
            var h = Math.Abs(point.Y - _marqueeStart.Y);
            _marqueeRect = new Rect(x, y, w, h);
            InvalidateVisual();
            return;
        }

        if (_dragMode == DragMode.Rotate && _dragElement is not null)
        {
            var elementRect = ToDeviceRect(cardRect, scale, _dragElement);
            var center = elementRect.Center;
            var angleRad = Math.Atan2(point.Y - center.Y, point.X - center.X);
            var angleDeg = angleRad * 180.0 / Math.PI + 90.0; // handle points "up" at rotation 0

            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                angleDeg = Math.Round(angleDeg / 15.0) * 15.0; // snap to 15-degree steps
            }

            _dragElement.Rotation = angleDeg;
            InvalidateVisual();
            return;
        }

        if (_dragMode == DragMode.Move && _dragStartRects.Count > 0)
        {
            var deltaMm = new Vector(
                (point.X - _dragStartPointerPos!.Value.X) / scale,
                (point.Y - _dragStartPointerPos.Value.Y) / scale);

            _activeSnapLines.Clear();
            var toleranceMm = SnapToleranceDevicePx / scale;

            // Snap candidates, in priority order: guides, card center, other (unselected)
            // elements' edges/centers, then plain grid (Part 20/21). Only the primary
            // drag element is used to decide the snap so a multi-selection doesn't jitter
            // between conflicting snaps for different members.
            var primary = _dragElement ?? _dragStartRects.Keys.First();
            var primaryStart = _dragStartRects[primary];
            var candidateX = primaryStart.X + deltaMm.X;
            var candidateY = primaryStart.Y + deltaMm.Y;
            var candidateCenterX = candidateX + primaryStart.W / 2;
            var candidateCenterY = candidateY + primaryStart.H / 2;
            var candidateRight = candidateX + primaryStart.W;
            var candidateBottom = candidateY + primaryStart.H;

            var snappedDeltaX = TrySnapAxis(
                tab, toleranceMm, GuideOrientation.Vertical, primary,
                candidateX, candidateCenterX, candidateRight, out var appliedX);
            var snappedDeltaY = TrySnapAxis(
                tab, toleranceMm, GuideOrientation.Horizontal, primary,
                candidateY, candidateCenterY, candidateBottom, out var appliedY);

            var newDeltaX = appliedX ? deltaMm.X + snappedDeltaX : deltaMm.X;
            var newDeltaY = appliedY ? deltaMm.Y + snappedDeltaY : deltaMm.Y;
            deltaMm = new Vector(newDeltaX, newDeltaY);

            if (!appliedX && !appliedY && tab.Document.SnapToGrid && tab.Document.GridSizeMm > 0)
            {
                var g = tab.Document.GridSizeMm;
                deltaMm = new Vector(
                    Math.Round((primaryStart.X + deltaMm.X) / g) * g - primaryStart.X,
                    Math.Round((primaryStart.Y + deltaMm.Y) / g) * g - primaryStart.Y);
            }

            foreach (var (element, start) in _dragStartRects)
            {
                element.X = start.X + deltaMm.X;
                element.Y = start.Y + deltaMm.Y;
            }

            InvalidateVisual();
            return;
        }

        if (IsResizeMode(_dragMode) && _dragElement is not null && _dragStartRects.TryGetValue(_dragElement, out var startRect))
        {
            var deltaMm = new Vector(
                (point.X - _dragStartPointerPos!.Value.X) / scale,
                (point.Y - _dragStartPointerPos.Value.Y) / scale);

            ApplyResize(_dragElement, startRect, deltaMm, _dragMode);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);

        var tab = Tab;
        if (tab is null)
        {
            _dragMode = DragMode.None;
            return;
        }

        if (_dragMode == DragMode.MarqueeSelect && _marqueeRect is { } marquee)
        {
            var (scale, sideRects) = ComputeLayout(tab);
            if (sideRects.TryGetValue(tab.FocusedSide, out var cardRect))
            {
                foreach (var el in tab.Document.GetSide(tab.FocusedSide).Elements)
                {
                    var rect = ToDeviceRect(cardRect, scale, el);
                    if (marquee.Intersects(rect))
                    {
                        tab.ToggleSelection(el);
                    }
                }
            }
            _marqueeRect = null;
        }
        else if (_dragMode == DragMode.Move && _dragStartRects.Count > 0)
        {
            var commands = new List<IDesignCommand>();
            foreach (var (element, start) in _dragStartRects)
            {
                var after = (element.X, element.Y, element.Width, element.Height);
                if (start.X != after.X || start.Y != after.Y)
                {
                    commands.Add(new TransformElementCommand(element, start, after));
                }
            }

            if (commands.Count > 0)
            {
                var composite = commands.Count == 1 ? commands[0] : new CompositeCommand("Move", commands);
                tab.History.Record(composite);
                tab.MarkDirty();
                tab.RefreshHistoryFlags();
            }
        }
        else if (IsResizeMode(_dragMode) && _dragElement is not null && _dragStartRects.TryGetValue(_dragElement, out var start))
        {
            var after = (_dragElement.X, _dragElement.Y, _dragElement.Width, _dragElement.Height);
            if (start.X != after.Item1 || start.Y != after.Item2 || start.W != after.Item3 || start.H != after.Item4)
            {
                tab.History.Record(new TransformElementCommand(_dragElement, start, after));
                tab.MarkDirty();
                tab.RefreshHistoryFlags();
            }
        }
        else if (_dragMode == DragMode.Rotate && _dragElement is not null && _dragElement.Rotation != _dragStartRotation)
        {
            tab.History.Record(new RotateElementCommand(_dragElement, _dragStartRotation, _dragElement.Rotation));
            tab.MarkDirty();
            tab.RefreshHistoryFlags();
        }

        _dragMode = DragMode.None;
        _dragElement = null;
        _dragStartRects.Clear();
        _activeSnapLines.Clear();
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var tab = Tab;
        if (tab is null) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Cursor-anchored zoom: without this, the whole card visibly slides out from
            // under the mouse on every wheel tick because zoom is applied around the
            // canvas center (see ComputeLayout's originX/originY). Fix is to compute
            // which document-space point sits under the cursor BEFORE changing zoom,
            // then adjust PanX/PanY AFTER so that same point still sits under the
            // cursor -- the standard "zoom to point" technique.
            var cursor = e.GetPosition(this);
            var oldZoom = tab.Zoom;
            var newZoom = Math.Clamp(oldZoom * (e.Delta.Y > 0 ? 1.1 : 0.9), 0.05, 16.0);
            newZoom = Math.Round(newZoom, 3);
            if (Math.Abs(newZoom - oldZoom) > 0.0001)
            {
                var (oldScale, oldRects) = ComputeLayout(tab);
                // Anchor to whichever card side's rect the cursor is closest to (Both view).
                var anchorSide = oldRects.OrderBy(kv => DistanceToRect(cursor, kv.Value)).First().Key;
                var cardRect = oldRects[anchorSide];
                var anchorMm = ToMm(cardRect, oldScale, cursor);

                tab.Zoom = newZoom;
                var (newScale, newRects) = ComputeLayout(tab);
                var newCardRect = newRects.TryGetValue(anchorSide, out var r) ? r : newRects.Values.First();

                // Where the anchor point *would* land on screen post-zoom if pan didn't change:
                var projected = new Point(newCardRect.X + anchorMm.X * newScale, newCardRect.Y + anchorMm.Y * newScale);
                var correction = cursor - projected;
                tab.PanX += correction.X;
                tab.PanY += correction.Y;
            }
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            tab.PanX += e.Delta.Y * 40;
        }
        else
        {
            tab.PanY += e.Delta.Y * 40;
        }

        e.Handled = true;
        InvalidateVisual();
    }

    private static double DistanceToRect(Point p, Rect r)
    {
        var dx = Math.Max(Math.Max(r.X - p.X, 0), p.X - r.Right);
        var dy = Math.Max(Math.Max(r.Y - p.Y, 0), p.Y - r.Bottom);
        return dx * dx + dy * dy;
    }

    /// <summary>Ctrl+0 -- sets zoom so the whole card (or both sides, in Front+Back
    /// view) fits in the visible canvas area with a small margin, and re-centers pan.
    /// There was previously no "fit" concept at all, only manual +/- and a fixed 100%
    /// reset (Part 22/76).</summary>
    private void FitToScreen(CardDesignTabViewModel tab)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var doc = tab.Document;
        var sidesShown = tab.ActiveSideView == DesignerSideView.Both ? 2 : 1;
        var contentWmm = doc.WidthMm * sidesShown + (sidesShown == 2 ? GapBetweenSidesMm : 0);
        var contentHmm = doc.HeightMm;

        const double marginPx = 48;
        var availW = Math.Max(Bounds.Width - marginPx, 10);
        var availH = Math.Max(Bounds.Height - marginPx, 10);

        var fitZoom = Math.Min(availW / (contentWmm * PixelsPerMm), availH / (contentHmm * PixelsPerMm));
        tab.Zoom = Math.Round(Math.Clamp(fitZoom, 0.05, 16.0), 3);
        tab.PanX = 0;
        tab.PanY = 0;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var tab = Tab;

        if (e.Key == Key.Space)
        {
            _spacePressed = true;
            e.Handled = true;
            return;
        }

        if (tab is null) return;

        if (e.Key is Key.Delete or Key.Back)
        {
            tab.DeleteSelectedCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.Z: tab.UndoCommand.Execute(null); e.Handled = true; return;
                case Key.Y: tab.RedoCommand.Execute(null); e.Handled = true; return;
                case Key.C: tab.CopyCommand.Execute(null); e.Handled = true; return;
                case Key.X: tab.CutCommand.Execute(null); e.Handled = true; return;
                case Key.V: tab.PasteCommand.Execute(null); e.Handled = true; return;
                case Key.D: tab.DuplicateSelectedCommand.Execute(null); e.Handled = true; return;
                case Key.S: tab.SaveCommand.Execute(null); e.Handled = true; return;
                case Key.G when e.KeyModifiers.HasFlag(KeyModifiers.Shift): tab.UngroupCommand.Execute(null); e.Handled = true; return;
                case Key.G: tab.GroupCommand.Execute(null); e.Handled = true; return;
                // Ctrl+0 = fit whole card in view, Ctrl+1 = 100% -- Part 22/76 keyboard
                // shortcuts that were missing entirely (only the toolbar +/- and the "1:1"
                // button existed before).
                case Key.D0 or Key.NumPad0: FitToScreen(tab); e.Handled = true; return;
                case Key.D1 or Key.NumPad1: tab.Zoom = 1.0; tab.PanX = 0; tab.PanY = 0; e.Handled = true; return;
                case Key.OemPlus or Key.Add: tab.ZoomInCommand.Execute(null); e.Handled = true; return;
                case Key.OemMinus or Key.Subtract: tab.ZoomOutCommand.Execute(null); e.Handled = true; return;
                case Key.A:
                    tab.SelectedElements.Clear();
                    foreach (var el in tab.FocusedSideModel.Elements) tab.SelectedElements.Add(el);
                    tab.NotifySelectionChanged();
                    InvalidateVisual();
                    e.Handled = true;
                    return;
            }
        }

        var nudge = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5.0 : e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 0.5 : 1.0;
        Vector? delta = e.Key switch
        {
            Key.Left => new Vector(-nudge, 0),
            Key.Right => new Vector(nudge, 0),
            Key.Up => new Vector(0, -nudge),
            Key.Down => new Vector(0, nudge),
            _ => null,
        };

        if (delta is { } d && tab.SelectedElements.Count > 0)
        {
            foreach (var el in tab.SelectedElements)
            {
                el.X += d.X;
                el.Y += d.Y;
            }
            tab.MarkDirty();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space) _spacePressed = false;
    }

    private static bool IsResizeMode(DragMode mode) =>
        mode is DragMode.ResizeTopLeft or DragMode.ResizeTopRight or DragMode.ResizeBottomLeft or DragMode.ResizeBottomRight;

    private static DragMode HitTestHandle(Rect elementRect, Point point)
    {
        const double h = 9;
        var rotationHandleCenter = new Point(elementRect.Center.X, elementRect.Top - 24);
        if (new Rect(rotationHandleCenter.X - 9, rotationHandleCenter.Y - 9, 18, 18).Contains(point)) return DragMode.Rotate;

        if (new Rect(elementRect.TopLeft.X - h / 2, elementRect.TopLeft.Y - h / 2, h, h).Contains(point)) return DragMode.ResizeTopLeft;
        if (new Rect(elementRect.TopRight.X - h / 2, elementRect.TopRight.Y - h / 2, h, h).Contains(point)) return DragMode.ResizeTopRight;
        if (new Rect(elementRect.BottomLeft.X - h / 2, elementRect.BottomLeft.Y - h / 2, h, h).Contains(point)) return DragMode.ResizeBottomLeft;
        if (new Rect(elementRect.BottomRight.X - h / 2, elementRect.BottomRight.Y - h / 2, h, h).Contains(point)) return DragMode.ResizeBottomRight;
        return DragMode.None;
    }

    private static void ApplyResize(DesignerElement el, (double X, double Y, double W, double H) start, Vector deltaMm, DragMode mode)
    {
        const double minSize = 2;
        switch (mode)
        {
            case DragMode.ResizeBottomRight:
                el.Width = Math.Max(minSize, start.W + deltaMm.X);
                el.Height = Math.Max(minSize, start.H + deltaMm.Y);
                break;
            case DragMode.ResizeBottomLeft:
                el.Width = Math.Max(minSize, start.W - deltaMm.X);
                el.Height = Math.Max(minSize, start.H + deltaMm.Y);
                el.X = start.X + (start.W - el.Width);
                break;
            case DragMode.ResizeTopRight:
                el.Width = Math.Max(minSize, start.W + deltaMm.X);
                el.Height = Math.Max(minSize, start.H - deltaMm.Y);
                el.Y = start.Y + (start.H - el.Height);
                break;
            case DragMode.ResizeTopLeft:
                el.Width = Math.Max(minSize, start.W - deltaMm.X);
                el.Height = Math.Max(minSize, start.H - deltaMm.Y);
                el.X = start.X + (start.W - el.Width);
                el.Y = start.Y + (start.H - el.Height);
                break;
        }
    }
}