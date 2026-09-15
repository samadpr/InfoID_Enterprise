using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;

namespace InfoID.Desktop.Features.CardDesigner.ViewModels;

/// <summary>
/// Crop editor dialog (Part 38). Works on its own copy of CropX/CropY/CropZoom --
/// nothing is written back to the actual element until Apply, so Cancel genuinely
/// discards every change including ones made mid-drag, and the live canvas behind the
/// dialog never flickers with in-progress edits.
/// </summary>
public sealed partial class CropEditorViewModel : DialogViewModelBase<bool>
{
    /// <summary>Loaded here, once, rather than via a XAML value converter -- same
    /// reasoning as RecentCardItem.ThumbnailImage: keeps the View a plain
    /// {Binding SourceBitmap} with no converter/type-resolution surface. Null if the
    /// asset can't be resolved or the file can't be decoded; the crop surface just shows
    /// an empty dark area rather than crashing the dialog over a missing/corrupt image.</summary>
    public Bitmap? SourceBitmap { get; }

    /// <summary>Target element's Width/Height ratio, so the crop viewport in the editor
    /// is shaped like the box the image will actually sit in on the card.</summary>
    public double AspectRatio { get; }

    [ObservableProperty]
    private double _cropX;

    [ObservableProperty]
    private double _cropY;

    [ObservableProperty]
    private double _cropZoom;

    public CropEditorViewModel(string? imagePath, double aspectRatio, double initialCropX, double initialCropY, double initialCropZoom)
    {
        AspectRatio = aspectRatio;
        _cropX = initialCropX;
        _cropY = initialCropY;
        _cropZoom = initialCropZoom;

        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            try
            {
                SourceBitmap = new Bitmap(imagePath);
            }
            catch
            {
                SourceBitmap = null;
            }
        }
    }

    [RelayCommand]
    private void ResetCrop()
    {
        CropX = 0;
        CropY = 0;
        CropZoom = 1.0;
    }

    [RelayCommand]
    private void Apply() => RequestClose(true);

    [RelayCommand]
    private void Cancel() => RequestClose(false);
}
