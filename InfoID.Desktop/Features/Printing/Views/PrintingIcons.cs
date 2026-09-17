using Avalonia.Media;

namespace InfoID.Desktop.Features.Printing.Views;

/// <summary>Icon glyphs used only by the Print module -- kept separate from Card
/// Designer's own DesignerIcons.cs (same hand-authored-path pattern, own 24x24 grid) so
/// the Print module's assets stay self-contained. Same defensive-parse-with-fallback
/// approach as DesignerIcons.G -- an invalid path string degrades to a plain square
/// rather than taking down the dialog.</summary>
public static class PrintingIcons
{
    private static Geometry G(string data)
    {
        try { return Geometry.Parse(data); }
        catch { return Geometry.Parse("M4,4 L20,4 L20,20 L4,20 Z"); }
    }

    public static readonly Geometry Printer = G(
        "F0 M6,3 L18,3 L18,8 L6,8 Z M4,8 L20,8 L20,17 A1,1 0 0 1 19,18 L16,18 L16,21 L8,21 L8,18 L5,18 A1,1 0 0 1 4,17 Z " +
        "M8,14 L16,14 L16,20 L8,20 Z M17,10.5 A1,1 0 1 0 17.01,10.5 Z");

    public static readonly Geometry Refresh = G(
        "M12,4 A8,8 0 1 1 4.6,8.5 L4.6,8.5 L6.2,9.6 A6,6 0 1 0 12,6 L12,6 L12,9 L6,4.5 L12,0 Z");

    public static readonly Geometry Plus = G("M11,4 L13,4 L13,11 L20,11 L20,13 L13,13 L13,20 L11,20 L11,13 L4,13 L4,11 L11,11 Z");

    public static readonly Geometry Gear = G(
        "F0 M13.2,2 L14.4,4.4 A8,8 0 0 1 16.6,5.6 L19.2,4.9 L20.9,7.1 L19.1,9 A8,8 0 0 1 19.1,15 L20.9,16.9 " +
        "L19.2,19.1 L16.6,18.4 A8,8 0 0 1 14.4,19.6 L13.2,22 L10.8,22 L9.6,19.6 A8,8 0 0 1 7.4,18.4 L4.8,19.1 " +
        "L3.1,16.9 L4.9,15 A8,8 0 0 1 4.9,9 L3.1,7.1 L4.8,4.9 L7.4,5.6 A8,8 0 0 1 9.6,4.4 L10.8,2 Z " +
        "M12,8.5 A3.5,3.5 0 1 0 12.01,8.5 Z");

    public static readonly Geometry Trash = G(
        "M9,2 L15,2 L15,4 L20,4 L20,6 L4,6 L4,4 L9,4 Z M5,7 L19,7 L17.5,22 L6.5,22 Z");

    /// <summary>Small filled circle -- the online/offline status dot next to the
    /// selected printer, colored via the binding's own brush rather than baked in here.</summary>
    public static readonly Geometry StatusDot = G("M12,7 A5,5 0 1 0 12.01,7 Z");

    public static readonly Geometry SaveFile = G(
        "M4,2 L16,2 L20,6 L20,22 L4,22 Z M7,2 L7,8 L15,8 L15,2 Z M7,13 L17,13 L17,20 L7,20 Z");

    public static readonly Geometry ChevronLeft = G("M15,3 L8,12 L15,21 L17,19 L11.5,12 L17,5 Z");
    public static readonly Geometry ChevronRight = G("M9,3 L16,12 L9,21 L7,19 L12.5,12 L7,5 Z");

    public static readonly Geometry CheckCircle = G(
        "F0 M12,2 A10,10 0 1 0 12.01,2 Z M12,4.2 A7.8,7.8 0 1 1 11.99,4.2 Z " +
        "M10.2,16 L5.5,11.3 L7,9.8 L10.2,13 L17,6.2 L18.5,7.7 Z");

    public static readonly Geometry Rotate180 = G(
        "M12,4 A8,8 0 1 1 4.6,8.5 L6.2,9.6 A6,6 0 1 0 12,6 L12,9 L6,4.5 L12,0 Z " +
        "M12,20 A8,8 0 0 0 19.4,15.5 L17.8,14.4 A6,6 0 0 1 12,18 L12,15 L18,19.5 L12,24 Z");

    public static readonly Geometry Minus = G("M4,11 L20,11 L20,13 L4,13 Z");
}
