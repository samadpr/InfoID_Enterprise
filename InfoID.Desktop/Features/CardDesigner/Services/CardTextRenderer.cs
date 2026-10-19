using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// One text-drawing routine shared by the design canvas and the print/preview/PDF/thumbnail
/// renderer, so a text box prints exactly as it looks in the designer.
///
/// Rules (the same everywhere):
/// - Drawn at the element's own font size, weight, style, underline and alignment.
/// - Wraps at spaces only. A word that doesn't fit (an email address, a URL, a long ID)
///   makes the text shrink rather than split; it is broken between characters only when
///   it can't fit even at the minimum size.
/// - Never adds "..." -- a printed ID card must never show a shortened value. (Avalonia's
///   FormattedText defaults to WordEllipsis trimming, which is what turned a long email
///   into "j@g...".)
/// - If the value doesn't fit the box at the designed size, the font shrinks until it does,
///   down to a minimum size. Below that it's drawn at the minimum and clipped to the box.
/// </summary>
public static class CardTextRenderer
{
    /// <summary>Smallest size any printed text shrinks to, in the designer's font units
    /// (the same unit as the elements' FontSize), unless the element sets its own.</summary>
    public const double DefaultMinFontSize = 6;

    /// <summary>Draws <paramref name="value"/> inside <paramref name="rect"/>.</summary>
    /// <param name="fontSize">Designed size, already converted to the target's pixels.</param>
    /// <param name="minFontSize">Smallest allowed size in the same pixels as fontSize.</param>
    public static void Draw(
        DrawingContext context, Rect rect, string? value,
        string? fontFamily, double fontSize, double minFontSize, IBrush foreground,
        bool bold = false, bool italic = false, bool underline = false,
        TextAlignmentX horizontal = TextAlignmentX.Left,
        TextAlignmentY vertical = TextAlignmentY.Top)
    {
        if (string.IsNullOrEmpty(value) || rect.Width < 0.5 || rect.Height < 0.5 || fontSize <= 0)
            return;

        var typeface = new Typeface(
            string.IsNullOrWhiteSpace(fontFamily) ? FontFamily.Default : new FontFamily(fontFamily),
            italic ? FontStyle.Italic : FontStyle.Normal,
            bold ? FontWeight.Bold : FontWeight.Normal);
        var alignment = horizontal switch
        {
            TextAlignmentX.Center => TextAlignment.Center,
            TextAlignmentX.Right => TextAlignment.Right,
            TextAlignmentX.Justify => TextAlignment.Justify,
            _ => TextAlignment.Left,
        };
        var decorations = underline ? TextDecorations.Underline : null;

        // WrapWithOverflow breaks only at spaces: a word that doesn't fit makes the line
        // too wide, so the fit test fails and the font shrinks -- an email in a one-line
        // box stays on one line, just smaller. Only if nothing fits even at the minimum
        // size is a long word broken between characters (TextWrapping.Wrap).
        TextLayout Build(double size, TextWrapping wrapping) => new(
            value, typeface, size, foreground, alignment, wrapping, TextTrimming.None,
            decorations, FlowDirection.LeftToRight, maxWidth: rect.Width);

        bool Fits(TextLayout candidate) =>
            candidate.Height <= rect.Height + 0.5 && candidate.WidthIncludingTrailingWhitespace <= rect.Width + 0.5;

        var size = Math.Max(0.5, fontSize);
        var layout = Build(size, TextWrapping.WrapWithOverflow);

        if (!Fits(layout))
        {
            // Shrink to fit: binary search for the largest size between the minimum and the
            // designed size that fits. Only runs for text that overflows, so normal boxes
            // cost one layout.
            var low = Math.Max(0.5, Math.Min(minFontSize, size));
            var high = size;
            var smallest = Build(low, TextWrapping.WrapWithOverflow);
            if (Fits(smallest))
            {
                layout = smallest;
                for (var i = 0; i < 10 && high - low > 0.25; i++)
                {
                    var mid = (low + high) / 2;
                    var candidate = Build(mid, TextWrapping.WrapWithOverflow);
                    if (Fits(candidate)) { low = mid; layout = candidate; }
                    else high = mid;
                }
            }
            else
            {
                // Even the minimum size doesn't fit: break long words so as much of the
                // value as possible is visible; whatever is left is clipped to the box.
                layout = Build(low, TextWrapping.Wrap);
            }
        }

        var y = vertical switch
        {
            TextAlignmentY.Middle => rect.Y + (rect.Height - layout.Height) / 2,
            TextAlignmentY.Bottom => rect.Bottom - layout.Height,
            _ => rect.Y,
        };

        // Text that still overflows at the minimum size is clipped to its own box, never
        // spilled over neighbouring elements.
        using (context.PushClip(rect))
        {
            layout.Draw(context, new Point(rect.X, Math.Max(rect.Y, y)));
        }
    }

    /// <summary>The text a Date/Time element shows (same format rules as the canvas).</summary>
    public static string FormatDateTime(DateTimeElement element)
    {
        try
        {
            var value = element.Value;
            return element.DisplayFormat switch
            {
                DateTimeDisplayFormat.DateTime => $"{value.ToString(element.DateFormat)} {value.ToString(element.TimeFormat)}",
                DateTimeDisplayFormat.Date => value.ToString(element.DateFormat),
                DateTimeDisplayFormat.Time => value.ToString(element.TimeFormat),
                _ => string.Empty,
            };
        }
        catch (FormatException)
        {
            return element.Value.ToString("g");
        }
    }
}
