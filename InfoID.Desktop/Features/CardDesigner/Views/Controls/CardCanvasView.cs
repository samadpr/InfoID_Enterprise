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
using InfoID.Desktop.Features.CardDesigner.Services;
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
    /// (see CardDesignerView.axaml's Grid).
    ///
    /// This overload always returns the FRONT card's origin (or the single visible
    /// card's origin in Front-only/Back-only view) -- used for drawing the ruler's own
    /// continuous tick-mark strip, which intentionally spans the whole visible
    /// workspace in one coordinate line rather than resetting at each card. For a
    /// screen position that corresponds to an mm value already expressed relative to a
    /// SPECIFIC side's own local origin (e.g. ScreenPointToFocusedMm's result, which is
    /// always relative to tab.FocusedSide -- see that method's own doc comment), use
    /// the ComputeOriginX(tab, controlWidth, side) overload below instead. Priority 6
    /// bug: RulerView's cursor-position marker and drag-to-create-guide code used to
    /// call this side-blind overload even when the focused side was Back, silently
    /// reusing the Front card's origin for an mm value that was actually relative to
    /// Back's -- the marker (and any guide dragged out while Back was focused) ended up
    /// offset by exactly one card-width-plus-gap from where the mouse actually was.</summary>
    internal static double ComputeOriginX(CardDesignTabViewModel tab, double controlWidth)
    {
        var scale = PixelsPerMm * tab.Zoom;
        var cardWpx = tab.Document.WidthMm * scale;
        var gapPx = GapBetweenSidesMm * scale;
        var totalWidth = tab.ActiveSideView == DesignerSideView.Both ? cardWpx * 2 + gapPx : cardWpx;
        return (controlWidth - totalWidth) / 2 + tab.PanX;
    }

    /// <summary>The X origin of <paramref name="side"/>'s own card specifically --
    /// identical to the side-blind overload above except in "Both" view when
    /// <paramref name="side"/> is Back, where it adds the front card's width plus the
    /// gap between them (the same offset CardCanvasView.ComputeLayout already applies
    /// when placing Back's Rect). See that overload's doc comment for the bug this
    /// fixes and why two overloads exist rather than one.</summary>
    internal static double ComputeOriginX(CardDesignTabViewModel tab, double controlWidth, CardSide side)
    {
        var baseOrigin = ComputeOriginX(tab, controlWidth);
        if (tab.ActiveSideView != DesignerSideView.Both || side != CardSide.Back) return baseOrigin;

        var scale = PixelsPerMm * tab.Zoom;
        var cardWpx = tab.Document.WidthMm * scale;
        var gapPx = GapBetweenSidesMm * scale;
        return baseOrigin + cardWpx + gapPx;
    }

    /// <summary>Y origin is identical for Front and Back in the current side-by-side
    /// (never stacked) layout -- see CardCanvasView.ComputeLayout, where both sides'
    /// Rects share the same originY. Kept as a single side-blind method (no overload
    /// needed the way ComputeOriginX has one) for that reason; if a stacked-vertically
    /// layout mode is ever added, this would need the same side-aware treatment.</summary>
    internal static double ComputeOriginY(CardDesignTabViewModel tab, double controlHeight)
    {
        var scale = PixelsPerMm * tab.Zoom;
        var cardHpx = tab.Document.HeightMm * scale;
        return (controlHeight - cardHpx) / 2 + tab.PanY;
    }

    private static readonly IBrush WorkspaceBackgroundBrushLight = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush WorkspaceBackgroundBrushDark = new SolidColorBrush(Color.Parse("#1E1E1E"));

    /// <summary>The pasteboard area behind the card itself. Previously a hardcoded
    /// dark color regardless of theme, which read as a rendering bug in light mode (a
    /// stark black rectangle behind an otherwise all-white/light UI).
    ///
    /// Reads Application.Current.ActualThemeVariant directly -- the same value
    /// ThemeService.ApplyTheme sets via Application.Current.RequestedThemeVariant --
    /// rather than a "BrushCanvasWorkspace" XAML resource looked up via
    /// this.TryFindResource(...): that route was tried first and didn't actually
    /// track the live theme for this control (canvas stayed on its dark fallback
    /// value in light mode), so this reads the one place the app's theme state is
    /// unambiguous instead of depending on this custom-drawn Control's resource-host
    /// chain resolving a themed dictionary entry correctly.</summary>
    private static IBrush WorkspaceBackgroundBrush =>
        Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
            ? WorkspaceBackgroundBrushDark
            : WorkspaceBackgroundBrushLight;

    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.Parse("#3B82F6"));
    private static readonly IBrush MarqueeFillBrush = new SolidColorBrush(Color.Parse("#333B82F6"));
    private static readonly IBrush GuideBrush = new SolidColorBrush(Color.Parse("#00B4D8"));
    private static readonly IBrush SmartGuideBrush = new SolidColorBrush(Color.Parse("#FF7A00"));
    private static readonly IBrush PlaceholderFillBrush = new SolidColorBrush(Color.Parse("#22FFFFFF"));
    private static readonly IBrush PlaceholderBorderBrush = Brushes.Gray;
    private static readonly IBrush CardBorderBrush = new SolidColorBrush(Color.Parse("#3A3A3A"));
    private static readonly IBrush GroupSelectionBrush = new SolidColorBrush(Color.Parse("#8B5CF6"));

    /// <summary>Layered semi-transparent rounded rects standing in for a real blurred
    /// drop shadow (Part 29's "professional canvas" ask). DrawingContext here has no
    /// gaussian-blur primitive to reach for, so this is the standard cheap
    /// approximation: several offset copies of the card's silhouette, each larger and
    /// fainter than the last, which reads as a soft shadow at normal viewing distance
    /// without needing a compositing effect this control doesn't have access to.</summary>
    private static readonly (double SpreadPx, byte Alpha)[] ShadowLayers =
    {
        (1, 40), (3, 28), (6, 18), (10, 10), (15, 5),
    };

    private enum DragMode { None, Move, ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight, Rotate, MarqueeSelect, Pan, DrawLine, DrawPen, LineEndpoint, PenNode }

    private DragMode _dragMode = DragMode.None;
    private DesignerElement? _dragElement;
    private Point? _dragStartPointerPos;
    private Point _panStart;
    private Point _marqueeStart;
    private Rect? _marqueeRect;
    private bool _spacePressed;

    // Line/Pen tool live preview (Part 96): device-pixel points captured while the drag is
    // in progress, drawn as a temporary overlay in Render() the same way _marqueeRect
    // already is, and converted to mm + committed as a real element only on
    // OnPointerReleased -- never mutated into the document mid-drag.
    private Point? _lineStartPoint;
    private Point? _lineEndPoint;
    private readonly List<Point> _penPoints = new();

    /// <summary>Minimum real-world distance between two captured Pen-tool points -- see
    /// OnPointerMoved's DragMode.DrawPen branch for why this is mm, not device px. Large
    /// enough that node-edit handles (12px hit-boxes, HitTestPenNode) stay usefully
    /// separated at typical editing zoom levels instead of forming one near-continuous
    /// band that leaves no gap to click the stroke's body (rather than a node) to move
    /// it as a whole.</summary>
    private const double MinPenPointSpacingMm = 3.5;
    private double _dragStartRotation;
    private readonly Dictionary<DesignerElement, (double X, double Y, double W, double H)> _dragStartRects = new();

    // Node editing (a selected Line/Arrow or Pen stroke shows its actual endpoints/points
    // as draggable handles instead of a generic bounding-box frame -- see
    // DrawSelectionOverlay/HitTestLineNode/HitTestPenNode). _dragStartCenterMm is the
    // element's own center at the moment the drag started, used as a stable rotation
    // pivot for the whole drag (matching how ApplyResize's corner-drag math rotates the
    // pointer delta by the element's OWN Rotation rather than tracking a moving pivot).
    private int _dragNodeIndex = -1;
    private bool _dragStartLineFlipped;
    private List<PenPoint>? _dragStartPenPoints;
    private Point _dragStartCenterMm;
    private Point _dragOtherEndpointMm;

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

        // WorkspaceBackgroundBrush reads the live theme fresh on every Render() call,
        // but Render() only runs when something invalidates this control -- toggling
        // the app's theme doesn't touch the Document/selection/pan/zoom state this
        // control already listens to, so without this the canvas would keep showing
        // its old-theme color until some unrelated redraw happened to occur.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
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

        UpdateCursor();
        InvalidateVisual();
    }

    private void OnTabChanged(object? sender, EventArgs e) => InvalidateVisual();
    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();
        UpdateCursor();
    }

    private void OnSelectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    /// <summary>Reflects the active tool in the mouse cursor -- Pan shows an open hand,
    /// Line/Pen show a crosshair (the standard "you're about to draw" affordance in every
    /// drawing tool), Select falls back to the platform default arrow. Runs on every
    /// tab PropertyChanged rather than only the three tool-mode properties specifically:
    /// cheap to recompute, and avoids subscribing/unsubscribing three more explicit event
    /// handlers for what OnTabPropertyChanged already receives.</summary>
    private void UpdateCursor()
    {
        Cursor = Tab switch
        {
            { PanToolActive: true } => new Cursor(StandardCursorType.Hand),
            { LineToolActive: true } => new Cursor(StandardCursorType.Cross),
            { PenToolActive: true } => new Cursor(StandardCursorType.Cross),
            _ => Cursor.Default,
        };
    }

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

        if (_dragMode == DragMode.DrawLine && _lineStartPoint is { } lineStart && _lineEndPoint is { } lineEnd)
        {
            context.DrawLine(new Pen(SelectionBrush, 1.5), lineStart, lineEnd);
        }

        if (_dragMode == DragMode.DrawPen && _penPoints.Count >= 2)
        {
            var previewGeometry = new StreamGeometry();
            using (var gc = previewGeometry.Open())
            {
                gc.BeginFigure(_penPoints[0], isFilled: false);
                for (var i = 1; i < _penPoints.Count; i++)
                {
                    gc.LineTo(_penPoints[i]);
                }
                gc.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(SelectionBrush, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), previewGeometry);
        }
    }

    private void DrawSide(DrawingContext context, CardDesignTabViewModel tab, CardSide side, Rect cardRect, double scale)
    {
        var doc = tab.Document;
        var radius = doc.CornerRadiusMm * scale;
        var roundedRect = new RoundedRect(cardRect, radius);

        DrawCardShadow(context, cardRect, radius);

        var sideModel = doc.GetSide(side);
        var background = sideModel.Background;

        if (background.Kind == BackgroundKind.Image && !string.IsNullOrEmpty(background.AssetReference))
        {
            // Border/shape stroke first (no fill -- the image itself fills the card),
            // then the image is drawn clipped to that same rounded shape, cover-fit
            // (ImageFitMode.Fill: scales to cover the whole card, cropping overflow,
            // never stretching/distorting -- "canvas size correctly fit it properly")
            // exactly like an Image element's own Fill fit mode already works.
            context.DrawRectangle(null, new Pen(CardBorderBrush, 1), roundedRect);
            using (context.PushClip(roundedRect))
            {
                DrawImage(context, cardRect, background.AssetReference, ImageFitMode.Fill, cornerRadius: 0);
            }
        }
        else
        {
            context.DrawRectangle(ResolveBackgroundBrush(background), new Pen(CardBorderBrush, 1), roundedRect);
        }

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
        if (side == tab.FocusedSide && tab.ShowGuides)
        {
            DrawGuides(context, cardRect, scale, tab);
        }
    }

    /// <summary>See ShadowLayers' own doc comment for why this is layered flat rects
    /// rather than a real blur. Drawn before the card fill/border so it sits entirely
    /// behind the card, offset down-and-right like a light source from the upper left --
    /// the conventional direction for UI drop shadows.</summary>
    private static void DrawCardShadow(DrawingContext context, Rect cardRect, double radius)
    {
        const double offsetX = 2, offsetY = 4;
        foreach (var (spread, alpha) in ShadowLayers)
        {
            var shadowRect = new Rect(
                cardRect.X - spread + offsetX, cardRect.Y - spread + offsetY,
                cardRect.Width + spread * 2, cardRect.Height + spread * 2);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
            context.DrawRectangle(brush, null, new RoundedRect(shadowRect, radius + spread * 0.5));
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

        using var transformScope = context.PushTransform(GetRotationTransform(elementRect, element.Rotation));

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
                DrawImage(context, elementRect, image.AssetReference, image.FitMode, image.CornerRadius, image.CropX, image.CropY, image.CropZoom,
                    image.MaskShape, image.FlipHorizontal, image.FlipVertical);
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
            case QrCodeElement qr:
                DrawQrCode(context, elementRect, qr);
                break;
            case PenElement pen:
                PenRenderer.Draw(context, elementRect, pen);
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
            // Priority 4 fix: text.FontSize was previously passed straight into
            // FormattedText with no relationship to the current zoom level at all,
            // while the element's own box (rect, via ToDeviceRect) IS scaled by
            // tab.Zoom -- position/size scaled with zoom, font size didn't. At low
            // zoom the box shrinks a lot but the glyphs stayed exactly the same
            // absolute device-pixel size, so most (eventually all) of each letter's
            // ink fell outside the card's clip region (see DrawSide's
            // context.PushClip(cardRect)) and the text effectively vanished; at high
            // zoom the reverse made text look disproportionately small relative to its
            // now-much-bigger box. Multiplying by tab.Zoom here makes text scale
            // together with its box exactly like every other element already does, and
            // is a no-op at the default Zoom=1.0 (100%), so on-screen appearance at the
            // zoom level everything was presumably designed/tested at is unchanged.
            Math.Max(1, text.FontSize * tab.Zoom),
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

        var formatted = new FormattedText(displayText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface,
            // Priority 4 fix: same reasoning as DrawText above -- field.FontSize must
            // scale with tab.Zoom or a data-field's text vanishes at low zoom exactly
            // like a plain TextElement's did.
            Math.Max(1, field.FontSize * tab.Zoom), brush)
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

    /// <summary>Used by Image elements (and the Background image path in DrawSide,
    /// which uses the defaults: Rectangle mask, no flip). Fit/Fill/Stretch match the
    /// semantics of CSS object-fit: Fit letterboxes to show the whole picture, Fill
    /// crops to cover the whole box, Stretch ignores aspect ratio entirely. maskShape
    /// mirrors DrawPhoto's own clip-geometry switch (same three shapes, same meaning);
    /// flipHorizontal/flipVertical mirror the source rect around its own center before
    /// drawing -- a transform, not a pixel operation, so it's free to redraw every
    /// frame and needs no caching.</summary>
    private void DrawImage(DrawingContext context, Rect rect, string? assetReference, ImageFitMode fitMode, double cornerRadius,
        double cropX = 0, double cropY = 0, double cropZoom = 1.0,
        PhotoMaskShape maskShape = PhotoMaskShape.Rectangle, bool flipHorizontal = false, bool flipVertical = false)
    {
        using var clip = ShapeRenderer.BuildMaskClipGeometry(rect, maskShape) is { } maskGeometry
            ? context.PushGeometryClip(maskGeometry)
            : maskShape == PhotoMaskShape.RoundedRectangle
                ? context.PushClip(new RoundedRect(rect, Math.Min(cornerRadius, Math.Min(rect.Width, rect.Height) / 2)))
                : cornerRadius > 0
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
                // Same pan+zoom formula as DrawPhoto below -- CropX/Y/Zoom mean the same
                // thing on both element types, so they render the same way.
                var fillScale = Math.Max(rect.Width / sourceSize.Width, rect.Height / sourceSize.Height) * Math.Max(cropZoom, 0.1);
                var visibleW = rect.Width / fillScale;
                var visibleH = rect.Height / fillScale;
                var maxOffsetX = Math.Max(0, sourceSize.Width - visibleW);
                var maxOffsetY = Math.Max(0, sourceSize.Height - visibleH);
                var offsetX = Math.Clamp((sourceSize.Width - visibleW) / 2 + cropX, 0, maxOffsetX);
                var offsetY = Math.Clamp((sourceSize.Height - visibleH) / 2 + cropY, 0, maxOffsetY);
                sourceRect = new Rect(offsetX, offsetY, Math.Min(visibleW, sourceSize.Width), Math.Min(visibleH, sourceSize.Height));
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

        if (flipHorizontal || flipVertical)
        {
            var center = rect.Center;
            var flipTransform = Matrix.CreateTranslation(-center.X, -center.Y)
                                 * new Matrix(flipHorizontal ? -1 : 1, 0, 0, flipVertical ? -1 : 1, 0, 0)
                                 * Matrix.CreateTranslation(center.X, center.Y);
            using var flipScope = context.PushTransform(flipTransform);
            context.DrawImage(bitmap, sourceRect, destRect);
        }
        else
        {
            context.DrawImage(bitmap, sourceRect, destRect);
        }
    }

    private void DrawPhoto(DrawingContext context, Rect rect, PhotoElement photo)
    {
        using var clip = ShapeRenderer.BuildMaskClipGeometry(rect, photo.MaskShape) is { } maskGeometry
            ? context.PushGeometryClip(maskGeometry)
            : photo.MaskShape == PhotoMaskShape.RoundedRectangle
                ? context.PushClip(new RoundedRect(rect, Math.Min(photo.CornerRadius, Math.Min(rect.Width, rect.Height) / 2)))
                : context.PushClip(rect);

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

    /// <summary>Delegates to the shared ShapeRenderer (Services/ShapeRenderer.cs) --
    /// see its own doc comment for why this used to be a private copy of the same code
    /// duplicated with ThumbnailRenderer, and the bug that caused.</summary>
    private static void DrawShape(DrawingContext context, Rect rect, ShapeElement shape) =>
        ShapeRenderer.Draw(context, rect, shape);

    /// <summary>Generic fallback for element types this pass doesn't have a specialized
    /// renderer for yet (Photo/Signature/Svg/...). Only appears if such an element
    /// already exists in a loaded document.</summary>

    /// <summary>Real barcode rendering via BarcodeRenderer/ZXing.Net (see that class's
    /// own doc comment). Draws the generated code scaled to fit the element's box,
    /// aspect-preserving (never non-uniformly stretched -- that can break scanning for
    /// a 2D code, and distorts a linear code's module widths). Falls back to the
    /// original honest text-label placeholder (symbology + value, no fake bars) only
    /// when encoding genuinely fails -- an invalid value for the chosen symbology (e.g.
    /// a bad EAN-13 checksum length), not a missing capability.</summary>
    private static void DrawBarcode(DrawingContext context, Rect rect, BarcodeElement barcode)
    {
        using var clip = context.PushClip(rect);

        var bitmap = BarcodeRenderer.TryGenerate(barcode);
        if (bitmap is not null)
        {
            DrawFit(context, rect, bitmap);
            return;
        }

        DrawBarcodePlaceholder(context, rect, $"[{barcode.Symbology}]", barcode.Value, barcode.ForegroundColorHex);
    }

    /// <summary>Real QR Code rendering via BarcodeRenderer/ZXing.Net. Same fit/fallback
    /// reasoning as DrawBarcode above.</summary>
    private static void DrawQrCode(DrawingContext context, Rect rect, QrCodeElement qr)
    {
        using var clip = context.PushClip(rect);

        var bitmap = BarcodeRenderer.TryGenerate(qr);
        if (bitmap is not null)
        {
            DrawFit(context, rect, bitmap);
            return;
        }

        DrawBarcodePlaceholder(context, rect, "[QR Code]", qr.Value, qr.ForegroundColorHex);
    }

    /// <summary>Contain-fits a generated barcode/QR bitmap into rect, centered,
    /// preserving its aspect ratio -- same formula as DrawImage's own Fit case.</summary>
    private static void DrawFit(DrawingContext context, Rect rect, Avalonia.Media.Imaging.Bitmap bitmap)
    {
        var sourceSize = bitmap.PixelSize.ToSizeWithDpi(bitmap.Dpi);
        var fitScale = Math.Min(rect.Width / sourceSize.Width, rect.Height / sourceSize.Height);
        var fitW = sourceSize.Width * fitScale;
        var fitH = sourceSize.Height * fitScale;
        var destRect = new Rect(rect.X + (rect.Width - fitW) / 2, rect.Y + (rect.Height - fitH) / 2, fitW, fitH);
        context.DrawImage(bitmap, new Rect(sourceSize), destRect);
    }

    /// <summary>Honest "couldn't encode this" fallback -- the encoded value as text
    /// with a clear label instead of drawing fake bars/modules (Part 81: a plausible-
    /// looking but non-functional barcode is worse than an admitted placeholder). Text
    /// sizing/position is proportional to the element's own box (clamped to sane min/
    /// max), not fixed pixel offsets, so it stays legible and clipped at any box
    /// size/zoom.</summary>
    private static void DrawBarcodePlaceholder(DrawingContext context, Rect rect, string label, string? value, string foregroundColorHex)
    {
        context.DrawRectangle(PlaceholderFillBrush, new Pen(PlaceholderBorderBrush, 1, dashStyle: DashStyle.Dash), rect);

        var typeface = new Typeface("Segoe UI");
        var padding = Math.Max(2, rect.Height * 0.08);
        var labelSize = Math.Clamp(rect.Height * 0.16, 7, 11);
        var valueSize = Math.Clamp(rect.Height * 0.24, 8, 14);

        var labelText = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, labelSize, Brushes.Gray)
        {
            MaxTextWidth = Math.Max(1, rect.Width - padding * 2),
        };
        context.DrawText(labelText, rect.TopLeft + new Vector(padding, padding));

        var displayValue = string.IsNullOrEmpty(value) ? "(no value)" : value;
        var valueText = new FormattedText(displayValue, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, valueSize, ParseBrush(foregroundColorHex))
        {
            MaxTextWidth = Math.Max(1, rect.Width - padding * 2),
            TextAlignment = TextAlignment.Center,
        };
        var valueY = rect.Top + padding + labelText.Height + Math.Max(2, rect.Height * 0.06);
        context.DrawText(valueText, new Point(rect.Left + rect.Width / 2 - valueText.Width / 2, Math.Min(valueY, rect.Bottom - valueText.Height - padding)));
    }

    private static void DrawPlaceholder(DrawingContext context, Rect rect, DesignerElement element)
    {
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#22FFFFFF")), new Pen(Brushes.Gray, 1, dashStyle: DashStyle.Dash), rect);
        var formatted = new FormattedText(element.ElementType.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.Gray);
        context.DrawText(formatted, rect.TopLeft + new Vector(4, 4));
    }

    /// <summary>Individual selection uses a SOLID outline, deliberately distinct from
    /// the dashed style used for bleed/safe-zone/ruler guides -- those are all
    /// construction aids, while a selection outline means "this is what you're actively
    /// editing", and having every dashed line on screen mean something different was a
    /// real source of visual noise/confusion on a busy card.</summary>
    private void DrawSelectionOverlay(DrawingContext context, Rect cardRect, double scale, CardDesignTabViewModel tab)
    {
        // Priority 8 fix: each selected element's own outline now rotates with it
        // (using the identical GetRotationTransform DrawElement applies to the
        // element's content), instead of always being drawn as a plain axis-aligned
        // rectangle regardless of the element's actual Rotation.
        foreach (var element in tab.SelectedElements)
        {
            // A Line/Arrow/Pen's own drawn shape is already its selection indicator (see
            // the node-handle branch below) -- a bounding-box rectangle around a
            // diagonal line or an arbitrary stroke reads as a confusing, unrelated
            // "square frame" rather than useful feedback, so those two skip this generic
            // per-element outline entirely.
            if (IsNodeEditable(element)) continue;

            var elRect = ToDeviceRect(cardRect, scale, element);
            using var rotateScope = context.PushTransform(GetRotationTransform(elRect, element.Rotation));
            context.DrawRectangle(null, new Pen(SelectionBrush, 1.5), elRect);
        }

        if (tab.SelectedElements.Count == 1)
        {
            var selected = tab.SelectedElements[0];
            var rect = ToDeviceRect(cardRect, scale, selected);

            using var rotateScope = context.PushTransform(GetRotationTransform(rect, selected.Rotation));

            if (selected is ShapeElement { Kind: ShapeKind.Line or ShapeKind.Arrow } lineShape)
            {
                // Real endpoint handles instead of corner-of-bounding-box handles: a
                // line only has two meaningful control points, and dragging either one
                // directly (see HitTestLineNode/OnPointerMoved's DragMode.LineEndpoint)
                // is how every other vector-editing tool lets you reshape a line --
                // never "resize the rectangle it happens to sit in".
                var (start, end) = GetLineEndpointsDevice(rect, lineShape);
                context.DrawLine(new Pen(SelectionBrush, 2), start, end);
                DrawNodeHandle(context, start);
                DrawNodeHandle(context, end);
            }
            else if (selected is PenElement penEl)
            {
                foreach (var p in penEl.PointsFraction)
                {
                    DrawNodeHandle(context, FractionToDevicePoint(rect, p));
                }
            }
            else
            {
                foreach (var handle in GetHandleRects(rect))
                {
                    context.DrawRectangle(Brushes.White, new Pen(SelectionBrush, 1), handle);
                }

                var rotationHandleCenter = new Point(rect.Center.X, rect.Top - 24);
                context.DrawLine(new Pen(SelectionBrush, 1), new Point(rect.Center.X, rect.Top), rotationHandleCenter);
                context.DrawEllipse(SelectionBrush, new Pen(Brushes.White, 1.5), rotationHandleCenter, 7, 7);
            }
        }
        else if (tab.SelectedElements.Count > 1)
        {
            // Group bounding box (Part 30: "multi-selection bounding box"), in its own
            // color so it reads as "the whole selection" rather than another individual
            // element outline -- union of every selected element's own rotation-aware
            // visual bounds, in device pixels (same coordinate space the per-element
            // outlines above are already drawn in). Computed from Left/Top/Right/Bottom
            // rather than a Rect.Union call, consistent with how bounds math is done
            // elsewhere in this file.
            var rects = tab.SelectedElements.Select(el => ToDeviceRect(cardRect, scale, el)).ToList();
            var groupLeft = rects.Min(r => r.Left);
            var groupTop = rects.Min(r => r.Top);
            var groupRight = rects.Max(r => r.Right);
            var groupBottom = rects.Max(r => r.Bottom);
            var groupRect = new Rect(groupLeft, groupTop, groupRight - groupLeft, groupBottom - groupTop);
            context.DrawRectangle(null, new Pen(GroupSelectionBrush, 1.5, dashStyle: DashStyle.Dash), groupRect);
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

    /// <summary>Line/Arrow and Pen get real point-based selection/editing (see
    /// DrawSelectionOverlay/HitTestLineNode/HitTestPenNode) instead of the generic
    /// bounding-box outline + corner-resize-handle treatment every other element uses.</summary>
    private static bool IsNodeEditable(DesignerElement element) =>
        element is PenElement || element is ShapeElement { Kind: ShapeKind.Line or ShapeKind.Arrow };

    private static void DrawNodeHandle(DrawingContext context, Point center) =>
        context.DrawEllipse(Brushes.White, new Pen(SelectionBrush, 1.5), center, 5, 5);

    private static Point FractionToDevicePoint(Rect rect, PenPoint fraction) =>
        new(rect.X + fraction.X * rect.Width, rect.Y + fraction.Y * rect.Height);

    /// <summary>The un-rotated device-space endpoints of a Line/Arrow -- rect.TopLeft to
    /// rect.BottomRight normally, or the other diagonal when LineFlipped (see
    /// ShapeRenderer.Draw's own Line/Arrow cases, which this mirrors exactly so the
    /// handles always sit exactly on the drawn line).</summary>
    private static (Point Start, Point End) GetLineEndpointsDevice(Rect rect, ShapeElement shape)
    {
        var start = shape.LineFlipped ? rect.BottomLeft : rect.TopLeft;
        var end = shape.LineFlipped ? rect.TopRight : rect.BottomRight;
        return (start, end);
    }

    /// <summary>Same as GetLineEndpointsDevice but in the element's own millimeter space
    /// (X/Y/Width/Height as stored on the model) rather than a device-pixel Rect -- used
    /// to capture the "other" (not being dragged) endpoint's absolute position once at
    /// the start of a DragMode.LineEndpoint drag.</summary>
    private static (Point Start, Point End) GetLineEndpointsMm(ShapeElement shape)
    {
        var start = shape.LineFlipped ? new Point(shape.X, shape.Y + shape.Height) : new Point(shape.X, shape.Y);
        var end = shape.LineFlipped ? new Point(shape.X + shape.Width, shape.Y) : new Point(shape.X + shape.Width, shape.Y + shape.Height);
        return (start, end);
    }

    private static int HitTestLineNode(Rect elementRect, double rotationDeg, ShapeElement shape, Point point)
    {
        const double h = 12;
        var center = elementRect.Center;
        var (localStart, localEnd) = GetLineEndpointsDevice(elementRect, shape);
        var start = RotatePointAround(localStart, center, rotationDeg);
        var end = RotatePointAround(localEnd, center, rotationDeg);
        if (new Rect(start.X - h / 2, start.Y - h / 2, h, h).Contains(point)) return 0;
        if (new Rect(end.X - h / 2, end.Y - h / 2, h, h).Contains(point)) return 1;
        return -1;
    }

    private static int HitTestPenNode(Rect elementRect, double rotationDeg, PenElement pen, Point point)
    {
        const double h = 12;
        var center = elementRect.Center;
        for (var i = 0; i < pen.PointsFraction.Count; i++)
        {
            var local = FractionToDevicePoint(elementRect, pen.PointsFraction[i]);
            var rotated = RotatePointAround(local, center, rotationDeg);
            if (new Rect(rotated.X - h / 2, rotated.Y - h / 2, h, h).Contains(point)) return i;
        }
        return -1;
    }

    /// <summary>The exact rotation transform DrawElement applies to an element's own
    /// content: rotate elementRect's own center by rotationDeg. Factored out here
    /// (Priority 8 fix) so the selection outline/handles/rotation-handle drawn in
    /// DrawSelectionOverlay can be rotated with the identical transform instead of
    /// being drawn axis-aligned while the element itself visibly rotates underneath
    /// them -- which was the root cause of the selection frame looking "stuck".</summary>
    private static Matrix GetRotationTransform(Rect elementRect, double rotationDeg)
    {
        var center = elementRect.Center;
        return Matrix.CreateTranslation(-center.X, -center.Y)
               * Matrix.CreateRotation(rotationDeg * Math.PI / 180.0)
               * Matrix.CreateTranslation(center.X, center.Y);
    }

    /// <summary>Rotates a point around a pivot by the given angle in degrees, using the
    /// same direction convention as Avalonia's Matrix.CreateRotation (positive angle
    /// turns clockwise on screen, since screen Y grows downward) -- i.e. the same
    /// visual rotation GetRotationTransform/DrawElement already apply. Used for
    /// hit-testing (Priority 8 fix): rather than inverse-transforming the pointer
    /// through a Matrix, this rotates each handle's own known un-rotated position
    /// forward to its true on-screen location, which is then compared directly against
    /// the raw pointer position -- avoiding any dependency on exactly how Avalonia's
    /// Matrix invert/point-transform APIs are named, while remaining mathematically
    /// equivalent.</summary>
    private static Point RotatePointAround(Point p, Point pivot, double angleDeg)
    {
        if (angleDeg == 0) return p;
        var rad = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var dx = p.X - pivot.X;
        var dy = p.Y - pivot.Y;
        return new Point(pivot.X + dx * cos - dy * sin, pivot.Y + dx * sin + dy * cos);
    }

    /// <summary>Rotates a direction vector (not a position) by the given angle in
    /// degrees, same convention as RotatePointAround. Used to convert a screen-space
    /// drag delta into an element's own local/unrotated axes (Priority 8 fix for
    /// resize handles): dragging the visually-bottom-right corner of a rotated element
    /// should extend it along its own edges, not along the absolute screen X/Y axes.</summary>
    private static Vector RotateVector(Vector v, double angleDeg)
    {
        if (angleDeg == 0) return v;
        var rad = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        return new Vector(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
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

        // Guides only contribute snap targets when SnapToGuides is on (Part 28); the
        // card-center target below is always active (there's no dedicated toggle for
        // it, and it's cheap/harmless); the other-object edges/centers loop just below
        // is separately gated on SnapToObjects.
        var targets = new List<double>(tab.SnapToGuides ? guides : Enumerable.Empty<double>()) { cardCenter };
        if (tab.SnapToObjects)
        {
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

        if (tab.LineToolActive || tab.PenToolActive)
        {
            var (toolScale, toolSideRects) = ComputeLayout(tab);
            var toolHit = toolSideRects.FirstOrDefault(kv => kv.Value.Contains(point));
            if (toolHit.Value != default)
            {
                tab.FocusedSide = toolHit.Key;
                if (tab.LineToolActive)
                {
                    _dragMode = DragMode.DrawLine;
                    _lineStartPoint = point;
                    _lineEndPoint = point;
                }
                else
                {
                    _dragMode = DragMode.DrawPen;
                    _penPoints.Clear();
                    _penPoints.Add(point);
                }
                e.Pointer.Capture(this);
                InvalidateVisual();
            }
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

            if (selected is ShapeElement { Kind: ShapeKind.Line or ShapeKind.Arrow } lineShape)
            {
                var nodeIndex = HitTestLineNode(elementRect, selected.Rotation, lineShape, point);
                if (nodeIndex >= 0)
                {
                    var (startMm, endMm) = GetLineEndpointsMm(lineShape);
                    _dragMode = DragMode.LineEndpoint;
                    _dragElement = selected;
                    _dragNodeIndex = nodeIndex;
                    _dragStartLineFlipped = lineShape.LineFlipped;
                    _dragStartCenterMm = new Point(selected.X + selected.Width / 2, selected.Y + selected.Height / 2);
                    _dragOtherEndpointMm = nodeIndex == 0 ? endMm : startMm;
                    _dragStartRects.Clear();
                    _dragStartRects[selected] = (selected.X, selected.Y, selected.Width, selected.Height);
                    e.Pointer.Capture(this);
                    return;
                }
            }
            else if (selected is PenElement penEl)
            {
                var nodeIndex = HitTestPenNode(elementRect, selected.Rotation, penEl, point);
                if (nodeIndex >= 0)
                {
                    _dragMode = DragMode.PenNode;
                    _dragElement = selected;
                    _dragNodeIndex = nodeIndex;
                    _dragStartCenterMm = new Point(selected.X + selected.Width / 2, selected.Y + selected.Height / 2);
                    _dragStartPenPoints = penEl.PointsFraction.ToList();
                    e.Pointer.Capture(this);
                    return;
                }
            }
            else
            {
                var handle = HitTestHandle(elementRect, selected.Rotation, point);
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
        // Priority 8 fix (related): a rotated element's clickable area is its own
        // rotated shape, not its plain axis-aligned X/Y/Width/Height rect. mmPoint is
        // rotated backward into the element's local frame (around its own center, in
        // the same mm space el.X/Y/Width/Height are already expressed in) before the
        // ordinary Contains test, using the same RotatePointAround helper the
        // handle/selection-outline fixes above use -- negative angle here undoes the
        // element's own +Rotation, landing mmPoint where it would be if the element
        // were drawn un-rotated.
        var hitElement = elements
            .Where(el => el.Visible && !el.Locked)
            .OrderByDescending(el => el.ZIndex)
            .FirstOrDefault(el =>
            {
                var localPoint = el.Rotation == 0
                    ? mmPoint
                    : RotatePointAround(mmPoint, new Point(el.X + el.Width / 2, el.Y + el.Height / 2), -el.Rotation);
                return new Rect(el.X, el.Y, el.Width, el.Height).Contains(localPoint);
            });

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

        if (_dragMode == DragMode.DrawLine)
        {
            // Hold Shift to constrain to horizontal/vertical/45-degree, the same
            // "hold a modifier to constrain" convention Rotate already uses (15-degree
            // snap) a few blocks down.
            var start = _lineStartPoint!.Value;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                var delta = point - start;
                var angle = Math.Round(Math.Atan2(delta.Y, delta.X) / (Math.PI / 4)) * (Math.PI / 4);
                var length = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
                point = start + new Vector(Math.Cos(angle) * length, Math.Sin(angle) * length);
            }
            _lineEndPoint = point;
            InvalidateVisual();
            return;
        }

        if (_dragMode == DragMode.DrawPen)
        {
            // Minimum spacing is expressed in mm (converted to device px via the current
            // zoom), not a flat device-px number -- a flat px threshold captures far more
            // points at high zoom (more device px per mm of actual mouse movement) than
            // at low zoom, which is exactly what produced a stroke made of near-
            // touching/overlapping points at 235% zoom. A real-world spacing keeps point
            // density (and so node-handle density -- see HitTestPenNode) consistent
            // regardless of how zoomed in the user happens to be.
            var minSpacingPx = MinPenPointSpacingMm * PixelsPerMm * tab.Zoom;
            var last = _penPoints[^1];
            var moved = point - last;
            if (Math.Sqrt(moved.X * moved.X + moved.Y * moved.Y) >= minSpacingPx)
            {
                _penPoints.Add(point);
                InvalidateVisual();
            }
            return;
        }

        var (scale, sideRects) = ComputeLayout(tab);
        if (!sideRects.TryGetValue(tab.FocusedSide, out var cardRect)) return;

        if (_dragMode == DragMode.LineEndpoint && _dragElement is ShapeElement lineShape)
        {
            var mmPoint = ToMm(cardRect, scale, point);
            var draggedMm = lineShape.Rotation == 0
                ? mmPoint
                : RotatePointAround(mmPoint, _dragStartCenterMm, -lineShape.Rotation);
            var otherMm = _dragOtherEndpointMm;

            var minX = Math.Min(otherMm.X, draggedMm.X);
            var minY = Math.Min(otherMm.Y, draggedMm.Y);
            var width = Math.Max(0.5, Math.Abs(draggedMm.X - otherMm.X));
            var height = Math.Max(0.5, Math.Abs(draggedMm.Y - otherMm.Y));

            var leftPoint = otherMm.X <= draggedMm.X ? otherMm : draggedMm;
            var rightPoint = otherMm.X <= draggedMm.X ? draggedMm : otherMm;

            lineShape.X = minX;
            lineShape.Y = minY;
            lineShape.Width = width;
            lineShape.Height = height;
            lineShape.LineFlipped = leftPoint.Y > rightPoint.Y;
            InvalidateVisual();
            return;
        }

        if (_dragMode == DragMode.PenNode && _dragElement is PenElement penEl && _dragNodeIndex >= 0 && _dragNodeIndex < penEl.PointsFraction.Count)
        {
            var mmPoint = ToMm(cardRect, scale, point);
            var localMm = penEl.Rotation == 0
                ? mmPoint
                : RotatePointAround(mmPoint, _dragStartCenterMm, -penEl.Rotation);

            var fx = penEl.Width > 0 ? (localMm.X - penEl.X) / penEl.Width : 0;
            var fy = penEl.Height > 0 ? (localMm.Y - penEl.Y) / penEl.Height : 0;
            penEl.PointsFraction[_dragNodeIndex] = new PenPoint(fx, fy);
            InvalidateVisual();
            return;
        }

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
            var rawDeltaMm = new Vector(
                (point.X - _dragStartPointerPos!.Value.X) / scale,
                (point.Y - _dragStartPointerPos.Value.Y) / scale);

            // Priority 8 fix: rotate the screen-space drag delta into the element's own
            // local (un-rotated) axes before resizing, so dragging a corner of a
            // rotated element extends it along its own edges rather than along the
            // absolute screen X/Y axes -- otherwise resizing a rotated element would
            // "work" (no crash) but feel wrong/unintuitive, moving the wrong direction
            // relative to what the user is visually dragging.
            var deltaMm = RotateVector(rawDeltaMm, -_dragElement.Rotation);

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
        else if (_dragMode == DragMode.DrawLine && _lineStartPoint is { } lineStart && _lineEndPoint is { } lineEnd)
        {
            var (scale, sideRects) = ComputeLayout(tab);
            if (sideRects.TryGetValue(tab.FocusedSide, out var cardRect))
            {
                var startMm = ToMm(cardRect, scale, lineStart);
                var endMm = ToMm(cardRect, scale, lineEnd);
                tab.InsertDrawnLine(startMm.X, startMm.Y, endMm.X, endMm.Y);
            }
        }
        else if (_dragMode == DragMode.DrawPen && _penPoints.Count >= 2)
        {
            var (scale, sideRects) = ComputeLayout(tab);
            if (sideRects.TryGetValue(tab.FocusedSide, out var cardRect))
            {
                var pointsMm = _penPoints.Select(p =>
                {
                    var mm = ToMm(cardRect, scale, p);
                    return (mm.X, mm.Y);
                }).ToList();
                tab.InsertDrawnPen(pointsMm);
            }
        }
        else if (_dragMode == DragMode.LineEndpoint && _dragElement is ShapeElement lineShapeReleased && _dragStartRects.TryGetValue(_dragElement, out var lineStartRect))
        {
            var after = (_dragElement.X, _dragElement.Y, _dragElement.Width, _dragElement.Height);
            var commands = new List<IDesignCommand>();
            if (lineStartRect.X != after.X || lineStartRect.Y != after.Y || lineStartRect.W != after.Width || lineStartRect.H != after.Height)
            {
                commands.Add(new TransformElementCommand(_dragElement, lineStartRect, after));
            }
            if (lineShapeReleased.LineFlipped != _dragStartLineFlipped)
            {
                commands.Add(new ChangePropertyCommand<ShapeElement, bool>(
                    lineShapeReleased, static (el, v) => el.LineFlipped = v, _dragStartLineFlipped, lineShapeReleased.LineFlipped, "Adjust line"));
            }

            if (commands.Count > 0)
            {
                var composite = commands.Count == 1 ? commands[0] : new CompositeCommand("Adjust line", commands);
                tab.History.Record(composite);
                tab.MarkDirty();
                tab.RefreshHistoryFlags();
            }
        }
        else if (_dragMode == DragMode.PenNode && _dragElement is PenElement penElReleased && _dragStartPenPoints is { } penStartPoints)
        {
            var penAfterPoints = penElReleased.PointsFraction.ToList();
            if (!penStartPoints.SequenceEqual(penAfterPoints))
            {
                tab.History.Record(new ChangePropertyCommand<PenElement, List<PenPoint>>(
                    penElReleased,
                    static (el, pts) =>
                    {
                        el.PointsFraction.Clear();
                        foreach (var p in pts) el.PointsFraction.Add(p);
                    },
                    penStartPoints, penAfterPoints, "Adjust drawing"));
                tab.MarkDirty();
                tab.RefreshHistoryFlags();
            }
        }

        _dragMode = DragMode.None;
        _dragElement = null;
        _dragStartRects.Clear();
        _activeSnapLines.Clear();
        _lineStartPoint = null;
        _lineEndPoint = null;
        _penPoints.Clear();
        _dragNodeIndex = -1;
        _dragStartPenPoints = null;
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

    private static DragMode HitTestHandle(Rect elementRect, double rotationDeg, Point point)
    {
        const double h = 9;
        var center = elementRect.Center;

        // Priority 8 fix: every handle's true on-screen position is now computed by
        // rotating its known un-rotated position forward by the element's own
        // Rotation (matching GetRotationTransform/DrawSelectionOverlay exactly),
        // instead of testing the raw pointer against the plain un-rotated rect. Without
        // this, fixing the *drawing* to rotate the handles would have made clicking
        // them impossible whenever Rotation != 0 (the handles would look rotated but
        // only respond to clicks at their old, un-rotated positions).
        var rotationHandleCenter = RotatePointAround(new Point(center.X, elementRect.Top - 24), center, rotationDeg);
        if (new Rect(rotationHandleCenter.X - 9, rotationHandleCenter.Y - 9, 18, 18).Contains(point)) return DragMode.Rotate;

        var topLeft = RotatePointAround(elementRect.TopLeft, center, rotationDeg);
        var topRight = RotatePointAround(elementRect.TopRight, center, rotationDeg);
        var bottomLeft = RotatePointAround(elementRect.BottomLeft, center, rotationDeg);
        var bottomRight = RotatePointAround(elementRect.BottomRight, center, rotationDeg);

        if (new Rect(topLeft.X - h / 2, topLeft.Y - h / 2, h, h).Contains(point)) return DragMode.ResizeTopLeft;
        if (new Rect(topRight.X - h / 2, topRight.Y - h / 2, h, h).Contains(point)) return DragMode.ResizeTopRight;
        if (new Rect(bottomLeft.X - h / 2, bottomLeft.Y - h / 2, h, h).Contains(point)) return DragMode.ResizeBottomLeft;
        if (new Rect(bottomRight.X - h / 2, bottomRight.Y - h / 2, h, h).Contains(point)) return DragMode.ResizeBottomRight;
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