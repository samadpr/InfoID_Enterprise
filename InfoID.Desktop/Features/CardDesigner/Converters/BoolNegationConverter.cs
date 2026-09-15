using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>Inverts a bool binding, e.g. IsVisible="{Binding IsRenaming, Converter=
/// {x:Static conv:BoolNegationConverter.Instance}}" to show the normal title while NOT
/// renaming. Used instead of Avalonia's "!Path" binding-negation shorthand so this stays
/// unambiguous and easy to grep for regardless of Avalonia version quirks.</summary>
public sealed class BoolNegationConverter : IValueConverter
{
    public static readonly BoolNegationConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : value;
}
