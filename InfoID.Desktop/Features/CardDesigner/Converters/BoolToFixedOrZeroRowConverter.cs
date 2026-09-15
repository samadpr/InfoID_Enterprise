using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>Converts a bool to a fixed-pixel GridLength (true) or a zero-size GridLength
/// (false), for a RowDefinition.Height / ColumnDefinition.Width binding.
///
/// Priority 5 fix (Rulers toggle): the mm ruler row/column in CardDesignerView.axaml's
/// "CanvasHost" Grid previously had a hard-coded "20" size and the RulerView controls
/// inside them had no IsVisible binding at all -- toggling the "Rulers" toolbar button
/// changed ActiveTab.ShowRulers but nothing in the view ever consumed it, so the rulers
/// never actually hid (or reserved space) at all. This converter, combined with
/// IsVisible="{Binding ActiveTab.ShowRulers}" on the ruler controls themselves, makes the
/// row/column collapse to zero AND the ruler content stop rendering/hit-testing when
/// rulers are turned off, exactly mirroring how BoolToStarOrZeroRowConverter already
/// reclaims space for the Properties/Layers panels.
///
/// ConverterParameter is the pixel size to use when true (defaults to 20 if omitted or
/// unparsable, matching the ruler strip's existing fixed thickness).</summary>
public sealed class BoolToFixedOrZeroRowConverter : IValueConverter
{
    public static readonly BoolToFixedOrZeroRowConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true) return new GridLength(0);

        var size = 20d;
        if (parameter is not null)
        {
            var text = parameter as string ?? parameter.ToString();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                size = parsed;
            }
        }
        return new GridLength(size);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
