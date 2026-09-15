using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using InfoID.Desktop.Features.CardDesigner.Services;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>Maps a DesignIssueSeverity to the small color dot shown next to each finding
/// in the Design Checker flyout (Part 79: "Show severity: Error/Warning/Info").</summary>
public sealed class SeverityToColorConverter : IValueConverter
{
    public static readonly SeverityToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value as DesignIssueSeverity? switch
        {
            DesignIssueSeverity.Error => Brushes.Crimson,
            DesignIssueSeverity.Warning => new SolidColorBrush(Color.Parse("#E8A33D")),
            DesignIssueSeverity.Info => new SolidColorBrush(Color.Parse("#4A90D9")),
            _ => Brushes.Gray,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
