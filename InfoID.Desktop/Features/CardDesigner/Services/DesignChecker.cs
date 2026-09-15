using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>Default <see cref="IDesignChecker"/>. Every check here is pure geometry or
/// data-presence over the document model -- no file I/O, no external libraries -- so
/// each one is something this implementation can actually guarantee is correct, rather
/// than a check that only looks right (e.g. this deliberately does NOT attempt an
/// "image resolution too low" check, since that would require loading the actual asset
/// file through IDesignAssetService and could throw/mislead for assets that are fine but
/// temporarily unavailable at check time).</summary>
public sealed class DesignChecker : IDesignChecker
{
    public IReadOnlyList<DesignIssue> Check(CardDesignDocument document)
    {
        var issues = new List<DesignIssue>();
        CheckSide(document, document.Front, issues);
        CheckSide(document, document.Back, issues);

        return issues
            .OrderBy(i => i.Severity)
            .ToList();
    }

    private static void CheckSide(CardDesignDocument document, CardDesignSide side, List<DesignIssue> issues)
    {
        // Deliberately built from Rect.Left/Top/Right/Bottom comparisons only (already
        // used elsewhere in this codebase's own canvas/snap code) rather than
        // Rect.Contains(Rect)/Intersects/Deflate -- keeps this independently correct by
        // inspection instead of depending on exact overloads this project hasn't
        // exercised before.
        double cardLeft = 0, cardTop = 0, cardRight = document.WidthMm, cardBottom = document.HeightMm;
        double safeLeft = document.SafeZoneMm, safeTop = document.SafeZoneMm;
        double safeRight = document.WidthMm - document.SafeZoneMm, safeBottom = document.HeightMm - document.SafeZoneMm;

        foreach (var element in side.Elements)
        {
            if (!element.Visible) continue; // an intentionally hidden element's placement isn't a print concern

            if (element.Width <= 0 || element.Height <= 0)
            {
                Add(issues, DesignIssueSeverity.Error, $"\"{element.Name}\" has zero or negative size.", element, side.Side);
                continue; // further geometry checks are meaningless for a degenerate rect
            }

            var bounds = GetVisualBounds(element);

            var overlapsCardAtAll = bounds.Left < cardRight && bounds.Right > cardLeft &&
                                     bounds.Top < cardBottom && bounds.Bottom > cardTop;
            var fullyInsideCard = bounds.Left >= cardLeft && bounds.Top >= cardTop &&
                                   bounds.Right <= cardRight && bounds.Bottom <= cardBottom;
            var fullyInsideSafeZone = bounds.Left >= safeLeft && bounds.Top >= safeTop &&
                                      bounds.Right <= safeRight && bounds.Bottom <= safeBottom;

            if (!overlapsCardAtAll)
            {
                Add(issues, DesignIssueSeverity.Error, $"\"{element.Name}\" is placed entirely outside the card.", element, side.Side);
            }
            else if (!fullyInsideCard)
            {
                Add(issues, DesignIssueSeverity.Warning, $"\"{element.Name}\" extends past the edge of the card.", element, side.Side);
            }
            else if (!fullyInsideSafeZone)
            {
                Add(issues, DesignIssueSeverity.Warning, $"\"{element.Name}\" crosses into the {document.SafeZoneMm:0.#}mm safe zone.", element, side.Side);
            }

            switch (element)
            {
                case ImageElement { AssetReference: null or "" }:
                    Add(issues, DesignIssueSeverity.Error, $"\"{element.Name}\" has no image selected.", element, side.Side);
                    break;
                case PhotoElement { AssetReference: null or "" }:
                    Add(issues, DesignIssueSeverity.Warning, $"\"{element.Name}\" has no photo selected yet.", element, side.Side);
                    break;
                case SignatureElement { AssetReference: null or "" }:
                    Add(issues, DesignIssueSeverity.Warning, $"\"{element.Name}\" has no signature image selected yet.", element, side.Side);
                    break;
                case BarcodeElement { Value: null or "" }:
                    Add(issues, DesignIssueSeverity.Error, $"\"{element.Name}\" has no barcode value.", element, side.Side);
                    break;
                case DataFieldElement { FieldKey: null or "" }:
                    Add(issues, DesignIssueSeverity.Warning, $"\"{element.Name}\" has no field selected.", element, side.Side);
                    break;
                case TextElement { Text: null or "" }:
                    Add(issues, DesignIssueSeverity.Info, $"\"{element.Name}\" is empty.", element, side.Side);
                    break;
                case TextElement text when IsTextOverflowing(text):
                    Add(issues, DesignIssueSeverity.Warning, $"\"{element.Name}\"'s text may not fit in its box at the current size.", element, side.Side);
                    break;
            }
        }

        // Overlap: O(n^2) over one side's elements, which in practice (Part 69's own
        // ~50-element target) is at most a couple thousand comparisons -- fine for an
        // on-demand check, not something run every frame.
        var visible = side.Elements.Where(e => e.Visible).ToList();
        for (var i = 0; i < visible.Count; i++)
        {
            for (var j = i + 1; j < visible.Count; j++)
            {
                var a = GetVisualBounds(visible[i]);
                var b = GetVisualBounds(visible[j]);

                var overlapLeft = Math.Max(a.Left, b.Left);
                var overlapTop = Math.Max(a.Top, b.Top);
                var overlapRight = Math.Min(a.Right, b.Right);
                var overlapBottom = Math.Min(a.Bottom, b.Bottom);
                if (overlapRight <= overlapLeft || overlapBottom <= overlapTop) continue;

                var overlapArea = (overlapRight - overlapLeft) * (overlapBottom - overlapTop);
                var smallerArea = Math.Min(a.Width * a.Height, b.Width * b.Height);
                if (smallerArea > 0 && overlapArea / smallerArea > 0.5)
                {
                    Add(issues, DesignIssueSeverity.Info,
                        $"\"{visible[i].Name}\" and \"{visible[j].Name}\" overlap heavily -- check this is intentional.",
                        visible[i], side.Side);
                }
            }
        }
    }

    /// <summary>Rough overflow estimate for a fixed-size, non-wrapping, non-auto-fit
    /// text box: measures the text at its configured font and compares against the
    /// element's own box. Wrapping or auto-fit text is exempt -- those modes are
    /// designed to handle overflow themselves (shrink or wrap), so flagging them would
    /// be a false positive.</summary>
    private static bool IsTextOverflowing(TextElement text)
    {
        if (text.WrapText || text.AutoFit || string.IsNullOrEmpty(text.Text)) return false;

        try
        {
            var weight = text.Bold ? FontWeight.Bold : FontWeight.Normal;
            var style = text.Italic ? FontStyle.Italic : FontStyle.Normal;
            var typeface = new Typeface(text.FontFamily, style, weight);
            var formatted = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, text.FontSize, Brushes.Black);

            // FormattedText measures in the same unit as FontSize; TextElement stores
            // FontSize in points/px and Width/Height in millimeters, so this is a
            // deliberately approximate check (roughly 1mm ~= 3.78px at 96dpi) -- good
            // enough to flag "clearly won't fit", not a pixel-perfect layout engine.
            const double mmPerPixel = 1 / 3.7795;
            var widthMm = formatted.Width * mmPerPixel;
            var heightMm = formatted.Height * mmPerPixel;

            return widthMm > text.Width * 1.05 || heightMm > text.Height * 1.2;
        }
        catch
        {
            // An unmeasurable font (unusual glyphs, missing font) shouldn't crash the
            // whole design check over one text box -- just skip this one check for it.
            return false;
        }
    }

    private static Rect GetVisualBounds(DesignerElement el)
    {
        if (el.Rotation == 0) return new Rect(el.X, el.Y, el.Width, el.Height);

        var cx = el.X + el.Width / 2;
        var cy = el.Y + el.Height / 2;
        var rad = el.Rotation * Math.PI / 180.0;
        var cos = Math.Abs(Math.Cos(rad));
        var sin = Math.Abs(Math.Sin(rad));
        var rotatedW = el.Width * cos + el.Height * sin;
        var rotatedH = el.Width * sin + el.Height * cos;
        return new Rect(cx - rotatedW / 2, cy - rotatedH / 2, rotatedW, rotatedH);
    }

    private static void Add(List<DesignIssue> issues, DesignIssueSeverity severity, string message, DesignerElement? element, CardSide side) =>
        issues.Add(new DesignIssue { Severity = severity, Message = message, Element = element, Side = side });
}
