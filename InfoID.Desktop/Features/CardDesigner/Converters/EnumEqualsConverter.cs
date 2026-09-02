using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>Compares a bound enum value against the converter parameter (a string
/// matching the enum member name) -- used to highlight the active Front/Back/Both
/// toggle in the toolbar.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null &&
        string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}