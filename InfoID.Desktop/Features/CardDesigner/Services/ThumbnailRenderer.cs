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
        public Func<ImageElement, string?>? ResolveImagePath { get; init; }
        /// <summary>The value a QR code / barcode encodes for the current record
        /// (CodeValueBinding). Null, or a null result, encodes the element's Value.</summary>
        public Func<DesignerElement, string?>? ResolveCodeValue { get; init; }
        public bool ShowBleedAndSafeZone { get; init; }

        /// <summary>Trial mode: draws "INFOID TRIAL VERSION" over the card. Part of the
        /// render itself, so it is in every printed page, PDF and preview made from it.</summary>
        public bool TrialWatermark { get; init; }

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

            if (options?.TrialWatermark == true)
                InfoID.Desktop.Features.Trial.Services.TrialWatermark.Draw(context, cardRect);
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

        // Same opacity and rotation as the design canvas (CardCanvasView.DrawElement):
        // rotate about the element's own centre, so a rotated or faded element prints
        // exactly as designed instead of straight and solid.
        using var opacityScope = context.PushOpacity(Math.Clamp(element.Opacity, 0, 1));
        var center = rect.Center;
        using var rotationScope = context.PushTransform(
            Matrix.CreateTranslation(-center.X, -center.Y)
            * Matrix.CreateRotation(element.Rotation * Math.PI / 180.0)
            * Matrix.CreateTranslation(center.X, center.Y));

        switch (element)
        {
            case TextElement text:
                var textValue = options?.ResolveText?.Invoke(text) ?? text.Text;
                CardTextRenderer.Draw(context, rect, textValue, text.FontFamily,
                    FontPx(text.FontSize, scale), FontPx(text.MinFontSize, scale), ParseBrush(text.ColorHex),
                    text.Bold, text.Italic, text.Underline, text.HorizontalAlignment, text.VerticalAlignment);
                break;
            case DataFieldElement field:
                var fieldValue = options?.ResolveField?.Invoke(field) ?? $"{{{{{field.FieldKey}}}}}";
                CardTextRenderer.Draw(context, rect, fieldValue, field.FontFamily,
                    FontPx(field.FontSize, scale), FontPx(CardTextRenderer.DefaultMinFontSize, scale), ParseBrush(field.ColorHex),
                    horizontal: field.HorizontalAlignment);
                break;
            case DateTimeElement dateTime:
                CardTextRenderer.Draw(context, rect, CardTextRenderer.FormatDateTime(dateTime), dateTime.FontFamily,
                    FontPx(dateTime.FontSize, scale), FontPx(CardTextRenderer.DefaultMinFontSize, scale), ParseBrush(dateTime.ColorHex),
                    dateTime.Bold, dateTime.Italic, dateTime.Underline, dateTime.HorizontalAlignment, dateTime.VerticalAlignment);
                break;
            case ShapeElement shape:
                DrawShape(context, rect, shape);
                break;
            case ImageElement image:
                {
                    // Photo folders: the printing caller can supply a per-record photo.
                    // null = normal image, "" = draw nothing (never a placeholder box on a printed card).
                    var overridePath = options?.ResolveImagePath?.Invoke(image);

                    var fullPath = overridePath is null
                        ? options?.ResolveAssetPath?.Invoke(image.AssetReference)
                        : overridePath;

                    var drawn = TryDrawImageFile(context, rect, fullPath,
                        image.FitMode, image.CornerRadius * scale,
                        image.CropX, image.CropY, image.CropZoom,
                        image.MaskShape, image.FlipHorizontal, image.FlipVertical);

                    if (!drawn && overridePath is null)
                        context.DrawRectangle(ImagePlaceholderBrush, null, rect);
                    break;
                }
            case PhotoElement photo:
                DrawImageOrPlaceholder(context, rect, photo.AssetReference, options);
                break;
            case SignatureElement signature:
                DrawImageOrPlaceholder(context, rect, signature.AssetReference, options);
                break;
            case BarcodeElement barcode:
                DrawBarcodeOrQr(context, rect, BarcodeRenderer.TryGenerate(barcode, options?.ResolveCodeValue?.Invoke(barcode)));
                break;
            case QrCodeElement qr:
                DrawBarcodeOrQr(context, rect, BarcodeRenderer.TryGenerate(qr, options?.ResolveCodeValue?.Invoke(qr)));
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

    private static string? ResolveImageFullPath(ImageElement image, RenderOptions options)
    {
        var overridePath = options.ResolveImagePath?.Invoke(image);

        if (overridePath is null)
            return options.ResolveAssetPath?.Invoke(image.AssetReference);

        return overridePath.Length == 0 ? null : overridePath;
    }

    /// <summary>The canvas draws text at FontSize x Zoom pixels while drawing
    /// (96/25.4) x Zoom pixels per millimetre, so one font unit = 25.4/96 mm. Here the card
    /// is drawn at <paramref name="scale"/> pixels per millimetre, so the same text is
    /// FontSize x scale x 25.4/96 pixels -- the printed size matches the designer exactly
    /// at any DPI (thumbnail, preview or 300 DPI print).</summary>
    private static double FontPx(double fontSize, double scale) => fontSize * scale * 25.4 / 96.0;

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
    //private static void DrawImageOrPlaceholder(DrawingContext context, Rect rect, string? assetReference, RenderOptions? options,
    //    PhotoMaskShape maskShape = PhotoMaskShape.Rectangle, bool flipHorizontal = false, bool flipVertical = false)
    //{
    //    var fullPath = options?.ResolveAssetPath?.Invoke(assetReference);
    //    if (string.IsNullOrWhiteSpace(fullPath))
    //    {
    //        context.DrawRectangle(ImagePlaceholderBrush, null, rect);
    //        return;
    //    }

    //    Bitmap? bitmap;
    //    try
    //    {
    //        bitmap = new Bitmap(fullPath);
    //    }
    //    catch
    //    {
    //        bitmap = null;
    //    }

    //    if (bitmap is null)
    //    {
    //        context.DrawRectangle(ImagePlaceholderBrush, null, rect);
    //        return;
    //    }

    //    using (bitmap)
    //    using (ShapeRenderer.BuildMaskClipGeometry(rect, maskShape) is { } maskGeometry ? context.PushGeometryClip(maskGeometry) : context.PushClip(rect))
    //    {
    //        // Contain-fit within the element's own box, centered -- consistent with how
    //        // this renderer already contain-fits the whole card into its target rect,
    //        // and a safe universal default across Image/Photo/Signature without needing
    //        // each element's own FitMode here.
    //        var sourceSize = bitmap.PixelSize.ToSizeWithDpi(bitmap.Dpi);
    //        var fitScale = Math.Min(rect.Width / sourceSize.Width, rect.Height / sourceSize.Height);
    //        var destSize = new Size(sourceSize.Width * fitScale, sourceSize.Height * fitScale);
    //        var destRect = new Rect(
    //            rect.Center.X - destSize.Width / 2, rect.Center.Y - destSize.Height / 2,
    //            destSize.Width, destSize.Height);

    //        if (flipHorizontal || flipVertical)
    //        {
    //            var center = destRect.Center;
    //            var flipTransform = Matrix.CreateTranslation(-center.X, -center.Y)
    //                                 * new Matrix(flipHorizontal ? -1 : 1, 0, 0, flipVertical ? -1 : 1, 0, 0)
    //                                 * Matrix.CreateTranslation(center.X, center.Y);
    //            using var flipScope = context.PushTransform(flipTransform);
    //            context.DrawImage(bitmap, new Rect(sourceSize), destRect);
    //        }
    //        else
    //        {
    //            context.DrawImage(bitmap, new Rect(sourceSize), destRect);
    //        }
    //    }
    //}


    /// <summary>Photo and Signature elements (and anything else without its own fit
    /// settings): contain-fit, falling back to a plain tinted box when there's no
    /// resolver, no asset reference, or the file can't be loaded.</summary>
    private static void DrawImageOrPlaceholder(DrawingContext context, Rect rect, string? assetReference, RenderOptions? options,
        PhotoMaskShape maskShape = PhotoMaskShape.Rectangle, bool flipHorizontal = false, bool flipVertical = false)
    {
        var fullPath = options?.ResolveAssetPath?.Invoke(assetReference);

        if (!TryDrawImageFile(context, rect, fullPath, ImageFitMode.Fit, 0, 0, 0, 1.0, maskShape, flipHorizontal, flipVertical))
            context.DrawRectangle(ImagePlaceholderBrush, null, rect);
    }

    /// <summary>Loads and draws an image file with the same fit, crop, corner and mask
    /// rules as CardCanvasView.DrawImage, so printed cards match the design canvas.
    /// Returns false (drawing nothing) if the path is empty or the file can't be loaded.</summary>
    private static bool TryDrawImageFile(DrawingContext context, Rect rect, string? fullPath,
        ImageFitMode fitMode, double cornerRadius,
        double cropX, double cropY, double cropZoom,
        PhotoMaskShape maskShape, bool flipHorizontal, bool flipVertical)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return false;

        Bitmap? bitmap;
        try
        {
            bitmap = new Bitmap(fullPath);
        }
        catch
        {
            return false;
        }

        using (bitmap)
        using (ShapeRenderer.BuildMaskClipGeometry(rect, maskShape) is { } maskGeometry
                   ? context.PushGeometryClip(maskGeometry)
                   : maskShape == PhotoMaskShape.RoundedRectangle || cornerRadius > 0
                       ? context.PushClip(new RoundedRect(rect, Math.Min(cornerRadius, Math.Min(rect.Width, rect.Height) / 2)))
                       : context.PushClip(rect))
        {
            var sourceSize = bitmap.PixelSize.ToSizeWithDpi(bitmap.Dpi);
            Rect sourceRect;
            Rect destRect;

            switch (fitMode)
            {
                case ImageFitMode.Stretch:
                    sourceRect = new Rect(sourceSize);
                    destRect = rect;
                    break;

                case ImageFitMode.Fill:
                    // Same pan + zoom formula as CardCanvasView.DrawImage.
                    var fillScale = Math.Max(rect.Width / sourceSize.Width, rect.Height / sourceSize.Height) * Math.Max(cropZoom, 0.1);
                    var visibleW = rect.Width / fillScale;
                    var visibleH = rect.Height / fillScale;
                    var maxOffsetX = Math.Max(0, sourceSize.Width - visibleW);
                    var maxOffsetY = Math.Max(0, sourceSize.Height - visibleH);
                    var offsetX = Math.Clamp((sourceSize.Width - visibleW) / 2 + cropX, 0, maxOffsetX);
                    var offsetY = Math.Clamp((sourceSize.Height - visibleH) / 2 + cropY, 0, maxOffsetY);
                    sourceRect = new Rect(offsetX, offsetY, Math.Min(visibleW, sourceSize.Width), Math.Min(visibleH, sourceSize.Height));
                    destRect = rect;
                    break;

                default: // Fit
                    var fitScale = Math.Min(rect.Width / sourceSize.Width, rect.Height / sourceSize.Height);
                    var fitW = sourceSize.Width * fitScale;
                    var fitH = sourceSize.Height * fitScale;
                    sourceRect = new Rect(sourceSize);
                    destRect = new Rect(rect.X + (rect.Width - fitW) / 2, rect.Y + (rect.Height - fitH) / 2, fitW, fitH);
                    break;
            }

            if (flipHorizontal || flipVertical)
            {
                var center = rect.Center;
                var flipTransform = Matrix.CreateTranslation(-center.X, -center.Y)
                                     * new Matrix(flipHorizontal ? -1 : 1, 0, 0, flipVertical ? -1 : 1, 0, 0)
                                     * Matrix.CreateTranslation(center.X, center.Y);
                using var flipScope = context.PushTransform(flipTransform);
                context.DrawImage(bitmap, sourceRect, destRect);
            }
            else
            {
                context.DrawImage(bitmap, sourceRect, destRect);
            }
        }

        return true;
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
