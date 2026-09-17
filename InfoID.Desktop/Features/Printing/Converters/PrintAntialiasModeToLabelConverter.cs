using System;
using System.Globalization;
using Avalonia.Data.Converters;
using InfoID.Desktop.Features.Printing.Models;

namespace InfoID.Desktop.Features.Printing.Converters;

/// <summary>Display text for the Antialiasing dropdown, matching the reference's exact
/// wording. See PrintAntialiasMode's own doc comment: only Yes/No are currently backed
/// by a real rendering difference.</summary>
public sealed class PrintAntialiasModeToLabelConverter : IValueConverter
{
    public static readonly PrintAntialiasModeToLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        PrintAntialiasMode.Yes => "Yes",
        PrintAntialiasMode.OnlyText => "Only Text",
        PrintAntialiasMode.OnlyImages => "Only Images",
        PrintAntialiasMode.No => "No",
        _ => value?.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
