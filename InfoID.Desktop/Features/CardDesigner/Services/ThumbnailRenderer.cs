using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Draws a real (not placeholder-gray) representation of one side of a
/// CardDesignDocument into a DrawingContext, for Recent Cards thumbnails (Part 61) and
/// Print Preview (Part 41).
///
/// Deliberately NOT reusing CardCanvasView's own DrawElement/DrawText/etc as instance
/// methods: those are entangled with the interactive canvas's own bitmap cache field and
/// CardDesignTabViewModel dependency. The shape-geometry helpers below (PolygonPoints,
/// StarPoints, BuildPolygonGeometry, DrawArrow/DrawArrowhead) are copied verbatim from
/// CardCanvasView rather than re-derived, specifically so a hexagon/star/polygon renders
/// identically here and on the live canvas -- the bug this fixed was exactly a Star
/// shape rendering as a plain rectangle because this file previously only special-cased
/// Ellipse and treated every other ShapeKind as a rectangle.
/// </summary>
public static class ThumbnailRenderer
{
    private static readonly IBrush ImagePlaceholderBrush = new SolidColorBrush(Color.Parse("#D8D8D8"));
    private static readonly IBrush BarcodePlaceholderBrush = new SolidColorBrush(Color.Parse("#E8E8E8"));

    /// <summary>Optional extras: data-binding/conditional-visibility evaluation against
    /// a sample record, bleed/safe-zone guides, and real asset decoding for Image/Photo/
    /// Signature elements. All null/false by default, so a caller that doesn't pass
    /// RenderOptions at all still gets a sensible plain render.</summary>
    public sealed class RenderOptions
    {
        public Func<TextElement, string>? ResolveText { get; init; }
        public Func<DataFieldElement, string>? ResolveField { get; init; }
        public Func<DesignerElement, bool>? IsVisibleNow { get; init; }
        public bool ShowBleedAndSafeZone { get; init; }

        /// <summary>Resolves an element's AssetReference to a real file path (see
        /// IDesignAssetService.ResolveToFullPath). Null, or a null/failed result from
        /// it, falls back to a plain tinted placeholder box -- never a crash on a
        /// missing/corrupt file.</summary>
        public Func<string?, string?>? ResolveAssetPath { get; init; }
    }

    public static void Render(DrawingContext context, CardDesignDocument document, CardSide side, Rect targetRect, RenderOptions? options = null)
    {
        var cardWidthMm = Math.Max(1, document.WidthMm);
        var cardHeightMm = Math.Max(1, document.HeightMm);

        // Contain-fit the card into targetRect, centered, preserving its real aspect
        // ratio -- a stretched render would misrepresent the actual card proportions.
        var scale = Math.Min(targetRect.Width / cardWidthMm, targetRect.Height / cardHeightMm);
        var cardWidthPx = cardWidthMm * scale;
        var cardHeightPx = cardHeightMm * scale;
        var cardRect = new Rect(
            targetRect.X + (targetRect.Width - cardWidthPx) / 2,
            targetRect.Y + (targetRect.Height - cardHeightPx) / 2,
            cardWidthPx, cardHeightPx);

        var radius = document.CornerRadiusMm * scale;
        var roundedRect = new RoundedRect(cardRect, Math.Min(radius, Math.Min(cardWidthPx, cardHeightPx) / 2));

        var sideModel = document.GetSide(side);
        var background = sideModel.Background;

        if (background.Kind == BackgroundKind.Image && !string.IsNullOrEmpty(background.AssetReference))
        {
            context.DrawRectangle(null, new Pen(Brushes.Gray, 0.75), roundedRect);
            using (context.PushClip(roundedRect))
            {
                DrawBackgroundImage(context, cardRect, background.AssetReference, options);
            }
        }
        else
        {
            context.DrawRectangle(ResolveBackgroundBrush(background), new Pen(Brushes.Gray, 0.75), roundedRect);
        }

        using (context.PushClip(roundedRect))
        {
            var visible = sideModel.Elements.Where(e => e.Visible && (options?.IsVisibleNow?.Invoke(e) ?? true));
            foreach (var element in visible.OrderBy(e => e.ZIndex))
            {
                DrawElement(context, cardRect, scale, element, options);
            }
        }

        if (options?.ShowBleedAndSafeZone == true)
        {
            if (document.BleedMm > 0)
            {
                var bleedRect = cardRect.Inflate(document.BleedMm * scale);
                context.DrawRectangle(null, new Pen(Brushes.OrangeRed, 1, dashStyle: DashStyle.Dash), bleedRect);
            }

            if (document.SafeZoneMm > 0)
            {
                var safeRect = cardRect.Deflate(document.SafeZoneMm * scale);
                context.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1, dashStyle: DashStyle.Dash), safeRect);
            }
        }
    }

    private static void DrawElement(DrawingContext context, Rect cardRect, double scale, DesignerElement element, RenderOptions? options)
    {
        var rect = new Rect(cardRect.X + element.X * scale, cardRect.Y + element.Y * scale,
            Math.Max(0.5, element.Width * scale), Math.Max(0.5, element.Height * scale));

        switch (element)
        {
            case TextElement text:
                var textValue = options?.ResolveText?.Invoke(text) ?? text.Text;
                DrawText(context, rect, textValue, text.FontFamily, text.ColorHex, text.HorizontalAlignment);
                break;
            case DataFieldElement field:
                var fieldValue = options?.ResolveField?.Invoke(field) ?? $"{{{{{field.FieldKey}}}}}";
                DrawText(context, rect, fieldValue, field.FontFamily, field.ColorHex, field.HorizontalAlignment);
                break;
            case ShapeElement shape:
                DrawShape(context, rect, shape);
                break;
            case ImageElement image:
                DrawImageOrPlaceholder(context, rect, image.AssetReference, options, image.MaskShape, image.FlipHorizontal, image.FlipVertical);
                break;
            case PhotoElement photo:
                DrawImageOrPlaceholder(context, rect, photo.AssetReference, options);
                break;
            case SignatureElement signature:
                DrawImageOrPlaceholder(context, rect, signature.AssetReference, options);
                break;
            case BarcodeElement barcode:
                DrawBarcodeOrQr(context, rect, BarcodeRenderer.TryGenerate(barcode));
                break;
            case QrCodeElement qr:
                DrawBarcodeOrQr(context, rect, BarcodeRenderer.TryGenerate(qr));
                break;
            case PenElement pen:
                PenRenderer.Draw(context, rect, pen);
                break;
        }
    }

    /// <summary>Real barcode/QR rendering (BarcodeRenderer.TryGenerate, shared with the
    /// live canvas -- see that class's own doc comment), contain-fit into rect
    /// (aspect-preserving, same formula as DrawImageOrPlaceholder's own contain-fit and
    /// CardCanvasView.DrawFit). Falls back to the plain placeholder box only when
    /// encoding genuinely failed (invalid value for the symbology), matching the live
    /// canvas's own honest-fallback behavior instead of silently showing nothing.</summary>
    private static void DrawBarcodeOrQr(DrawingContext context, Rect rect, Bitmap? bitmap)
    {
        if (bitmap is null)
        {
            context.DrawRectangle(BarcodePlaceholderBrush, null, rect);
            return;
        }

        var sourceSize = bitmap.PixelSize.ToSizeWithDpi(bitmap.Dpi);
        var fitScale = Math.Min(rect.Width / sourceSize.Width, rect.Height / sourceSize.Height);
        var destSize = new Size(sourceSize.Width * fitScale, sourceSize.Height * fitScale);
        var destRect = new Rect(
            rect.Center.X - destSize.Width / 2, rect.Center.Y - destSize.Height / 2,
            destSize.Width, destSize.Height);
        context.DrawImage(bitmap, new Rect(sourceSize), destRect);
    }

    /// <summary>Draws a BackgroundKind.Image background cover-fit (fills rect entirely,
    /// cropping overflow, never stretching) -- deliberately different fit math from
    /// DrawImageOrPlaceholder below (which contain-fits Image/Photo/Signature ELEMENTS),
    /// matching CardCanvasView.DrawImage's ImageFitMode.Fill formula exactly so a
    /// background looks identical here (thumbnails/Print Preview) and on the live
    /// canvas -- see this class's own header doc comment for why that consistency
    /// matters (the Star-render duplication bug this codebase already hit once).</summary>
    private static void DrawBackgroundImage(DrawingContext context, Rect rect, string assetReference, RenderOptions? options)
    {
        var fullPath = options?.ResolveAssetPath?.Invoke(assetReference);
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            context.DrawRectangle(ImagePlaceholderBrush, null, rect);
            return;
        }

        Bitmap? bitmap;
        try
        {
            bitmap = new Bitmap(fullPath);
        }
        catch
        {
            bitmap = null;
        }

        if (bitmap is null)
        {
            context.DrawRectangle(ImagePlaceholderBrush, null, rect);
            return;
        }

        using (bitmap)
        {
            var sourceSize = bitmap.PixelSize.ToSizeWithDpi(bitmap.Dpi);
            var fillScale = Math.Max(rect.Width / sourceSize.Width, rect.Height / sourceSize.Height);
            var visibleW = rect.Width / fillScale;
            var visibleH = rect.Height / fillScale;
            var offsetX = Math.Max(0, (sourceSize.Width - visibleW) / 2);
            var offsetY = Math.Max(0, (sourceSize.Height - visibleH) / 2);
            var sourceRect = new Rect(offsetX, offsetY, Math.Min(visibleW, sourceSize.Width), Math.Min(visibleH, sourceSize.Height));
            context.DrawImage(bitmap, sourceRect, rect);
        }
    }

    private static void DrawText(DrawingContext context, Rect rect, string? value, string fontFamily, string colorHex, TextAlignmentX alignment)
    {
        if (string.IsNullOrEmpty(value) || rect.Height < 1) return;

        // Small renders (list thumbnails) need font sizes clamped to the element's own
        // scaled height, or an entirely reasonable full-canvas font size (8-40pt)
        // becomes sub-pixel noise. At Print Preview's larger scale this clamp rarely
        // engages since rect.Height is already generous.
        var fontSize = Math.Clamp(rect.Height * 0.75, 1, rect.Height);
        var typeface = new Typeface(fontFamily);
        var formatted = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, fontSize, ParseBrush(colorHex))
        {
            MaxTextWidth = Math.Max(1, rect.Width),
            MaxTextHeight = Math.Max(1, rect.Height),
            TextAlignment = alignment switch
            {
                TextAlignmentX.Center => TextAlignment.Center,
                TextAlignmentX.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
        };
        context.DrawText(formatted, rect.TopLeft);
    }

    /// <summary>Delegates to the shared ShapeRenderer (Services/ShapeRenderer.cs) so
    /// thumbnails/Print Preview always render every ShapeKind identically to the live
    /// canvas -- see that class's own doc comment for the duplication bug this
    /// replaces (a Star used to render as a plain rectangle here because this file
    /// carried its own separately-hand-maintained copy of the same switch).</summary>
    private static void DrawShape(DrawingContext context, Rect rect, ShapeElement shape) =>
        ShapeRenderer.Draw(context, rect, shape);

    /// <summary>Real image decoding when a resolver is available (both ThumbnailService
    /// and Print Preview now supply one), falling back to a plain tinted box only when
    /// there's no resolver, no asset reference, or the file can't be loaded -- never a
    /// crash, and never claiming an image exists when it doesn't.</summary>
    private static void DrawImageOrPlaceholder(DrawingContext context, Rect rect, string? assetReference, RenderOptions? options,
        PhotoMaskShape maskShape = PhotoMaskShape.Rectangle, bool flipHorizontal = false, bool flipVertical = false)
    {
        var fullPath = options?.ResolveAssetPath?.Invoke(assetReference);
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            context.DrawRectangle(ImagePlaceholderBrush, null, rect);
            return;
        }

        Bitmap? bitmap;
        try
        {
            bitmap = new Bitmap(fullPath);
        }
        catch
        {
            bitmap = null;
        }

        if (bitmap is null)
        {
            context.DrawRectangle(ImagePlaceholderBrush, null, rect);
            return;
        }

        using (bitmap)
        using (ShapeRenderer.BuildMaskClipGeometry(rect, maskShape) is { } maskGeometry ? context.PushGeometryClip(maskGeometry) : context.PushClip(rect))
        {
            // Contain-fit within the element's own box, centered -- consistent with how
            // this renderer already contain-fits the whole card into its target rect,
            // and a safe universal default across Image/Photo/Signature without needing
            // each element's own FitMode here.
            var sourceSize = bitmap.PixelSize.ToSizeWithDpi(bitmap.Dpi);
            var fitScale = Math.Min(rect.Width / sourceSize.Width, rect.Height / sourceSize.Height);
            var destSize = new Size(sourceSize.Width * fitScale, sourceSize.Height * fitScale);
            var destRect = new Rect(
                rect.Center.X - destSize.Width / 2, rect.Center.Y - destSize.Height / 2,
                destSize.Width, destSize.Height);

            if (flipHorizontal || flipVertical)
            {
                var center = destRect.Center;
                var flipTransform = Matrix.CreateTranslation(-center.X, -center.Y)
                                     * new Matrix(flipHorizontal ? -1 : 1, 0, 0, flipVertical ? -1 : 1, 0, 0)
                                     * Matrix.CreateTranslation(center.X, center.Y);
                using var flipScope = context.PushTransform(flipTransform);
                context.DrawImage(bitmap, new Rect(sourceSize), destRect);
            }
            else
            {
                context.DrawImage(bitmap, new Rect(sourceSize), destRect);
            }
        }
    }

    private static IBrush ResolveBackgroundBrush(BackgroundSettings background) => background.Kind switch
    {
        BackgroundKind.Transparent => Brushes.White,
        BackgroundKind.Gradient => new LinearGradientBrush
        {
            GradientStops =
            {
                new GradientStop(ParseColor(background.GradientStartHex ?? background.ColorHex), 0),
                new GradientStop(ParseColor(background.GradientEndHex ?? background.ColorHex), 1),
            },
        },
        _ => ParseBrush(background.ColorHex),
    };

    private static IBrush ParseBrush(string? hex)
    {
        try { return new SolidColorBrush(Color.Parse(hex ?? "#000000")); }
        catch { return Brushes.Black; }
    }

    private static Color ParseColor(string? hex)
    {
        try { return Color.Parse(hex ?? "#808080"); }
        catch { return Colors.Gray; }
    }
}
