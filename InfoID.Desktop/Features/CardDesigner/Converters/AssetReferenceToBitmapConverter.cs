using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using InfoID.Desktop.Features.CardDesigner.Services;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>Resolves an element's plain AssetReference string (e.g. "3f9a1c2b.png")
/// straight to a decoded Bitmap for direct XAML binding (Image.Source="{Binding
/// AssetReference, Converter=...}}") -- used by the Image Properties Clipping tab so
/// each mask-shape option can preview the element's OWN current picture instead of a
/// generic icon (Part 92).
///
/// Reaches IDesignAssetService via App.Services rather than constructor injection: a
/// value converter is a stateless singleton instantiated by the XAML infrastructure,
/// not something the DI container constructs, so there's no natural injection point --
/// same reasoning ShapeElement's OnFontFamilyChanged already uses to reach
/// RecentFontsTracker from a plain model class with no DI access of its own.
///
/// No caching: only ever bound a handful of times at once (the mask-shape picker has 5
/// tiles), and only re-evaluates when the selected element's AssetReference actually
/// changes, so the decode cost here is negligible.</summary>
public sealed class AssetReferenceToBitmapConverter : IValueConverter
{
    public static readonly AssetReferenceToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string assetReference || string.IsNullOrWhiteSpace(assetReference)) return null;

        try
        {
            var assetService = InfoID.Desktop.App.Services.GetService(typeof(IDesignAssetService)) as IDesignAssetService;
            var fullPath = assetService?.ResolveToFullPath(assetReference);
            return fullPath is not null ? new Bitmap(fullPath) : null;
        }
        catch
        {
            // Missing/corrupt asset file -- the preview just shows nothing, never a
            // crashed Properties panel.
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
