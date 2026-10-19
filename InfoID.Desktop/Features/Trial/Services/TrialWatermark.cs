using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace InfoID.Desktop.Features.Trial.Services;

/// <summary>
/// The "INFOID TRIAL VERSION" mark on trial cards. Drawn by the same renderer that
/// produces the printed bitmap (ThumbnailRenderer, via CardRasterizer), so it is part of
/// the card image sent to the printer -- not an on-screen overlay that could be left off.
///
/// Two parts:
/// - a large diagonal text across the card, semi-transparent with a light outline, so
///   it shows on light and dark designs while names, photos and numbers stay readable;
/// - a thin solid strip along the top edge, which survives black-only (monochrome)
///   printing, where the faint diagonal would be thresholded away.
/// </summary>
public static class TrialWatermark
{
    public const string Text = "INFOID TRIAL VERSION";

    private static readonly IBrush DiagonalFill = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28), 0.30);
    private static readonly IBrush DiagonalOutline = new SolidColorBrush(Colors.White, 0.55);
    private static readonly IBrush StripFill = new SolidColorBrush(Colors.White, 0.9);
    private static readonly IBrush StripText = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
    private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    public static void Draw(DrawingContext context, Rect cardRect)
    {
        if (cardRect.Width < 8 || cardRect.Height < 8) return;

        using (context.PushClip(cardRect))
        {
            DrawDiagonal(context, cardRect);
            DrawStrip(context, cardRect);
        }
    }

    private static void DrawDiagonal(DrawingContext context, Rect card)
    {
        var diagonal = Math.Sqrt(card.Width * card.Width + card.Height * card.Height);
        // Measure at a reference size, then scale so the text spans ~78% of the diagonal.
        var probe = Format(100);
        var size = Math.Max(4, 100 * (diagonal * 0.78) / Math.Max(1, probe.Width));
        var text = Format(size);
        var geometry = text.BuildGeometry(new Point(-text.Width / 2, -text.Height / 2));
        if (geometry is null) return;

        var angle = -Math.Atan2(card.Height, card.Width);
        var transform = Matrix.CreateRotation(angle) * Matrix.CreateTranslation(card.Center.X, card.Center.Y);
        using (context.PushTransform(transform))
        {
            context.DrawGeometry(DiagonalFill, new Pen(DiagonalOutline, Math.Max(0.5, size * 0.035)), geometry);
        }
    }

    private static void DrawStrip(DrawingContext context, Rect card)
    {
        var height = Math.Max(4, card.Height * 0.075);
        var strip = new Rect(card.X, card.Y, card.Width, height);
        context.FillRectangle(StripFill, strip);

        var size = height * 0.62;
        var text = new FormattedText(Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, size, StripText);
        // Never wider than the card: shrink if needed.
        if (text.Width > card.Width * 0.9)
            text = new FormattedText(Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, size * card.Width * 0.9 / text.Width, StripText);
        context.DrawText(text, new Point(strip.Center.X - text.Width / 2, strip.Center.Y - text.Height / 2));
    }

    private static FormattedText Format(double size) =>
        new(Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, size, DiagonalFill);
}
