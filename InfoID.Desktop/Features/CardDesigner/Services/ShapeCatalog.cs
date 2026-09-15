using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Views.Icons;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>One entry in the Shapes flyout: a stable Id (for lookup/persistence-free
/// re-selection), a user-facing display name (search matches against this, and it also
/// becomes the inserted element's Name -- so the Layers panel shows "Rounded Rectangle"/
/// "Right Triangle"/"Double Arrow" etc., not a generic "Shape"), a category for the
/// flyout's grouping, an icon, and a factory that builds a real, ready-to-insert
/// ShapeElement.</summary>
public sealed record ShapePreset(string Id, string DisplayName, string Category, Geometry Icon, Func<ShapeElement> Create);

/// <summary>One category section of the Shapes flyout, already filtered -- each group
/// carries its own preset list directly so the flyout's two-level ItemsControl (outer:
/// groups, inner: each group's own Presets) never needs to reach back into an ancestor
/// DataContext to find "the presets for this category".</summary>
public sealed record ShapeCategoryGroup(string Category, IReadOnlyList<ShapePreset> Presets);

/// <summary>
/// Strongly-typed catalog of every native (vector, non-bitmap) shape the Shapes flyout
/// offers -- deliberately a plain in-memory list, not a database/JSON file, per the
/// brief ("a strongly typed shape catalog is preferable for native shapes" -- there is
/// nothing here that changes at runtime or needs persisting).
///
/// Several entries deliberately reuse an EXISTING ShapeKind with different preset
/// parameters rather than adding a new enum member for every named shape:
///  - Rounded Rectangle = Rectangle + a non-zero CornerRadius (already-modeled field).
///  - Circle = Ellipse with equal Width/Height.
///  - Diamond/Pentagon/Hexagon/Heptagon/Octagon = Polygon + PolygonSides 4/5/6/7/8.
///    (ShapeRenderer's PolygonPoints places sides evenly starting at the top, so 4
///    sides lands on top/right/bottom/left -- a diamond, not an axis-aligned square.)
///  - Double Arrow = Arrow with both StartArrowhead and EndArrowhead set (both flags
///    already existed on ShapeElement and ShapeRenderer.DrawArrow already honors both).
///  - Burst / Seal = Star with a high point count and a shallower inner radius.
/// Only RightTriangle and Parallelogram needed genuinely new ShapeKind members and
/// geometry, since their point sets aren't expressible as an evenly-spaced polygon.
/// </summary>
public static class ShapeCatalog
{
    public const string CategoryBasic = "Basic";
    public const string CategoryLinesArrows = "Lines & Arrows";
    public const string CategoryTriangles = "Triangles";
    public const string CategoryPolygons = "Polygons";
    public const string CategoryStars = "Stars & Symbols";

    /// <summary>Display order for the flyout's category grouping.</summary>
    public static IReadOnlyList<string> Categories { get; } = new[]
    {
        CategoryBasic, CategoryLinesArrows, CategoryTriangles, CategoryPolygons, CategoryStars,
    };

    public static IReadOnlyList<ShapePreset> All { get; } = new List<ShapePreset>
    {
        new("Rectangle", "Rectangle", CategoryBasic, DesignerIcons.Rectangle,
            () => new ShapeElement { Name = "Rectangle", Kind = ShapeKind.Rectangle, Width = 30, Height = 20 }),
        new("RoundedRectangle", "Rounded Rectangle", CategoryBasic, DesignerIcons.RoundedRectangle,
            () => new ShapeElement { Name = "Rounded Rectangle", Kind = ShapeKind.Rectangle, Width = 30, Height = 20, CornerRadius = 3 }),
        new("Ellipse", "Ellipse", CategoryBasic, DesignerIcons.Ellipse,
            () => new ShapeElement { Name = "Ellipse", Kind = ShapeKind.Ellipse, Width = 28, Height = 20 }),
        new("Circle", "Circle", CategoryBasic, DesignerIcons.Circle,
            () => new ShapeElement { Name = "Circle", Kind = ShapeKind.Ellipse, Width = 20, Height = 20 }),

        new("Line", "Line", CategoryLinesArrows, DesignerIcons.Line,
            () => new ShapeElement { Name = "Line", Kind = ShapeKind.Line, Width = 30, Height = 0, StrokeWidth = 1, FillEnabled = false }),
        new("Arrow", "Arrow", CategoryLinesArrows, DesignerIcons.Arrow,
            () => new ShapeElement { Name = "Arrow", Kind = ShapeKind.Arrow, Width = 30, Height = 0, StrokeWidth = 1.2, FillEnabled = false, EndArrowhead = true }),
        new("DoubleArrow", "Double Arrow", CategoryLinesArrows, DesignerIcons.DoubleArrow,
            () => new ShapeElement { Name = "Double Arrow", Kind = ShapeKind.Arrow, Width = 30, Height = 0, StrokeWidth = 1.2, FillEnabled = false, StartArrowhead = true, EndArrowhead = true }),

        new("Triangle", "Triangle", CategoryTriangles, DesignerIcons.Triangle,
            () => new ShapeElement { Name = "Triangle", Kind = ShapeKind.Triangle, Width = 24, Height = 20 }),
        new("RightTriangle", "Right Triangle", CategoryTriangles, DesignerIcons.RightTriangle,
            () => new ShapeElement { Name = "Right Triangle", Kind = ShapeKind.RightTriangle, Width = 24, Height = 20 }),

        new("Diamond", "Diamond", CategoryPolygons, DesignerIcons.Diamond,
            () => new ShapeElement { Name = "Diamond", Kind = ShapeKind.Polygon, PolygonSides = 4, Width = 22, Height = 22 }),
        new("Parallelogram", "Parallelogram", CategoryPolygons, DesignerIcons.Parallelogram,
            () => new ShapeElement { Name = "Parallelogram", Kind = ShapeKind.Parallelogram, Width = 30, Height = 18 }),
        new("Pentagon", "Pentagon", CategoryPolygons, DesignerIcons.Pentagon,
            () => new ShapeElement { Name = "Pentagon", Kind = ShapeKind.Polygon, PolygonSides = 5, Width = 22, Height = 22 }),
        new("Hexagon", "Hexagon", CategoryPolygons, DesignerIcons.Hexagon,
            () => new ShapeElement { Name = "Hexagon", Kind = ShapeKind.Polygon, PolygonSides = 6, Width = 22, Height = 22 }),
        new("Heptagon", "Heptagon", CategoryPolygons, DesignerIcons.Heptagon,
            () => new ShapeElement { Name = "Heptagon", Kind = ShapeKind.Polygon, PolygonSides = 7, Width = 22, Height = 22 }),
        new("Octagon", "Octagon", CategoryPolygons, DesignerIcons.Octagon,
            () => new ShapeElement { Name = "Octagon", Kind = ShapeKind.Polygon, PolygonSides = 8, Width = 22, Height = 22 }),

        new("Star", "Star", CategoryStars, DesignerIcons.Star,
            () => new ShapeElement { Name = "Star", Kind = ShapeKind.Star, Width = 22, Height = 22 }),
        new("Burst", "Burst / Seal", CategoryStars, DesignerIcons.Burst,
            () => new ShapeElement { Name = "Burst / Seal", Kind = ShapeKind.Star, StarPoints = 16, StarInnerRadiusRatio = 0.85, Width = 24, Height = 24 }),
    };

    /// <summary>Case-insensitive substring match against DisplayName. Empty/whitespace
    /// query returns every preset (search box cleared = show everything again).</summary>
    public static IReadOnlyList<ShapePreset> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return All;
        return All.Where(p => p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Search results grouped into their categories, in Categories' display
    /// order -- a category with no matches is omitted entirely rather than shown with
    /// an empty body (so e.g. searching "star" shows only "Stars &amp; Symbols", not
    /// four empty category headers above it).</summary>
    public static IReadOnlyList<ShapeCategoryGroup> SearchGrouped(string? query)
    {
        var matches = Search(query);
        return Categories
            .Select(category => new ShapeCategoryGroup(category, matches.Where(p => p.Category == category).ToList()))
            .Where(group => group.Presets.Count > 0)
            .ToList();
    }
}
