using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace InfoID.Desktop.Features.Printing.Converters;

/// <summary>The small status dot next to the selected printer -- green when the OS
/// reports it online, amber (not red: "offline" here just means "not currently
/// reporting ready", not necessarily broken) otherwise.</summary>
public sealed class BoolToOnlineBrushConverter : IValueConverter
{
    public static readonly BoolToOnlineBrushConverter Instance = new();

    private static readonly IBrush Online = new SolidColorBrush(Color.Parse("#2E8B57"));
    private static readonly IBrush Offline = new SolidColorBrush(Color.Parse("#D9A441"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Online : Offline;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
