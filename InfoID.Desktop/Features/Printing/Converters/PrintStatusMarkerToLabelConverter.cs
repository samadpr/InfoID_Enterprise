using System;
using System.Globalization;
using Avalonia.Data.Converters;
using InfoID.Desktop.Features.Printing.Models;

namespace InfoID.Desktop.Features.Printing.Converters;

/// <summary>Display text for the Automatic Print Status Update marker dropdown,
/// matching the reference's $PRINTSTATUS/$PRINTCOUNTER/... wording. See
/// PrintStatusMarker's own doc comment for what each one actually does.</summary>
public sealed class PrintStatusMarkerToLabelConverter : IValueConverter
{
    public static readonly PrintStatusMarkerToLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        PrintStatusMarker.PrintStatus => "$PRINTSTATUS",
        PrintStatusMarker.PrintCounter => "$PRINTCOUNTER",
        PrintStatusMarker.PrintDate => "$PRINTDATE",
        PrintStatusMarker.PrintStatusAndCounter => "$PRINTSTATUS&COUNTER",
        PrintStatusMarker.PrintStatusAndCounterAndDate => "$PRINTSTATUS&COUNTER&DATE",
        _ => value?.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
