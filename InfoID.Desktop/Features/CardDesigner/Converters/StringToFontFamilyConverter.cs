using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>Converts a plain font-family name string (e.g. "Wingdings 3") into a real
/// Avalonia.Media.FontFamily instance for the Font family picker's per-item preview
/// (CardDesignerView.axaml's ComboBox.ItemTemplate) -- binding FontFamily="{Binding}"
/// directly against a string item relies on Avalonia's binding engine invoking
/// FontFamily's own TypeConverter, which didn't actually take effect for a plain
/// self-binding item in a DataTemplate with no x:DataType (every dropdown entry still
/// rendered in the same UI font instead of its own). Going through an explicit
/// converter that constructs `new FontFamily(name)` itself removes that ambiguity.</summary>
public sealed class StringToFontFamilyConverter : IValueConverter
{
    public static readonly StringToFontFamilyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || string.IsNullOrWhiteSpace(name)) return AvaloniaProperty.UnsetValue;

        try
        {
            return new FontFamily(name);
        }
        catch
        {
            // A malformed family name string should never crash the dropdown --
            // AvaloniaProperty.UnsetValue leaves FontFamily at whatever it would
            // otherwise inherit (the default UI font) for just that one item.
            return AvaloniaProperty.UnsetValue;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
