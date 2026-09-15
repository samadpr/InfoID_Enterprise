using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using InfoID.Desktop.Features.CardDesigner.ViewModels;

namespace InfoID.Desktop.Features.CardDesigner.Views.Controls;

public enum RulerOrientation { Horizontal, Vertical }

/// <summary>
/// A physical-unit (mm) ruler strip -- horizontal (placed above the canvas) or vertical
/// (placed to its left) -- implementing Part 22 (rulers) and the drag-to-create-guide
/// gesture from Part 21/23. Ruler origin/scale is computed with the exact same formula
/// CardCanvasView uses (CardCanvasView.ComputeOriginX/Y), which only works correctly
/// because CardDesignerView.axaml lays this control out with the same Width (horizontal
/// ruler) or Height (vertical ruler) as the canvas itself -- see that file's Grid.
/// </summary>
public sealed class RulerView : Control
{
    public static readonly StyledProperty<CardDesignTabViewModel?> TabProperty =
        AvaloniaProperty.Register<RulerView, CardDesignTabViewModel?>(nameof(Tab));

    public CardDesignTabViewModel? Tab
    {
        get => GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    public static readonly StyledProperty<RulerOrientation> OrientationProperty =
        AvaloniaProperty.Register<RulerView, RulerOrientation>(nameof(Orientation));

    public RulerOrientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    // Light/dark pairs -- same "read Application.Current.ActualThemeVariant directly"
    // approach as CardCanvasView.WorkspaceBackgroundBrush, for the same reason: this is
    // a custom-drawn Control painting itself in one Render() pass, not something a
    // XAML DynamicResource can reach. Dark values are unchanged from what this ruler
    // always used; Light values are new (previously hardcoded dark regardless of
    // theme, same bug class as the canvas's own background).
    private static readonly IBrush BackgroundBrushLight = new SolidColorBrush(Color.Parse("#EDEEF1"));
    private static readonly IBrush BackgroundBrushDark = new SolidColorBrush(Color.Parse("#252526"));
    private static readonly IBrush TickBrushLight = new SolidColorBrush(Color.Parse("#6B7078"));
    private static readonly IBrush TickBrushDark = new SolidColorBrush(Color.Parse("#8A8A8A"));
    private static readonly IBrush TextBrushLight = new SolidColorBrush(Color.Parse("#4A4E55"));
    private static readonly IBrush TextBrushDark = new SolidColorBrush(Color.Parse("#B0B0B0"));
    private static readonly IBrush CursorMarkerBrush = new SolidColorBrush(Color.Parse("#00B4D8"));

    private static bool IsDarkTheme =>
        Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;

    private static IBrush BackgroundBrush => IsDarkTheme ? BackgroundBrushDark : BackgroundBrushLight;
    private static IBrush TickBrush => IsDarkTheme ? TickBrushDark : TickBrushLight;
    private static IBrush TextBrush => IsDarkTheme ? TextBrushDark : TextBrushLight;

    private double? _cursorPositionPx;
    private bool _isDraggingGuide;

    /// <summary>Raised continuously while a guide is being dragged out of this ruler,
    /// with the current mm position (or null when the drag ends) -- lets
    /// CardDesignerView forward it into tab.GuidePreviewMm so the canvas can draw a
    /// live preview line before the guide is actually committed.</summary>
    public event EventHandler<double?>? GuidePreviewChanged;

    /// <summary>Raised once, on release, with the final mm position of a newly created
    /// guide.</summary>
    public event EventHandler<double>? GuideCommitted;

    static RulerView()
    {
        AffectsRender<RulerView>(TabProperty, OrientationProperty);
    }

    public RulerView()
    {
        // Toggling the app's theme doesn't touch Tab/Orientation (the only properties
        // AffectsRender watches above), so without this the ruler would keep showing
        // its old-theme colors until some unrelated redraw happened to occur -- same
        // reasoning as CardCanvasView's own constructor.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TabProperty)
        {
            if (change.OldValue is CardDesignTabViewModel oldTab) oldTab.PropertyChanged -= OnTabPropertyChanged;
            if (change.NewValue is CardDesignTabViewModel newTab) newTab.PropertyChanged += OnTabPropertyChanged;
            InvalidateVisual();
        }
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => InvalidateVisual();

    /// <summary>Called by CardDesignerView.axaml.cs whenever the canvas reports pointer
    /// movement, so the ruler can show a live cursor-position tick even though the
    /// pointer itself never enters the ruler's own bounds during normal use.
    ///
    /// Priority 6 bug fix: mmAlongAxis comes from CardCanvasView.ScreenPointToFocusedMm,
    /// which is always relative to tab.FocusedSide's own card origin (that method's
    /// name says so, and its doc comment confirms it) -- so converting it back to a
    /// screen position must use that SAME side's origin via the side-aware
    /// ComputeOriginX/Y(tab, extent, side) overload, not the side-blind one. Using the
    /// side-blind overload here (as this used to) silently assumed the mm value was
    /// always relative to the Front card, so the marker landed exactly one
    /// card-width-plus-gap away from the actual mouse position whenever Back was the
    /// focused side in "Both" view.</summary>
    public void UpdateCursorPosition(double? mmAlongAxis)
    {
        if (mmAlongAxis is null) { _cursorPositionPx = null; InvalidateVisual(); return; }

        var tab = Tab;
        if (tab is null) return;
        var scale = CardCanvasView.PixelsPerMm * tab.Zoom;
        var origin = Orientation == RulerOrientation.Horizontal
            ? CardCanvasView.ComputeOriginX(tab, Bounds.Width, tab.FocusedSide)
            : CardCanvasView.ComputeOriginY(tab, Bounds.Height);
        _cursorPositionPx = origin + mmAlongAxis.Value * scale;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));

        var tab = Tab;
        if (tab is null) return;

        var scale = CardCanvasView.PixelsPerMm * tab.Zoom;
        var horizontal = Orientation == RulerOrientation.Horizontal;
        var origin = horizontal ? CardCanvasView.ComputeOriginX(tab, Bounds.Width) : CardCanvasView.ComputeOriginY(tab, Bounds.Height);
        var extentPx = horizontal ? Bounds.Width : Bounds.Height;
        var thickness = horizontal ? Bounds.Height : Bounds.Width;

        // Choose a tick spacing (mm) that keeps ticks legible at the current zoom --
        // avoids an unreadable solid black bar at high zoom-out or a sparse ruler at
        // high zoom-in.
        double[] steps = { 1, 2, 5, 10, 20, 50, 100 };
        var stepMm = 10.0;
        foreach (var s in steps)
        {
            if (s * scale >= 8) { stepMm = s; break; }
        }
        var majorEvery = stepMm < 10 ? 10.0 / stepMm : 1;

        var pen = new Pen(TickBrush, 1);
        var startMm = Math.Floor(-origin / scale / stepMm) * stepMm;
        var endMm = (extentPx - origin) / scale;

        var typeface = new Typeface("Segoe UI");
        var tickIndex = (long)Math.Round(startMm / stepMm);
        for (var mm = startMm; mm <= endMm; mm += stepMm, tickIndex++)
        {
            var pos = origin + mm * scale;
            var isMajor = majorEvery <= 1 || tickIndex % (long)majorEvery == 0;
            var tickLen = isMajor ? thickness * 0.55 : thickness * 0.3;

            if (horizontal)
            {
                context.DrawLine(pen, new Point(pos, thickness), new Point(pos, thickness - tickLen));
            }
            else
            {
                context.DrawLine(pen, new Point(thickness, pos), new Point(thickness - tickLen, pos));
            }

            if (isMajor && Math.Abs(mm) > 0.001)
            {
                var label = Math.Round(mm).ToString(CultureInfo.InvariantCulture);
                var formatted = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, TextBrush);
                if (horizontal)
                {
                    context.DrawText(formatted, new Point(pos + 2, 1));
                }
                else
                {
                    // NOTE: the translation here is (small indent, pos) -- pos is this
                    // tick's screen Y position along the ruler. Previously this was
                    // (pos, 1), i.e. the arguments were swapped: every label's anchor
                    // point ended up at world Y=1 (pinned to the very top) with X=pos,
                    // which mostly fell outside this ~20px-wide vertical strip and was
                    // clipped -- exactly the "numbers only at the top" symptom.
                    using var rotate = context.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(1, pos));
                    context.DrawText(formatted, new Point(0, 0));
                }
            }
        }

        if (_cursorPositionPx is { } cursorPos)
        {
            var markerPen = new Pen(CursorMarkerBrush, 1);
            if (horizontal) context.DrawLine(markerPen, new Point(cursorPos, 0), new Point(cursorPos, thickness));
            else context.DrawLine(markerPen, new Point(0, cursorPos), new Point(thickness, cursorPos));
        }
    }

    // ---------------------------------------------------- drag-to-create-guide ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Tab is null) return;
        _isDraggingGuide = true;
        e.Pointer.Capture(this);
        ReportPreview(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDraggingGuide) return;
        ReportPreview(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDraggingGuide) return;
        _isDraggingGuide = false;
        e.Pointer.Capture(null);

        var mm = PointToMm(e.GetPosition(this));
        if (mm is { } value) GuideCommitted?.Invoke(this, value);
        GuidePreviewChanged?.Invoke(this, null);
    }

    private void ReportPreview(Point point)
    {
        var mm = PointToMm(point);
        GuidePreviewChanged?.Invoke(this, mm);
    }

    /// <summary>Converts a ruler-local point into an mm value relative to the currently
    /// focused side's own card origin -- matching the same reference frame
    /// CardCanvasView.ScreenPointToFocusedMm and the guide-drawing code in
    /// CardCanvasView.DrawSide both already use (guides are stored as a single set of
    /// "mm relative to a card's own local origin" values, drawn against whichever side
    /// is focused). Same Priority 6 bug/fix as UpdateCursorPosition above: this used to
    /// always use the side-blind origin overload, so dragging a new guide out of this
    /// ruler while Back was the focused side in "Both" view produced a guide positioned
    /// as if it were relative to Front's origin instead -- landing it visibly wrong
    /// once drawn against Back's own card rect.</summary>
    private double? PointToMm(Point pointerPosInRuler)
    {
        var tab = Tab;
        if (tab is null) return null;
        var scale = CardCanvasView.PixelsPerMm * tab.Zoom;

        // The pointer is captured, so while dragging it can report positions well
        // outside this ruler strip's own bounds (e.g. once the drag has moved onto the
        // canvas below/right of it) -- that's expected and desired, since the guide's
        // final mm position should reflect wherever the pointer ended up over the card,
        // not be clamped to the ruler's own small footprint.
        if (Orientation == RulerOrientation.Horizontal)
        {
            var origin = CardCanvasView.ComputeOriginX(tab, Bounds.Width, tab.FocusedSide);
            return (pointerPosInRuler.X - origin) / scale;
        }
        else
        {
            var origin = CardCanvasView.ComputeOriginY(tab, Bounds.Height);
            return (pointerPosInRuler.Y - origin) / scale;
        }
    }
}
