using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>Two-way version of EnumEqualsConverter: lets a ToggleButton/RadioButton's
/// IsChecked bind directly to one member of an enum property, so a row of buttons acts
/// like a radio group without any extra command plumbing in the ViewModel. Used for the
/// Text-alignment / Vertical-alignment property-panel controls (Part 1), which must stay
/// clearly separate from the object-alignment toolbar buttons.</summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null &&
        string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null) return Avalonia.Data.BindingOperations.DoNothing;
        return Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true);
    }
}
