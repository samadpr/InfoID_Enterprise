using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace InfoID.Desktop.Features.CardDesigner.Views.Controls;

/// <summary>
/// The actual interactive part of the crop editor (Part 38: "Support: Import, Replace,
/// Crop, Pan, Zoom..."). Shows the whole source image contain-fit in the control, with a
/// dimmed overlay everywhere except a bright "viewport" rectangle showing exactly what
/// will render on the card -- drag anywhere to pan, scroll to zoom.
///
/// CropX/CropY/CropZoom here mean exactly what they mean in
/// CardCanvasView.DrawImage/DrawPhoto's Fill-mode formula (same variable names, same
/// math) so what the user sees here is exactly what ends up on the card -- this was
/// deliberately built by reading that formula first rather than inventing a new one that
/// might drift from the actual renderer.
/// </summary>
public sealed class CropEditorSurface : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<CropEditorSurface, Bitmap?>(nameof(Source));

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Target element's own Width/Height ratio -- the crop viewport rectangle
    /// is drawn at this aspect ratio, since that's the box the cropped image will
    /// actually be placed into on the card.</summary>
    public static readonly StyledProperty<double> AspectRatioProperty =
        AvaloniaProperty.Register<CropEditorSurface, double>(nameof(AspectRatio), 1.0);

    public double AspectRatio
    {
        get => GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    public static readonly StyledProperty<double> CropXProperty =
        AvaloniaProperty.Register<CropEditorSurface, double>(nameof(CropX), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public double CropX
    {
        get => GetValue(CropXProperty);
        set => SetValue(CropXProperty, value);
    }

    public static readonly StyledProperty<double> CropYProperty =
        AvaloniaProperty.Register<CropEditorSurface, double>(nameof(CropY), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public double CropY
    {
        get => GetValue(CropYProperty);
        set => SetValue(CropYProperty, value);
    }

    public static readonly StyledProperty<double> CropZoomProperty =
        AvaloniaProperty.Register<CropEditorSurface, double>(nameof(CropZoom), 1.0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public double CropZoom
    {
        get => GetValue(CropZoomProperty);
        set => SetValue(CropZoomProperty, value);
    }

    private Point? _dragStart;
    private double _dragStartCropX;
    private double _dragStartCropY;
    private double _lastDisplayScale = 1;

    static CropEditorSurface()
    {
        AffectsRender<CropEditorSurface>(SourceProperty, AspectRatioProperty, CropXProperty, CropYProperty, CropZoomProperty);
    }

    public CropEditorSurface()
    {
        ClipToBounds = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _dragStart = e.GetPosition(this);
        _dragStartCropX = CropX;
        _dragStartCropY = CropY;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is not { } start || _lastDisplayScale <= 0) return;

        var current = e.GetPosition(this);
        var deltaDisplay = current - start;

        // Screen/display pixels -> original image pixels, the inverse of the
        // contain-fit display scale computed during the last Render call.
        CropX = _dragStartCropX + deltaDisplay.X / _lastDisplayScale;
        CropY = _dragStartCropY + deltaDisplay.Y / _lastDisplayScale;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragStart = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var factor = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
        CropZoom = Math.Clamp(CropZoom * factor, 0.2, 5);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#1E1E1E")), bounds);

        var bitmap = Source;
        if (bitmap is null || bounds.Width <= 0 || bounds.Height <= 0) return;

        var sourceSize = bitmap.PixelSize.ToSizeWithDpi(bitmap.Dpi);
        var displayScale = Math.Min(bounds.Width / sourceSize.Width, bounds.Height / sourceSize.Height);
        _lastDisplayScale = displayScale;

        var imageDisplaySize = new Size(sourceSize.Width * displayScale, sourceSize.Height * displayScale);
        var imageOrigin = new Point(
            bounds.Center.X - imageDisplaySize.Width / 2,
            bounds.Center.Y - imageDisplaySize.Height / 2);
        var imageDisplayRect = new Rect(imageOrigin, imageDisplaySize);

        context.DrawImage(bitmap, new Rect(sourceSize), imageDisplayRect);

        // Same formula as DrawImage/DrawPhoto's Fill mode -- this IS the region that
        // will actually render on the card, in original-image pixel coordinates.
        var aspect = AspectRatio <= 0 ? 1.0 : AspectRatio;
        var boxW = sourceSize.Width;
        var boxH = boxW / aspect;
        var fillScale = Math.Max(boxW / sourceSize.Width, boxH / sourceSize.Height) * Math.Max(CropZoom, 0.1);
        var visibleW = boxW / fillScale;
        var visibleH = boxH / fillScale;
        var maxOffsetX = Math.Max(0, sourceSize.Width - visibleW);
        var maxOffsetY = Math.Max(0, sourceSize.Height - visibleH);
        var offsetX = Math.Clamp((sourceSize.Width - visibleW) / 2 + CropX, 0, maxOffsetX);
        var offsetY = Math.Clamp((sourceSize.Height - visibleH) / 2 + CropY, 0, maxOffsetY);

        // Convert that viewport (in original image pixels) into display pixels.
        var viewportDisplay = new Rect(
            imageOrigin.X + offsetX * displayScale,
            imageOrigin.Y + offsetY * displayScale,
            Math.Min(visibleW, sourceSize.Width) * displayScale,
            Math.Min(visibleH, sourceSize.Height) * displayScale);

        // Dim everything outside the viewport so it reads as "this part won't show",
        // then outline the viewport itself.
        var dimBrush = new SolidColorBrush(Colors.Black) { Opacity = 0.55 };
        context.FillRectangle(dimBrush, new Rect(bounds.X, bounds.Y, bounds.Width, Math.Max(0, viewportDisplay.Y - bounds.Y)));
        context.FillRectangle(dimBrush, new Rect(bounds.X, viewportDisplay.Bottom, bounds.Width, Math.Max(0, bounds.Bottom - viewportDisplay.Bottom)));
        context.FillRectangle(dimBrush, new Rect(bounds.X, viewportDisplay.Y, Math.Max(0, viewportDisplay.X - bounds.X), viewportDisplay.Height));
        context.FillRectangle(dimBrush, new Rect(viewportDisplay.Right, viewportDisplay.Y, Math.Max(0, bounds.Right - viewportDisplay.Right), viewportDisplay.Height));

        context.DrawRectangle(null, new Pen(Brushes.White, 2), viewportDisplay);
    }
}
