using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using InfoID.Desktop.Features.CardDesigner.ViewModels;

namespace InfoID.Desktop.Features.CardDesigner.Views;

/// <summary>Code-behind for the crop rectangle's drag-to-select interaction (Priority
/// 12). Kept in code-behind rather than pure bindings for the same reason
/// CardCanvasView's own selection/resize handles are: a crop rectangle is fundamentally
/// a pointer-drag interaction, not something that changes on its own from data.
///
/// Coordinate handling: the preview Image uses Stretch="Uniform", so the actual
/// displayed picture is letterboxed within PreviewHost's bounds whenever the image's
/// aspect ratio doesn't exactly match the panel's -- the same situation CardCanvasView.
/// DrawImage's own "Fit" case already handles for on-canvas image elements. GetImageRect
/// below reuses that exact formula (fitScale = min(hostW/imgW, hostH/imgH), centered)
/// so a drag is measured against where the picture is actually drawn, not the whole
/// (possibly larger, letterboxed) host panel.</summary>
public partial class ImageEditorDialogView : UserControl
{
    private Canvas _cropCanvas = null!;
    private Panel _previewHost = null!;
    private Image _previewImageControl = null!;
    private Rectangle _selectionRect = null!;
    private Rectangle _faceBoxRect = null!;

    private bool _dragging;
    private Point _dragStartCanvasPoint;
    private bool _syncingFromViewModel;

    public ImageEditorDialogView()
    {
        InitializeComponent();

        _cropCanvas = this.FindControl<Canvas>("CropCanvas")!;
        _previewHost = this.FindControl<Panel>("PreviewHost")!;
        _previewImageControl = this.FindControl<Image>("PreviewImageControl")!;
        _selectionRect = this.FindControl<Rectangle>("CropSelectionRect")!;
        _faceBoxRect = this.FindControl<Rectangle>("FaceBoxRect")!;

        DataContextChanged += (_, _) => OnDataContextChanged();
    }

    private ImageEditorDialogViewModel? ViewModel => DataContext as ImageEditorDialogViewModel;

    private void OnDataContextChanged()
    {
        if (ViewModel is { } vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_dragging) return; // avoid fighting an in-progress drag with a sync-back
        if (e.PropertyName is nameof(ImageEditorDialogViewModel.CropRegionFraction)
            or nameof(ImageEditorDialogViewModel.PreviewImage)
            or nameof(ImageEditorDialogViewModel.DetectedFaceBoxFraction))
        {
            SyncSelectionVisualFromViewModel();
            SyncFaceBoxVisualFromViewModel();
        }
    }

    /// <summary>Repositions the (non-interactive, informational) face-box Rectangle to
    /// match the ViewModel's current DetectedFaceBoxFraction -- the raw, tight box the
    /// model found, shown alongside (not instead of) the padded CropSelectionRect.</summary>
    private void SyncFaceBoxVisualFromViewModel()
    {
        if (ViewModel is not { } vm) return;

        if (vm.DetectedFaceBoxFraction is not { } fraction)
        {
            _faceBoxRect.IsVisible = false;
            return;
        }

        var imageRect = GetImageRect();
        if (imageRect is not { } rect) return;

        Canvas.SetLeft(_faceBoxRect, rect.X + fraction.X * rect.Width);
        Canvas.SetTop(_faceBoxRect, rect.Y + fraction.Y * rect.Height);
        _faceBoxRect.Width = Math.Max(0, fraction.Width * rect.Width);
        _faceBoxRect.Height = Math.Max(0, fraction.Height * rect.Height);
        _faceBoxRect.IsVisible = true;
    }

    /// <summary>Repositions the (non-interactive) selection Rectangle to match the
    /// ViewModel's current CropRegionFraction -- used when the fraction changes from
    /// something other than this View's own drag (Reset/ResetCrop commands, or a fresh
    /// image being loaded).</summary>
    private void SyncSelectionVisualFromViewModel()
    {
        if (ViewModel is not { } vm) return;

        var fraction = vm.CropRegionFraction;
        var isFullImage = fraction.X == 0 && fraction.Y == 0 && fraction.Width >= 1 && fraction.Height >= 1;
        if (isFullImage)
        {
            _selectionRect.IsVisible = false;
            return;
        }

        var imageRect = GetImageRect();
        if (imageRect is not { } rect) return;

        _syncingFromViewModel = true;
        try
        {
            Canvas.SetLeft(_selectionRect, rect.X + fraction.X * rect.Width);
            Canvas.SetTop(_selectionRect, rect.Y + fraction.Y * rect.Height);
            _selectionRect.Width = Math.Max(0, fraction.Width * rect.Width);
            _selectionRect.Height = Math.Max(0, fraction.Height * rect.Height);
            _selectionRect.IsVisible = true;
        }
        finally
        {
            _syncingFromViewModel = false;
        }
    }

    /// <summary>The rectangle, in CropCanvas-local coordinates, where the preview image
    /// is actually drawn -- accounting for Stretch="Uniform" letterboxing. Mirrors
    /// CardCanvasView.DrawImage's "Fit" case formula exactly. Returns null if there's no
    /// image yet or the host hasn't been laid out (zero bounds).</summary>
    private Rect? GetImageRect()
    {
        if (_previewImageControl.Source is not IImage image) return null;

        var hostW = _previewHost.Bounds.Width;
        var hostH = _previewHost.Bounds.Height;
        var imgW = image.Size.Width;
        var imgH = image.Size.Height;
        if (hostW <= 0 || hostH <= 0 || imgW <= 0 || imgH <= 0) return null;

        var fitScale = Math.Min(hostW / imgW, hostH / imgH);
        var fitW = imgW * fitScale;
        var fitH = imgH * fitScale;
        var x = (hostW - fitW) / 2;
        var y = (hostH - fitH) / 2;

        return new Rect(x, y, fitW, fitH);
    }

    private void OnCropCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_cropCanvas).Properties.IsLeftButtonPressed) return;
        if (GetImageRect() is not { } imageRect) return;

        // Eyedropper mode (Background tab) takes over the click instead of starting a
        // crop drag -- see ImageEditorDialogViewModel.IsPickingBackgroundColor's own
        // doc comment.
        if (ViewModel is { IsPickingBackgroundColor: true } vm)
        {
            var clickPoint = Clamp(e.GetPosition(_cropCanvas), imageRect);
            PickBackgroundColor(clickPoint, imageRect, vm);
            vm.IsPickingBackgroundColor = false;
            return;
        }

        _dragging = true;
        e.Pointer.Capture(_cropCanvas);
        _dragStartCanvasPoint = Clamp(e.GetPosition(_cropCanvas), imageRect);

        _selectionRect.IsVisible = true;
        Canvas.SetLeft(_selectionRect, _dragStartCanvasPoint.X);
        Canvas.SetTop(_selectionRect, _dragStartCanvasPoint.Y);
        _selectionRect.Width = 0;
        _selectionRect.Height = 0;
    }

    private void OnCropCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        if (GetImageRect() is not { } imageRect) return;

        var current = Clamp(e.GetPosition(_cropCanvas), imageRect);

        var left = Math.Min(_dragStartCanvasPoint.X, current.X);
        var top = Math.Min(_dragStartCanvasPoint.Y, current.Y);
        var width = Math.Abs(current.X - _dragStartCanvasPoint.X);
        var height = Math.Abs(current.Y - _dragStartCanvasPoint.Y);

        // Only the VISUAL selection rectangle updates live during the drag -- the
        // ViewModel's CropRegionFraction is deliberately not touched here (see
        // BuildPreviewBitmap's doc comment for the bug this fixes): committing on
        // every pointer-move used to re-crop the live preview to a shrinking image
        // while the user was still mid-drag, corrupting the drag's own coordinate
        // math. The fraction is now computed and committed exactly once, in
        // OnCropCanvasPointerReleased below.
        Canvas.SetLeft(_selectionRect, left);
        Canvas.SetTop(_selectionRect, top);
        _selectionRect.Width = width;
        _selectionRect.Height = height;
    }

    private void OnCropCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        if (GetImageRect() is not { } imageRect) { _dragging = false; e.Pointer.Capture(null); return; }

        var current = Clamp(e.GetPosition(_cropCanvas), imageRect);
        var left = Math.Min(_dragStartCanvasPoint.X, current.X);
        var top = Math.Min(_dragStartCanvasPoint.Y, current.Y);
        var width = Math.Abs(current.X - _dragStartCanvasPoint.X);
        var height = Math.Abs(current.Y - _dragStartCanvasPoint.Y);

        _dragging = false;
        e.Pointer.Capture(null);

        // A plain click without a meaningful drag (a few pixels either way) reads as
        // "cancel the selection" rather than an accidental sliver-thin crop nobody
        // intended -- mirrors how most crop tools treat a near-zero-size drag.
        if (width < 4 || height < 4)
        {
            _selectionRect.IsVisible = false;
            ViewModel?.ResetCropCommand.Execute(null);
            return;
        }

        // Commit exactly once, now that the drag is finished and the image the user
        // was dragging against is guaranteed not to have changed size underneath them.
        PushFractionToViewModel(new Rect(left, top, width, height), imageRect);
    }

    private void PushFractionToViewModel(Rect selectionInCanvas, Rect imageRect)
    {
        if (ViewModel is not { } vm || _syncingFromViewModel) return;
        if (imageRect.Width <= 0 || imageRect.Height <= 0) return;

        var fractionX = (selectionInCanvas.X - imageRect.X) / imageRect.Width;
        var fractionY = (selectionInCanvas.Y - imageRect.Y) / imageRect.Height;
        var fractionW = selectionInCanvas.Width / imageRect.Width;
        var fractionH = selectionInCanvas.Height / imageRect.Height;

        vm.CropRegionFraction = new Rect(
            Math.Clamp(fractionX, 0, 1), Math.Clamp(fractionY, 0, 1),
            Math.Clamp(fractionW, 0, 1), Math.Clamp(fractionH, 0, 1));
    }

    private static Point Clamp(Point p, Rect bounds) => new(
        Math.Clamp(p.X, bounds.Left, bounds.Right),
        Math.Clamp(p.Y, bounds.Top, bounds.Bottom));

    /// <summary>Samples the single pixel under <paramref name="canvasPoint"/> from the
    /// currently displayed preview bitmap (what's actually on screen, rotate/flip/color
    /// adjustments and all) and writes it into BackgroundColorHex. Bitmap.CopyPixels
    /// always yields BGRA-ordered bytes into the destination buffer regardless of the
    /// source bitmap's own internal storage format -- same assumption ImageEditingService.
    /// ProcessPixels already relies on elsewhere in this codebase.</summary>
    private static void PickBackgroundColor(Point canvasPoint, Rect imageRect, ImageEditorDialogViewModel vm)
    {
        if (vm.PreviewImage is not Bitmap bitmap) return;
        if (imageRect.Width <= 0 || imageRect.Height <= 0) return;

        var fractionX = Math.Clamp((canvasPoint.X - imageRect.X) / imageRect.Width, 0, 1);
        var fractionY = Math.Clamp((canvasPoint.Y - imageRect.Y) / imageRect.Height, 0, 1);

        var pixelSize = bitmap.PixelSize;
        var px = Math.Min((int)(fractionX * pixelSize.Width), pixelSize.Width - 1);
        var py = Math.Min((int)(fractionY * pixelSize.Height), pixelSize.Height - 1);

        var pixelBuffer = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(px, py, 1, 1), pixelBuffer, 4, 4);
            var bytes = new byte[4];
            Marshal.Copy(pixelBuffer, bytes, 0, 4);

            var color = Color.FromRgb(bytes[2], bytes[1], bytes[0]); // BGRA -> RGB
            vm.BackgroundColorHex = color.ToString();
        }
        finally
        {
            Marshal.FreeHGlobal(pixelBuffer);
        }
    }
}
