using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.Features.CardDesigner.Views.Icons;

namespace InfoID.Desktop.Features.CardDesigner.Converters;

/// <summary>Layers panel row icon for a LayerNode -- a folder glyph for a group header,
/// or the same per-element-type icon ElementTypeToIconConverter already resolves for a
/// leaf (delegated to, not duplicated, so a shape/text/image row's icon can never drift
/// between the two converters).</summary>
public sealed class LayerNodeToIconConverter : IValueConverter
{
    public static readonly LayerNodeToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        LayerNode { IsGroup: true } => DesignerIcons.Folder,
        LayerNode { Element: { } element } => ElementTypeToIconConverter.Instance.Convert(element, targetType, parameter, culture),
        _ => DesignerIcons.LayerGeneric,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
