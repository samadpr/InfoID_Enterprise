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
}
