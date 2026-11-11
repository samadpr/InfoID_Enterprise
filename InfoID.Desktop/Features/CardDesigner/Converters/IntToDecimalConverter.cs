using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>Binds an int property to NumericUpDown.Value (decimal?). An empty box
/// becomes 0 instead of a binding error, and decimals are rounded to whole numbers.</summary>
public sealed class IntToDecimalConverter : IValueConverter
{
    public static readonly IntToDecimalConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? (decimal)i : 0m;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal d ? (int)Math.Round(d, MidpointRounding.AwayFromZero) : 0;
}
