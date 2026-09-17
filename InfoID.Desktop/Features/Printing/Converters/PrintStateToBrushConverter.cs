using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using InfoID.Desktop.Features.Printing.Models;

namespace InfoID.Desktop.Features.Printing.Converters;

/// <summary>Background color for a record's status pill in the Advanced Print
/// Operations record list -- the "show properly completed prints" affordance.</summary>
public sealed class PrintStateToBrushConverter : IValueConverter
{
    public static readonly PrintStateToBrushConverter Instance = new();

    private static readonly IBrush NotPrinted = new SolidColorBrush(Color.Parse("#6B7280"));
    private static readonly IBrush Printed = new SolidColorBrush(Color.Parse("#2E8B57"));
    private static readonly IBrush Failed = new SolidColorBrush(Color.Parse("#C1272D"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        PrintRecordState.Printed => Printed,
        PrintRecordState.Failed => Failed,
        _ => NotPrinted,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
