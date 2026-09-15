using Avalonia.Media;

namespace InfoID.Desktop.Features.CardDesigner.Views.Icons;

/// <summary>
/// Single source of truth for every icon glyph used by the Card Designer's toolbar,
/// left tool rail, shapes flyout and Layers panel (Part 9/10/19 of the redesign brief).
///
/// Why plain <see cref="Geometry"/> constants instead of a NuGet icon package: the
/// project's package-management rule says not to add a dependency "without explicit
/// confirmation", and Avalonia's built-in <c>PathIcon</c> + <c>StreamGeometry</c> mini
/// language already cover this need with zero extra dependencies and full cross-platform
/// consistency (Part 77). Every icon lives on a 24x24 design grid so they can share a
/// single <c>PathIcon</c> Width/Height in XAML.
///
/// Consumed two ways:
///  - Directly in XAML via <c>{x:Static icons:DesignerIcons.Undo}</c> for static buttons.
///  - Through the small value converters in Features/CardDesigner/Converters (e.g.
///    LayerElementTypeToIconConverter, LayerVisibleIconConverter) for icons that depend
///    on a bound value (element type, visibility, lock state) so there is exactly one
///    place each glyph's path data is authored -- no XAML/converter duplication.
///
/// These are original, hand-authored simple silhouettes, not a copied icon set.
/// </summary>
public static class DesignerIcons
{
    /// <summary>Parses one icon's path data defensively. These fields are static
    /// readonly, so they run the moment this class is first touched -- if any hand-
    /// authored path string turns out to be invalid mini-language syntax, this must not
    /// take the whole Card Designer (or the app) down with a static-constructor
    /// exception. Falls back to a plain filled square (still a visible, clickable-looking
    /// icon, just not the intended glyph) rather than crashing.</summary>
    private static Geometry G(string data)
    {
        try
        {
            return Geometry.Parse(data);
        }
        catch
        {
            return Geometry.Parse("M4,4 L20,4 L20,20 L4,20 Z");
        }
    }

    // --- Tool modes -------------------------------------------------------
    public static readonly Geometry Select = G("M5,3 L5,19.5 L9,15.3 L11.6,20.8 L14.1,19.6 L11.5,14.1 L17,13.7 Z");
    public static readonly Geometry Pan = G(
        "M9,3 L11,3 L11,12 L9,12 Z " +
        "M12,2 L14,2 L14,12 L12,12 Z " +
        "M15,3 L17,3 L17,12 L15,12 Z " +
        "M6,9 L8,7.5 L8,12.5 L6,13.5 Z " +
        "M6,12 L18,12 L18,19 A3,3 0 0 1 15,22 L10,22 A4,4 0 0 1 6,18 Z");

    // --- History ------------------------------------------------------------
    public static readonly Geometry Undo = G("M9,5 L9,1 L2,8 L9,15 L9,11 C15,11 19,14.5 18,20 C21,15 19,5 9,5 Z");
    public static readonly Geometry Redo = G("M15,5 L15,1 L22,8 L15,15 L15,11 C9,11 5,14.5 6,20 C3,15 5,5 15,5 Z");

    // --- Navigation chevrons (preview record stepper) ------------------------
    public static readonly Geometry ChevronLeft = G("M15,3 L8,12 L15,21 L17,19 L11.5,12 L17,5 Z");
    public static readonly Geometry ChevronRight = G("M9,3 L16,12 L9,21 L7,19 L12.5,12 L7,5 Z");

    // --- Clipboard / edit -----------------------------------------------------
    public static readonly Geometry Delete = G(
        "M9,2 L15,2 L15,4 L20,4 L20,6 L4,6 L4,4 L9,4 Z " +
        "M5,7 L19,7 L17.5,22 L6.5,22 Z");
    public static readonly Geometry Copy = G("M8,3 L19,3 L19,14 L8,14 Z M4,8 L15,8 L15,19 L4,19 Z");
    public static readonly Geometry Paste = G("M5,3 L19,3 L19,22 L5,22 Z M9,1 L15,1 L15,4 L9,4 Z");
    public static readonly Geometry Duplicate = G("M9,3 L20,3 L20,14 L9,14 Z M4,9 L15,9 L15,20 L4,20 Z");

    // --- Grouping -----------------------------------------------------------
    public static readonly Geometry Group = G(
        "M3,3 L9,3 L9,9 L3,9 Z M15,3 L21,3 L21,9 L15,9 Z " +
        "M3,15 L9,15 L9,21 L3,21 Z M15,15 L21,15 L21,21 L15,21 Z");
    public static readonly Geometry Ungroup = G("M3,3 L11,3 L11,11 L3,11 Z M13,13 L21,13 L21,21 L13,21 Z");

    // --- Object alignment (distinct from text/paragraph alignment) ------------
    public static readonly Geometry AlignLeft = G("M2,2 L4,2 L4,22 L2,22 Z M6,5 L18,5 L18,9 L6,9 Z M6,13 L14,13 L14,17 L6,17 Z");
    public static readonly Geometry AlignRight = G("M22,2 L20,2 L20,22 L22,22 Z M18,5 L6,5 L6,9 L18,9 Z M18,13 L10,13 L10,17 L18,17 Z");
    public static readonly Geometry AlignCenterHorizontal = G("M11,2 L13,2 L13,22 L11,22 Z M4,5 L20,5 L20,9 L4,9 Z M7,13 L17,13 L17,17 L7,17 Z");
    public static readonly Geometry AlignTop = G("M2,2 L22,2 L22,4 L2,4 Z M5,6 L9,6 L9,18 L5,18 Z M13,6 L17,6 L17,14 L13,14 Z");
    public static readonly Geometry AlignBottom = G("M2,22 L22,22 L22,20 L2,20 Z M5,18 L9,18 L9,6 L5,6 Z M13,18 L17,18 L17,10 L13,10 Z");
    public static readonly Geometry AlignMiddleVertical = G("M2,11 L22,11 L22,13 L2,13 Z M5,4 L9,4 L9,20 L5,20 Z M13,7 L17,7 L17,17 L13,17 Z");

    // --- Distribute (Part 18/34) ------------------------------------------------
    public static readonly Geometry DistributeHorizontal = G(
        "M2,9 L6,9 L6,15 L2,15 Z M10,9 L14,9 L14,15 L10,15 Z M18,9 L22,9 L22,15 L18,15 Z");
    public static readonly Geometry DistributeVertical = G(
        "M9,2 L15,2 L15,6 L9,6 Z M9,10 L15,10 L15,14 L9,14 Z M9,18 L15,18 L15,22 L9,22 Z");

    // --- Z-order --------------------------------------------------------------
    public static readonly Geometry BringToFront = G("M4,10 L16,10 L16,22 L4,22 Z M10,2 L15,8 L11.5,8 L11.5,14 L8.5,14 L8.5,8 L5,8 Z");
    public static readonly Geometry SendToBack = G("M8,2 L20,2 L20,14 L8,14 Z M14,22 L9,16 L12.5,16 L12.5,10 L15.5,10 L15.5,16 L19,16 Z");

    // --- Card side switcher -----------------------------------------------------
    public static readonly Geometry CardFront = G(
        "F0 M3,5 L21,5 L21,19 L3,19 Z M5,7 L19,7 L19,17 L5,17 Z M6,9 A1.5,1.5 0 1 0 6.01,9 Z");
    public static readonly Geometry CardBack = G(
        "F0 M3,5 L21,5 L21,19 L3,19 Z M5,7 L19,7 L19,17 L5,17 Z " +
        "M6,9 L18,9 L18,10.5 L6,10.5 Z M6,12 L18,12 L18,13.5 L6,13.5 Z M6,15 L14,15 L14,16.5 L6,16.5 Z");
    public static readonly Geometry CardBoth = G("M2,3 L16,3 L16,15 L2,15 Z M8,9 L22,9 L22,21 L8,21 Z");

    // --- Zoom -------------------------------------------------------------------
    public static readonly Geometry ZoomIn = G(
        "F0 M11,2 L11,20 A9,9 0 1 1 11.01,20 Z M11,5 L11,17 A6,6 0 1 1 11.01,17 Z " +
        "M17,18 L23,24 L24,23 L18,17 Z M8,10 L14,10 L14,12 L8,12 Z M10,8 L12,8 L12,14 L10,14 Z");
    public static readonly Geometry ZoomOut = G(
        "F0 M11,2 L11,20 A9,9 0 1 1 11.01,20 Z M11,5 L11,17 A6,6 0 1 1 11.01,17 Z " +
        "M17,18 L23,24 L24,23 L18,17 Z M8,10 L14,10 L14,12 L8,12 Z");
    public static readonly Geometry ZoomReset = G(
        "M4,4 L10,4 L10,6 L6,6 L6,10 L4,10 Z M14,4 L20,4 L20,10 L18,10 L18,6 L14,6 Z " +
        "M4,14 L6,14 L6,18 L10,18 L10,20 L4,20 Z M18,14 L20,14 L20,20 L14,20 L14,18 L18,18 Z");

    // --- Left rail insert tools ---------------------------------------------------
    public static readonly Geometry Text = G("M4,4 L20,4 L20,7 L13,7 L13,20 L11,20 L11,7 L4,7 Z");
    public static readonly Geometry Shapes = G("M3,13 L11,13 L11,21 L3,21 Z M13,3 A5,5 0 1 0 13.01,3 Z");
    public static readonly Geometry Rectangle = G("F0 M3,6 L21,6 L21,18 L3,18 Z M5.5,8.5 L18.5,8.5 L18.5,15.5 L5.5,15.5 Z");
    public static readonly Geometry Ellipse = G("F0 M12,3 A9,9 0 1 0 12.01,3 Z M12,6 A6,6 0 1 0 12.01,6 Z");
    public static readonly Geometry Line = G("M3,19 L19,3 L21,5 L5,21 Z");
    public static readonly Geometry Arrow = G("M2,13 L15,13 L15,8 L22,12 L15,16 L15,11 L2,11 Z");
    public static readonly Geometry Triangle = G("F0 M12,3 L22,20 L2,20 Z M12,8 L18,17.5 L6,17.5 Z");
    public static readonly Geometry Polygon = G("F0 M12,2 L21,8 L18,19 L6,19 L3,8 Z M12,6 L17.5,10 L15.5,17 L8.5,17 L6.5,10 Z");
    public static readonly Geometry Star = G("M12,2 L15,9 L22,9.5 L16.5,14 L18.5,21 L12,17 L5.5,21 L7.5,14 L2,9.5 L9,9 Z");

    // --- Extended shape catalog (Shapes flyout, Part 90) -------------------------
    public static readonly Geometry RoundedRectangle = G(
        "F0 M7,4 L17,4 A5,5 0 0 1 22,9 L22,15 A5,5 0 0 1 17,20 L7,20 A5,5 0 0 1 2,15 L2,9 A5,5 0 0 1 7,4 Z " +
        "M8,7.5 L16,7.5 A2.5,2.5 0 0 1 18.5,10 L18.5,14 A2.5,2.5 0 0 1 16,16.5 L8,16.5 A2.5,2.5 0 0 1 5.5,14 L5.5,10 A2.5,2.5 0 0 1 8,7.5 Z");
    public static readonly Geometry Circle = G("F0 M12,3 A9,9 0 1 0 12.01,3 Z M12,6.5 A5.5,5.5 0 1 0 12.01,6.5 Z");
    public static readonly Geometry RightTriangle = G("F0 M4,3 L4,21 L21,21 Z M6.5,7 L6.5,18.5 L18,18.5 Z");
    public static readonly Geometry Diamond = G("F0 M12,2 L22,12 L12,22 L2,12 Z M12,7 L17,12 L12,17 L7,12 Z");
    public static readonly Geometry Parallelogram = G("F0 M9,4 L22,4 L15,20 L2,20 Z M10.8,7.5 L18.4,7.5 L13.7,16.5 L6.1,16.5 Z");
    public static readonly Geometry Pentagon = G("F0 M12,2 L22,9.2 L18.1,21 L5.9,21 L2,9.2 Z M12,6.5 L18,11 L15.7,17.8 L8.3,17.8 L6,11 Z");
    public static readonly Geometry Hexagon = G("F0 M6,3.5 L18,3.5 L23,12 L18,20.5 L6,20.5 L1,12 Z M8,7 L16,7 L19,12 L16,17 L8,17 L5,12 Z");
    public static readonly Geometry Heptagon = G(
        "F0 M12,2 L19.6,5.4 L21.9,13.4 L17,20.1 L7,20.1 L2.1,13.4 L4.4,5.4 Z " +
        "M12,6.3 L16.4,8.3 L17.7,13 L14.7,17 L9.3,17 L6.3,13 L7.6,8.3 Z");
    public static readonly Geometry Octagon = G(
        "F0 M8,2 L16,2 L22,8 L22,16 L16,22 L8,22 L2,16 L2,8 Z " +
        "M9.2,6 L14.8,6 L18,9.2 L18,14.8 L14.8,18 L9.2,18 L6,14.8 L6,9.2 Z");
    public static readonly Geometry DoubleArrow = G("M2,12 L6,7 L6,10.5 L18,10.5 L18,7 L22,12 L18,17 L18,13.5 L6,13.5 L6,17 Z");
    public static readonly Geometry Burst = G(
        "M12,2 L13.6,6.6 L17.5,3.8 L16.8,8.6 L21.6,8 L18.7,11.9 L23,14 L18.2,15.2 L20.8,19.4 " +
        "L16,18.3 L16.3,23.1 L12.6,19.9 L10.3,24 L9.1,19.3 L5.2,22.1 L5.9,17.3 L1.1,17.9 L4,14 " +
        "L-0.3,11.9 L4.5,10.7 L1.9,6.5 L6.7,7.6 L6.4,2.8 L10.1,6 Z");
    public static readonly Geometry Image = G(
        "F0 M3,4 L21,4 L21,20 L3,20 Z M5,6 L19,6 L19,18 L5,18 Z " +
        "M7,15 L10,11 L13,14 L15,12 L18,16 L7,16 Z M8.5,8 A1.5,1.5 0 1 0 8.51,8 Z");
    public static readonly Geometry Photo = G(
        "F0 M3,4 L21,4 L21,20 L3,20 Z M5,6 L19,6 L19,18 L5,18 Z " +
        "M12,8 A3,3 0 1 0 12.01,8 Z M7,17 A5,4.5 0 0 1 17,17 Z");
    public static readonly Geometry Signature = G(
        "M2,20 C6,14 8,9 6,7 C4,5 3,9 5,12 C8,17 16,17 20,10 L18.3,9 C15,15 9,15.5 6.5,12 " +
        "M19,4 L22,7 L14,15 L11,15 L11,12 Z");
    public static readonly Geometry Barcode = G(
        "M3,4 L4.4,4 L4.4,20 L3,20 Z M5.6,4 L7,4 L7,20 L5.6,20 Z " +
        "M8.6,4 L9.3,4 L9.3,20 L8.6,20 Z M10.6,4 L12.6,4 L12.6,20 L10.6,20 Z " +
        "M14,4 L14.7,4 L14.7,20 L14,20 Z M16,4 L17.4,4 L17.4,20 L16,20 Z " +
        "M18.6,4 L19.3,4 L19.3,20 L18.6,20 Z M20.3,4 L21.7,4 L21.7,20 L20.3,20 Z");
    public static readonly Geometry QrCode = G(
        "M3,3 L10,3 L10,10 L3,10 Z M5,5 L8,5 L8,8 L5,8 Z " +
        "M14,3 L21,3 L21,10 L14,10 Z M16,5 L19,5 L19,8 L16,8 Z " +
        "M3,14 L10,14 L10,21 L3,21 Z M5,16 L8,16 L8,19 L5,19 Z " +
        "M14,14 L17,14 L17,17 L14,17 Z M18,14 L21,14 L21,17 L18,17 Z M14,18 L17,18 L17,21 L14,21 Z M18,18 L21,18 L21,21 L18,21 Z");
    public static readonly Geometry DataField = G(
        "F0 M2,7 L22,7 L22,17 L2,17 Z M5,10 L19,10 L19,14 L5,14 Z");

    // --- Layers panel state icons -----------------------------------------------
    public static readonly Geometry Eye = G(
        "F0 M12,5 C6,5 2,12 2,12 C2,12 6,19 12,19 C18,19 22,12 22,12 C22,12 18,5 12,5 Z " +
        "M12,8 A4,4 0 1 0 12.01,8 Z");
    public static readonly Geometry EyeOff = G(
        "F0 M3,4 L21,20 L19.5,21.5 L16.7,19.1 C15.3,19.6 13.7,20 12,20 C6,20 2,13 2,13 C2,13 3.5,10.3 6.3,8.1 L1.5,4 Z " +
        "M12,8 A5,5 0 0 1 17,13 L12,8 Z M22,13 C22,13 20.5,15.7 17.7,17.9 L9.4,9.6 C10.2,9.2 11.1,9 12,9 C16.5,9 20,13 20,13 Z");
    public static readonly Geometry Lock = G(
        "F0 M6,10 L18,10 L18,21 L6,21 Z M8.5,12.5 L15.5,12.5 L15.5,18.5 L8.5,18.5 Z " +
        "M7,10 L7,7 A5,5 0 0 1 17,7 L17,10 L14.5,10 L14.5,7 A2.5,2.5 0 0 0 9.5,7 L9.5,10 Z");
    public static readonly Geometry Unlock = G(
        "F0 M6,10 L18,10 L18,21 L6,21 Z M8.5,12.5 L15.5,12.5 L15.5,18.5 L8.5,18.5 Z " +
        "M4,7 A5,5 0 0 1 14,7 L14,9 L11.5,9 L11.5,7 A2.5,2.5 0 0 0 6.5,7 L6.5,10 L4,10 Z");
    public static readonly Geometry LayerGeneric = G("F0 M4,4 L20,4 L20,20 L4,20 Z M6.5,6.5 L17.5,6.5 L17.5,17.5 L6.5,17.5 Z");

    // --- Design checker -----------------------------------------------------------
    public static readonly Geometry CheckCircle = G(
        "F0 M12,2 A10,10 0 1 0 12.01,2 Z M12,4.4 A7.6,7.6 0 1 1 11.99,4.4 Z " +
        "M9.5,12 L11,13.5 L15,9 L16.2,10.1 L11,16 L8.3,13.2 Z");
    public static readonly Geometry AlertTriangle = G(
        "F0 M12,3 L22,20 L2,20 Z M12,7.2 L5.2,18.5 L18.8,18.5 Z " +
        "M11,10 L13,10 L12.6,15 L11.4,15 Z M11.2,16.3 L12.8,16.3 L12.8,17.7 L11.2,17.7 Z");

    // --- Print Preview -----------------------------------------------------------
    public static readonly Geometry Print = G(
        "F0 M6,2 L18,2 L18,8 L6,8 Z M8,4 L16,4 L16,6 L8,6 Z " +
        "M3,8 L21,8 L21,17 L16,17 L16,22 L8,22 L8,17 L3,17 Z " +
        "M6,10 L15,10 L15,12 L6,12 Z M9,19 L15,19 L15,20 L9,20 Z");

    // --- Recent cards: pinned badge (Priority 1 fix) -----------------------------
    /// <summary>Simple thumbtack/pin silhouette used as a small corner badge on a
    /// pinned Recent Card tile, replacing the old "Pinned" text row that pushed the
    /// card name/subtitle down and clipped the tile.</summary>
    public static readonly Geometry Pin = G(
        "F0 M12,2 A4.2,4.2 0 1 0 12.01,2 Z M12,4.4 A1.8,1.8 0 1 1 11.99,4.4 Z " +
        "M11,6.2 L13,6.2 L13,13.5 L11,13.5 Z M12,13.5 L12,22 L10.5,17.5 L13.5,17.5 Z");

    // --- Add Image workflow (Priority 12) -----------------------------------------
    /// <summary>Simple folder outline, used for the "Browse Image" flyout option.</summary>
    public static readonly Geometry Folder = G(
        "F0 M2,5 A1,1 0 0 1 3,4 L9,4 L11,6 L21,6 A1,1 0 0 1 22,7 L22,18 A1,1 0 0 1 21,19 " +
        "L3,19 A1,1 0 0 1 2,18 Z M4,8 L20,8 L20,17 L4,17 Z");

    /// <summary>Simple camera body + lens, used for the "Take Photo / Use Camera"
    /// flyout option.</summary>
    public static readonly Geometry Camera = G(
        "F0 M9,4 L15,4 L16.5,6.5 L21,6.5 A1,1 0 0 1 22,7.5 L22,19 A1,1 0 0 1 21,20 " +
        "L3,20 A1,1 0 0 1 2,19 L2,7.5 A1,1 0 0 1 3,6.5 L7.5,6.5 Z " +
        "M12,9.5 A4.5,4.5 0 1 0 12.01,9.5 Z M12,11.7 A2.3,2.3 0 1 1 11.99,11.7 Z");
    public static Geometry Calendar =>
       Geometry.Parse(
           "F0 M3,6 L21,6 L21,21 L3,21 Z M4.5,10 L19.5,10 L19.5,19.5 L4.5,19.5 Z " +
           "M6,2 L7.5,2 L7.5,6 L6,6 Z M16.5,2 L18,2 L18,6 L16.5,6 Z " +
           "M6,11.5 L8.5,11.5 L8.5,14 L6,14 Z M10.75,11.5 L13.25,11.5 L13.25,14 L10.75,14 Z M15.5,11.5 L18,11.5 L18,14 L15.5,14 Z " +
           "M6,15.5 L8.5,15.5 L8.5,18 L6,18 Z M10.75,15.5 L13.25,15.5 L13.25,18 L10.75,18 Z");
    public static readonly Geometry Clock = G(
       "F0 M12,2 A10,10 0 1 0 12.01,2 Z M12,4.4 A7.6,7.6 0 1 1 11.99,4.4 Z " +
       "M11.2,6.8 L12.8,6.8 L12.8,12.8 L11.2,12.8 Z " +
       "M12,11.2 L16.2,11.2 L16.2,12.8 L12,12.8 Z");
    public static readonly Geometry CalendarClock = G(
       "F0 M2,5 L14,5 L14,19 L2,19 Z M4,3 L5.5,3 L5.5,6 L4,6 Z M10.5,3 L12,3 L12,6 L10.5,6 Z " +
       "M2,9 L14,9 L14,10.5 L2,10.5 Z " +
       "M17,11.5 A5,5 0 1 0 17.01,11.5 Z M17,13 A3.5,3.5 0 1 1 16.99,13 Z " +
       "M16.3,13 L17.7,13 L17.7,15.8 L16.3,15.8 Z M17,15 L19,15 L19,16.3 L17,16.3 Z");
}
