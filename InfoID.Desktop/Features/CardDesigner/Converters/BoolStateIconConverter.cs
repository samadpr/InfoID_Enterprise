using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using InfoID.Desktop.Features.CardDesigner.Views.Icons;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>Picks between two icon geometries for a bool, e.g. Visible -> Eye/EyeOff or
/// Locked -> Lock/Unlock (Part 19). ConverterParameter selects which pair: "Visibility"
/// or "Lock". One converter instead of two near-identical classes.</summary>
public sealed class BoolStateIconConverter : IValueConverter
{
    public static readonly BoolStateIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isOn = value is true;
        return parameter?.ToString() switch
        {
            "Lock" => isOn ? DesignerIcons.Lock : DesignerIcons.Unlock,
            _ => isOn ? DesignerIcons.Eye : DesignerIcons.EyeOff,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
