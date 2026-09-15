using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.CardDesigner.Models;
using InfoID.Desktop.Features.CardDesigner.Services;

namespace InfoID.Desktop.Features.CardDesigner.ViewModels;

/// <summary>
/// Priority 12 (Add Image workflow / Image Editor), "enterprise" pass. Shown by
/// CardDesignTabViewModel after either "Browse Image" or "Take Photo" produces a
/// source, before anything is actually inserted onto the canvas -- "Only the final
/// edited image should be inserted into the canvas" per the brief. Hosted by
/// IDialogService like every other dialog in this codebase, same
/// DialogViewModelBase&lt;TResult&gt; pattern, no special-casing.
///
/// What's real in this pass: Crop, Rotate 90, Flip Horizontal/Vertical, Brightness,
/// Contrast, Saturation (all via ImageEditingService.AdjustColors -- real per-pixel
/// math now, not the earlier compositing approximation), Color Mode presets
/// (Grayscale/Sepia/Negative), and same-color Background Removal (color-distance
/// threshold against a user-chosen target color). See ImageEditingService's own doc
/// comment for the techniques and honest confidence level behind each.
///
/// Face Detection: runs automatically (fire-and-forget, off the UI thread) as soon as
/// the dialog is constructed, via IFaceDetectionService (the bundled offline UltraFace
/// ONNX model -- see FaceDetectionService's own doc comment). The service returns a raw,
/// tight box around just the face; this ViewModel pads it out into a head-and-shoulders
/// crop using the four user-adjustable Margin*Percent properties (each "how much extra
/// room beyond the face box's own size, on that side" -- see ApplyMarginsToFaceBox) and
/// writes the result to CropRegionFraction, same as a manual drag selection would.
/// Changing any margin live-recomputes the crop against the last detected face, so the
/// user can dial in the framing without re-running detection. "Cut Image" then commits
/// that crop into the working image immediately (see CutImage), separate from Done()'s
/// final export -- this lets a user detect/crop a face and keep editing (color, etc.)
/// against the now-cropped result, or run detection again against it.
/// </summary>
public sealed partial class ImageEditorDialogViewModel : DialogViewModelBase<ImageEditorResult>
{
    /// <summary>Shows "InfoID Image Editor" in the hosted window's own title bar
    /// (which already has real, native minimize/close controls -- DialogService hosts
    /// every dialog in an actual OS Window) instead of the generic "InfoID" every
    /// other dialog still shows by default.</summary>
    public override string Title => "InfoID Image Editor";

    /// <summary>Resizable/maximizable -- see DialogViewModelBase.CanResize's own doc
    /// comment for why: a fixed-size dialog with a lot of content (preview + a full
    /// tool panel) risks pushing the footer buttons off-screen on a smaller display,
    /// which is exactly what was reported. The tabbed layout in the View also reduces
    /// how much height is needed in the first place, but resizability is the real
    /// safety net regardless of screen size.</summary>
    public override bool CanResize => true;

    /// <summary>Generous, explicit initial size -- see DialogViewModelBase.
    /// PreferredSize's own doc comment for the bug this fixes (the previous cramped/
    /// clipped layout you reported was the Window itself opening too small, not
    /// anything wrong with the tab layout). Still resizable/maximizable beyond this
    /// via CanResize above if a user wants more room.</summary>
    public override (double Width, double Height)? PreferredSize => (1100, 760);

    private readonly IImageEditingService _imageEditingService;
    private readonly IFaceDetectionService _faceDetectionService;

    /// <summary>The untouched source this dialog was opened with -- never reassigned,
    /// so Reset() can always get back to it even after a "Cut Image" has replaced
    /// _workingBaseBitmap.</summary>
    private readonly Bitmap _trueOriginalBitmap;

    /// <summary>What Rotate/Flip/"Cut Image" build on top of. Starts equal to
    /// _trueOriginalBitmap; "Cut Image" (see CutImage below) folds the current crop
    /// selection into a new value here and resets rotation/flip/crop state back to
    /// identity, the same way Rotate/Flip already treat _rotationQuarters/_flipX as
    /// "applied to the true original from scratch every rebuild" -- Cut Image just
    /// moves the base those apply to, rather than being one more transient step
    /// recomputed on every preview rebuild the way crop-before-Cut is.</summary>
    private Bitmap _workingBaseBitmap;

    public ImageEditorDialogViewModel(Bitmap sourceBitmap, IImageEditingService imageEditingService, IFaceDetectionService faceDetectionService)
    {
        _imageEditingService = imageEditingService;
        _faceDetectionService = faceDetectionService;
        _trueOriginalBitmap = sourceBitmap;
        _workingBaseBitmap = sourceBitmap;
        RebuildPreview();
        _ = DetectFace();
    }

    // ------------------------------------------------------------- edit state ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewSizeText))]
    private IImage? _previewImage;

    /// <summary>"Size: W x H px" for the current working image -- shown above the
    /// preview. Genuinely reflects the real preview bitmap; there's no zoom/fit
    /// percentage to show alongside it since the preview always fits the panel
    /// (Stretch="Uniform") rather than supporting an actual zoom level.</summary>
    public string PreviewSizeText => PreviewImage is { } img
        ? $"Size: {(int)Math.Round(img.Size.Width)} × {(int)Math.Round(img.Size.Height)} px"
        : string.Empty;

    /// <summary>0 = no rotation, 1 = 90° CW, 2 = 180°, 3 = 270° CW (i.e. 90° CCW).
    /// Always rebuilt from _workingBaseBitmap rather than repeatedly rotating an
    /// already-rotated bitmap, so repeated rotate clicks never accumulate quality
    /// loss.</summary>
    private int _rotationQuarters;

    private bool _flipHorizontal;
    private bool _flipVertical;

    /// <summary>Crop selection as 0..1 fractions of the CURRENT (post-rotate/flip)
    /// working image -- (0,0,1,1) means "no crop, whole image". Reset to full-image
    /// whenever rotation/flip changes since a selection made against one orientation/
    /// aspect ratio doesn't carry a meaningful position once the image's own
    /// dimensions change underneath it. Deliberately excluded from the live preview --
    /// see BuildPreviewBitmap's doc comment.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCropSelection))]
    private Rect _cropRegionFraction = new(0, 0, 1, 1);

    /// <summary>True once CropRegionFraction is anything other than the whole image --
    /// drives whether "Cut Image" is enabled, regardless of whether that selection came
    /// from a manual drag (Custom tab) or from face detection + margins.</summary>
    public bool HasCropSelection => CropRegionFraction != new Rect(0, 0, 1, 1);

    [ObservableProperty]
    private double _brightnessPercent;

    [ObservableProperty]
    private double _contrastPercent;

    [ObservableProperty]
    private double _saturationPercent;

    [ObservableProperty]
    private ImageColorMode _selectedColorMode = ImageColorMode.Color;

    [ObservableProperty]
    private bool _isBackgroundRemovalEnabled;

    /// <summary>Target color to remove, as a hex string -- bound directly to the same
    /// ColorPickerField control used everywhere else in this codebase (Fill/Stroke/
    /// Text color), so background removal gets the exact same "click a swatch, pick a
    /// color" UX as everything else rather than a bespoke picker.</summary>
    [ObservableProperty]
    private string _backgroundColorHex = "#FFFFFF";

    [ObservableProperty]
    private double _backgroundTolerancePercent = 20;

    /// <summary>Eyedropper mode for BackgroundColorHex -- while true, the next click on
    /// the preview image (handled in the View's code-behind, which has the pixel data)
    /// samples that pixel's color into BackgroundColorHex instead of starting a crop
    /// drag, then turns itself back off. Lets the user pick the actual backdrop color
    /// directly off the photo instead of guessing a hex value.</summary>
    [ObservableProperty]
    private bool _isPickingBackgroundColor;

    partial void OnBrightnessPercentChanged(double value) => RebuildPreview();
    partial void OnContrastPercentChanged(double value) => RebuildPreview();
    partial void OnSaturationPercentChanged(double value) => RebuildPreview();
    partial void OnBackgroundColorHexChanged(string value) => RebuildPreview();
    partial void OnBackgroundTolerancePercentChanged(double value) => RebuildPreview();

    partial void OnSelectedColorModeChanged(ImageColorMode value) => RebuildPreview();

    partial void OnIsBackgroundRemovalEnabledChanged(bool value) => RebuildPreview();

    // ---------------------------------------------------------------- face detection ----

    public bool IsFaceDetectionAvailable => true;

    [ObservableProperty]
    private bool _isDetectingFace;

    /// <summary>Human-readable outcome of the last detection attempt (or null before the
    /// first one completes) -- shown under the "Detect Face" button in the View.</summary>
    [ObservableProperty]
    private string? _faceDetectionStatusMessage;

    /// <summary>The raw (unpadded) face box from the last successful detection, in the
    /// same 0..1-fraction-of-current-working-image convention as CropRegionFraction --
    /// null before the first detection or after one that found nothing. Drives the
    /// View's second, informational overlay rectangle (the tight face outline, distinct
    /// from the padded CropRegionFraction selection) and is what ApplyMarginsToFaceBox
    /// re-pads every time a Margin*Percent changes, so adjusting margins never requires
    /// re-running the model.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectedFace))]
    private Rect? _detectedFaceBoxFraction;

    public bool HasDetectedFace => DetectedFaceBoxFraction is not null;

    /// <summary>How much extra room to keep beyond the detected face box's own
    /// width/height on each side, as a percentage of that box's size -- e.g. 50 on
    /// MarginTopPercent means "add half the face box's own height above it". Defaults
    /// match a natural head-and-shoulders ID-photo framing. Purely additive to the raw
    /// DetectedFaceBoxFraction -- never re-runs detection, just re-pads it.</summary>
    [ObservableProperty]
    private decimal _marginTopPercent = 50;

    [ObservableProperty]
    private decimal _marginLeftPercent = 50;

    [ObservableProperty]
    private decimal _marginRightPercent = 50;

    [ObservableProperty]
    private decimal _marginBottomPercent = 50;

    partial void OnMarginTopPercentChanged(decimal value) => ApplyMarginsToFaceBox();
    partial void OnMarginLeftPercentChanged(decimal value) => ApplyMarginsToFaceBox();
    partial void OnMarginRightPercentChanged(decimal value) => ApplyMarginsToFaceBox();
    partial void OnMarginBottomPercentChanged(decimal value) => ApplyMarginsToFaceBox();

    /// <summary>Backs the Margins panel's "-"/"+" buttons -- CommandParameter is
    /// "Top"/"Left"/"Right"/"Bottom" combined with a signed step, e.g. "Top:10" or
    /// "Top:-10". A plain custom stepper (Border + TextBlock + two Buttons) rather than
    /// Avalonia's built-in NumericUpDown, which rendered with its text unreadable in
    /// this app's theme -- this way the displayed value is an ordinary TextBlock, using
    /// the same text rendering already proven correct everywhere else in this dialog.</summary>
    [RelayCommand]
    private void AdjustMargin(string sideAndStep)
    {
        var parts = sideAndStep.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var step)) return;

        switch (parts[0])
        {
            case "Top": MarginTopPercent = ClampMarginPercent(MarginTopPercent + step); break;
            case "Left": MarginLeftPercent = ClampMarginPercent(MarginLeftPercent + step); break;
            case "Right": MarginRightPercent = ClampMarginPercent(MarginRightPercent + step); break;
            case "Bottom": MarginBottomPercent = ClampMarginPercent(MarginBottomPercent + step); break;
        }
    }

    private static decimal ClampMarginPercent(decimal value) => Math.Clamp(value, 0, 200);

    [RelayCommand]
    private async Task DetectFace()
    {
        IsDetectingFace = true;
        FaceDetectionStatusMessage = "Detecting face...";

        try
        {
            DetectedFaceBoxFraction = await _faceDetectionService.DetectFaceAsync(BuildRotatedFlippedBitmap());

            if (DetectedFaceBoxFraction is not null)
            {
                ApplyMarginsToFaceBox();
                FaceDetectionStatusMessage = "Face detected -- adjust margins below, then Cut Image.";
            }
            else
            {
                FaceDetectionStatusMessage = "No face detected. Use the Custom tab to crop manually.";
            }
        }
        catch (Exception)
        {
            // Never let a detection failure surface as a crashed dialog -- the manual
            // crop tool is always still right there as a fallback.
            DetectedFaceBoxFraction = null;
            FaceDetectionStatusMessage = "Face detection couldn't run. Use the Custom tab to crop manually.";
        }
        finally
        {
            IsDetectingFace = false;
        }
    }

    /// <summary>Re-pads the last detected face box using the current Margin*Percent
    /// values and writes the result to CropRegionFraction. A no-op if nothing has been
    /// detected yet.</summary>
    private void ApplyMarginsToFaceBox()
    {
        if (DetectedFaceBoxFraction is not { } face) return;

        var left = Math.Clamp(face.X - face.Width * (double)MarginLeftPercent / 100.0, 0, 1);
        var top = Math.Clamp(face.Y - face.Height * (double)MarginTopPercent / 100.0, 0, 1);
        var right = Math.Clamp(face.X + face.Width + face.Width * (double)MarginRightPercent / 100.0, 0, 1);
        var bottom = Math.Clamp(face.Y + face.Height + face.Height * (double)MarginBottomPercent / 100.0, 0, 1);

        if (right > left && bottom > top)
        {
            CropRegionFraction = new Rect(left, top, right - left, bottom - top);
        }
    }

    // ------------------------------------------------------------------ commands ----

    [RelayCommand]
    private void RotateLeft()
    {
        _rotationQuarters = (_rotationQuarters + 3) % 4;
        ClearCropAndFaceState();
        RebuildPreview();
    }

    [RelayCommand]
    private void RotateRight()
    {
        _rotationQuarters = (_rotationQuarters + 1) % 4;
        ClearCropAndFaceState();
        RebuildPreview();
    }

    [RelayCommand]
    private void ToggleFlipHorizontal()
    {
        _flipHorizontal = !_flipHorizontal;
        ClearCropAndFaceState();
        RebuildPreview();
    }

    [RelayCommand]
    private void ToggleFlipVertical()
    {
        _flipVertical = !_flipVertical;
        ClearCropAndFaceState();
        RebuildPreview();
    }

    /// <summary>No RebuildPreview() call here -- PreviewImage deliberately excludes the
    /// crop step (see BuildPreviewBitmap's doc comment), so a CropRegionFraction change
    /// has nothing to rebuild in the preview itself. The code-behind still reacts to
    /// this property changing (via the ViewModel's PropertyChanged event) to reposition
    /// the overlay selection rectangle, independent of this method.</summary>
    partial void OnCropRegionFractionChanged(Rect value) { }

    [RelayCommand]
    private void ResetCrop() => CropRegionFraction = new Rect(0, 0, 1, 1);

    /// <summary>Commits the current CropRegionFraction into the working image right
    /// now, rather than waiting for Done(). Afterwards the crop selection, the detected
    /// face box, and rotation/flip all reset to identity -- they're already baked into
    /// the new _workingBaseBitmap, so re-applying them on the next rebuild would double
    /// them up. A no-op if nothing is selected (HasCropSelection is false, which also
    /// keeps the View's button disabled).</summary>
    [RelayCommand]
    private void CutImage()
    {
        if (!HasCropSelection) return;

        _workingBaseBitmap = _imageEditingService.Crop(BuildRotatedFlippedBitmap(), CropRegionFraction);

        _rotationQuarters = 0;
        _flipHorizontal = false;
        _flipVertical = false;
        ClearCropAndFaceState();
        RebuildPreview();
    }

    private void ClearCropAndFaceState()
    {
        CropRegionFraction = new Rect(0, 0, 1, 1);
        DetectedFaceBoxFraction = null;
        FaceDetectionStatusMessage = null;
    }

    [RelayCommand]
    private void Reset()
    {
        _workingBaseBitmap = _trueOriginalBitmap;
        _rotationQuarters = 0;
        _flipHorizontal = false;
        _flipVertical = false;
        ClearCropAndFaceState();
        BrightnessPercent = 0;
        ContrastPercent = 0;
        SaturationPercent = 0;
        SelectedColorMode = ImageColorMode.Color;
        IsBackgroundRemovalEnabled = false;
        RebuildPreview();
    }

    [RelayCommand]
    private void Cancel() => RequestClose(null);

    [RelayCommand]
    private async Task Done()
    {
        var final = BuildEditedBitmap();
        var path = await _imageEditingService.SaveTempPngAsync(final);
        RequestClose(new ImageEditorResult { EditedFilePath = path });
    }

    // ------------------------------------------------------------------- pipeline ----

    /// <summary>Applies rotate -> flip -> crop -> color adjustments -> color mode ->
    /// background removal, in that fixed order, to a fresh copy of _workingBaseBitmap
    /// every time -- never to an already-edited result -- so undo-by-adjusting-a-
    /// slider-back-to-0 is always exact, and so repeated rotate/crop clicks never
    /// compound quality loss the way editing an already-edited bitmap repeatedly
    /// would. Used only by Done() -- the actual final exported image. See
    /// BuildPreviewBitmap below for why the live PreviewImage deliberately excludes
    /// the crop step specifically.</summary>
    private Bitmap BuildEditedBitmap()
    {
        var working = BuildRotatedFlippedBitmap();

        if (HasCropSelection)
        {
            working = _imageEditingService.Crop(working, CropRegionFraction);
        }

        return ApplyColorAdjustments(working);
    }

    /// <summary>What PreviewImage actually shows: rotate/flip/color-adjustments/color-
    /// mode/background-removal applied, but crop deliberately NOT applied -- the crop
    /// selection is shown as a separate overlay rectangle on top of the full image
    /// instead (see ImageEditorDialogView.axaml.cs), the same way essentially every
    /// real crop tool works (Windows Photos, Instagram, ...): you always see the whole
    /// picture with a draggable selection box, not a live-shrinking preview.
    ///
    /// This split fixes a real, reported bug: the crop drag interaction used to commit
    /// the crop (and therefore rebuild the preview around an already-cropped, smaller
    /// bitmap) on every single pointer-move event during the drag, which changed the
    /// image's own on-screen size mid-drag and corrupted the drag's own coordinate
    /// math. Crop is now committed to CropRegionFraction exactly once, on
    /// pointer-release (see the View's code-behind) or by CutImage above, and the
    /// preview never changes size mid-drag because it never reflects the crop at all --
    /// only Done()'s BuildEditedBitmap and CutImage itself actually cut pixels.</summary>
    private Bitmap BuildPreviewBitmap()
    {
        var working = BuildRotatedFlippedBitmap();
        return ApplyColorAdjustments(working);
    }

    private Bitmap BuildRotatedFlippedBitmap()
    {
        Bitmap working = _workingBaseBitmap;

        for (var i = 0; i < _rotationQuarters; i++)
        {
            working = _imageEditingService.Rotate90(working, clockwise: true);
        }

        if (_flipHorizontal) working = _imageEditingService.FlipHorizontal(working);
        if (_flipVertical) working = _imageEditingService.FlipVertical(working);

        return working;
    }

    private Bitmap ApplyColorAdjustments(Bitmap working)
    {
        if (BrightnessPercent != 0 || ContrastPercent != 0 || SaturationPercent != 0)
        {
            working = _imageEditingService.AdjustColors(working, BrightnessPercent, ContrastPercent, SaturationPercent);
        }

        if (SelectedColorMode != ImageColorMode.Color)
        {
            working = _imageEditingService.ApplyColorMode(working, SelectedColorMode);
        }

        if (IsBackgroundRemovalEnabled)
        {
            try
            {
                var color = Color.Parse(BackgroundColorHex);
                working = _imageEditingService.RemoveBackgroundColor(working, color, BackgroundTolerancePercent);
            }
            catch (Exception)
            {
                // Invalid/unparsable hex mid-edit -- skip background removal for this
                // rebuild rather than crash the dialog over a transient bad value.
            }
        }

        return working;
    }

    private void RebuildPreview()
    {
        try
        {
            PreviewImage = BuildPreviewBitmap();
        }
        catch (Exception)
        {
            // A bad intermediate state must never crash the dialog -- fall back to
            // showing the last known-good working image rather than leaving
            // PreviewImage stuck on a stale frame or throwing out of a property setter.
            PreviewImage = _workingBaseBitmap;
        }
    }
}
