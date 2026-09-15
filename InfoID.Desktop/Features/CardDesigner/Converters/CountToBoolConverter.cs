using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>Converts an int count to a bool for IsVisible/IsEnabled bindings --
/// ObservableCollection.Count is an int, and Avalonia does not implicitly convert
/// int to bool the way some other binding engines do, so a direct
/// IsVisible="{Binding Items.Count}" binding would fail. ConverterParameter selects
/// which way: "Zero" (true when count == 0, e.g. an empty-state message) or the
/// default/omitted (true when count > 0, e.g. a badge or enabling a button).</summary>
public sealed class CountToBoolConverter : IValueConverter
{
    public static readonly CountToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        return parameter?.ToString() == "Zero" ? count == 0 : count > 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
