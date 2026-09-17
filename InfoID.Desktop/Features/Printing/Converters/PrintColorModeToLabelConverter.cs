using System;
using System.Globalization;
using Avalonia.Data.Converters;
using InfoID.Desktop.Features.Printing.Models;

namespace InfoID.Desktop.Features.Printing.Converters;

/// <summary>Display text for the "Black" (rendering mode) dropdown, matching the
/// reference's exact wording.</summary>
public sealed class PrintColorModeToLabelConverter : IValueConverter
{
    public static readonly PrintColorModeToLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        PrintColorMode.MonoChrome => "Send as MonoChrome",
        PrintColorMode.Composite => "Send Composite",
        PrintColorMode.CompositeAndMonoChrome => "Send Composite and MonoChrome",
        _ => value?.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
