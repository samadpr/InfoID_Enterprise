using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>Converts a bool to a star-sized GridLength (true) or a zero-height GridLength
/// (false), for a RowDefinition.Height binding -- used to actually reclaim screen space
/// when the Properties or Layers panel is hidden (Part 24/75: "Allow show/hide
/// properties, show/hide layers"), rather than just leaving a blank gap the way toggling
/// a child's IsVisible alone would (the Grid row keeps its star-share of space
/// regardless of the content inside it being visible).</summary>
public sealed class BoolToStarOrZeroRowConverter : IValueConverter
{
    public static readonly BoolToStarOrZeroRowConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
