using InfoID.Desktop.Features.BlankCard.Models;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.Visitors.Services;

/// <summary>
/// A ready-made visitor badge (CR-80 portrait) bound to the visitor fields, so Visitor
/// Management works on day one without designing anything. It is an ordinary design:
/// saved like any other and editable in the Card Designer.
///
///   ┌──────────────────────┐
///   │ VISITOR          TUE │  red header, visit weekday
///   │      ┌────────┐      │
///   │      │ photo  │      │
///   │      └────────┘      │
///   │     Full Name        │
///   │      Company         │
///   │  Meeting: Person     │
///   │    15 Oct 2026       │
///   │ [QR]  V-000231       │  QR encodes the visitor ID
///   └──────────────────────┘
/// </summary>
public static class VisitorStarterBadge
{
    public const string DefaultName = "Visitor badge";
    private const string Accent = "#DA3025";
    private const double W = 53.98;   // CR-80 portrait, mm
    private const double H = 85.60;

    public static CardDesignDocument Create(string name = DefaultName)
    {
        var document = new CardDesignDocument
        {
            Name = name,
            Description = "Starter visitor badge (Visitor Management). Bound to the Visitors fields.",
            CardFormatId = "cr80",
            WidthMm = W,
            HeightMm = H,
            Orientation = CardOrientation.Portrait,
        };

        var z = 0;
        void Add(DesignerElement element)
        {
            element.ZIndex = z++;
            document.Front.Elements.Add(element);
        }

        Add(new ShapeElement
        {
            Name = "Header", Kind = ShapeKind.Rectangle, X = 0, Y = 0, Width = W, Height = 14,
            FillColorHex = Accent, FillEnabled = true, StrokeWidth = 0,
        });
        Add(Fixed("VISITOR", 4, 3, 34, 8, 15, bold: true, color: "#FFFFFF", align: TextAlignmentX.Left));
        Add(Bound(VisitorBadgeData.VisitDay, "DAY", 36, 4, 14, 6, 9, bold: true, color: "#FFFFFF", align: TextAlignmentX.Right));

        Add(new ImageElement
        {
            Name = "Photo", X = (W - 25) / 2, Y = 17, Width = 25, Height = 31,
            IsDatabaseBound = true, DatabaseColumn = VisitorBadgeData.Photo,
            FitMode = ImageFitMode.Fill, CornerRadius = 1.5,
            MissingPhotoBehavior = MissingPhotoBehavior.Placeholder,
        });

        Add(Bound(VisitorBadgeData.FullName, "Visitor name", 3, 50, W - 6, 7, 12, bold: true, autoFit: true));
        Add(Bound(VisitorBadgeData.Company, "Company", 3, 57, W - 6, 5, 8.5, color: "#4B5059", autoFit: true));
        Add(Expression("Meeting: {{" + VisitorBadgeData.PersonToMeet + "}}", "Meeting: Person to meet", 3, 62.5, W - 6, 4.5, 7.5));
        Add(Bound(VisitorBadgeData.VisitDate, "Visit date", 3, 67, W - 6, 4.5, 7.5, color: "#4B5059"));

        Add(new QrCodeElement
        {
            Name = "Visitor ID QR", X = 4, Y = 71.5, Width = 12.5, Height = 12.5,
            Value = "{{" + VisitorBadgeData.VisitorId + "}}", MarginMm = 0,
        });
        Add(Bound(VisitorBadgeData.VisitorId, "V-000000", 19, 72.5, W - 22, 5.5, 10, bold: true, align: TextAlignmentX.Left));
        Add(Fixed("Please return this badge when you leave", 19, 78, W - 22, 5, 5.5, color: "#4B5059", align: TextAlignmentX.Left));

        return document;
    }

    private static TextElement Fixed(string text, double x, double y, double w, double h, double size,
        bool bold = false, string color = "#1B1D22", TextAlignmentX align = TextAlignmentX.Center) => new()
    {
        Name = text.Length > 20 ? text[..20] : text, Text = text, X = x, Y = y, Width = w, Height = h,
        FontSize = size, Bold = bold, ColorHex = color, HorizontalAlignment = align,
        VerticalAlignment = TextAlignmentY.Middle, FontFamily = "Segoe UI",
    };

    /// <summary>Text showing one visitor field (Parameters → Database in the designer).</summary>
    private static TextElement Bound(string column, string placeholder, double x, double y, double w, double h, double size,
        bool bold = false, string color = "#1B1D22", TextAlignmentX align = TextAlignmentX.Center, bool autoFit = false)
    {
        var element = Fixed(placeholder, x, y, w, h, size, bold, color, align);
        element.Name = column;
        element.SourceType = TextSourceType.Database;   // also sets IsDatabaseBound
        element.IsDatabaseBound = true;
        element.DatabaseColumn = column;
        element.AutoFit = autoFit;
        element.MinFontSize = 6;
        return element;
    }

    /// <summary>Text mixing fixed words and fields, e.g. "Meeting: {{PersonToMeet}}".</summary>
    private static TextElement Expression(string expression, string placeholder, double x, double y, double w, double h, double size)
    {
        var element = Fixed(placeholder, x, y, w, h, size);
        element.Name = "Meeting";
        element.DataBindingExpression = expression;
        return element;
    }
}
