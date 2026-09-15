using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

public enum ImageFitMode { Fit, Fill, Stretch }

public sealed partial class ImageElement : DesignerElement
{
    public override ElementType ElementType => ElementType.Image;

    /// <summary>App-relative asset reference (never an absolute local path -- Part 51),
    /// resolved through the future IDesignAssetService.</summary>
    [ObservableProperty] private string? _assetReference;
    [ObservableProperty] private ImageFitMode _fitMode = ImageFitMode.Fit;
    [ObservableProperty] private double _cornerRadius;
    [ObservableProperty] private double _brightness;
    [ObservableProperty] private double _contrast;
    [ObservableProperty] private double _saturation;

    /// <summary>Only meaningful in Fill mode (Fit/Stretch always show the whole image,
    /// there's nothing to pan/zoom within). Same pan+zoom crop model as
    /// PhotoElement.CropX/CropY/CropZoom, and rendered with the exact same formula in
    /// CardCanvasView.DrawImage -- kept as separate fields on each element type rather
    /// than a shared base, matching how this codebase already keeps Image and Photo as
    /// distinct element types with their own property sets.</summary>
    [ObservableProperty] private double _cropX;
    [ObservableProperty] private double _cropY;
    [ObservableProperty] private double _cropZoom = 1.0;

    /// <summary>Clip shape (Part 91). Reuses PhotoMaskShape rather than a second,
    /// identical enum -- Rectangle/RoundedRectangle/Circle mean the exact same thing
    /// for either element type, and CardCanvasView already has the clip-geometry
    /// switch for it (DrawPhoto); DrawImage now shares that same switch.</summary>
    [ObservableProperty] private PhotoMaskShape _maskShape = PhotoMaskShape.Rectangle;

    /// <summary>Mirror-flips the image content in place (the element's own X/Y/Width/
    /// Height/Rotation are untouched -- this only flips what's drawn inside that box).
    /// Applied as a transform in CardCanvasView.DrawImage/ThumbnailRenderer, not a
    /// pixel-level operation, so it costs nothing extra to redraw and needs no
    /// caching.</summary>
    [ObservableProperty] private bool _flipHorizontal;
    [ObservableProperty] private bool _flipVertical;
}
