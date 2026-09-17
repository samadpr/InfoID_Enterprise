using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Services;

namespace InfoID.Desktop.Features.Printing.Services;

/// <summary>
/// Renders one side of a CardDesignDocument to a real bitmap at a given pixel resolution
/// -- the same throwaway-Control-plus-RenderTargetBitmap technique ThumbnailService
/// already uses for Recent Cards thumbnails, generalized here so a printed card can ask
/// for real print resolution (300 DPI's worth of pixels by default) instead of
/// thumbnails' fixed 320px-wide preview size.
/// </summary>
public static class CardRasterizer
{
    public const double DefaultPrintDpi = 300;

    public static RenderTargetBitmap RenderSide(
        CardDesignDocument document, CardSide side, IDesignAssetService assetService,
        ThumbnailRenderer.RenderOptions? options = null, double dpi = DefaultPrintDpi,
        bool disableAntialiasing = false)
    {
        var widthPx = Math.Max(1, (int)Math.Round(document.WidthMm / 25.4 * dpi));
        var heightPx = Math.Max(1, (int)Math.Round(document.HeightMm / 25.4 * dpi));
        var pixelSize = new PixelSize(widthPx, heightPx);

        // Same technique as ThumbnailService.RenderToBitmap: a throwaway Control whose
        // Width/Height (device-independent units) are set to the SAME numeric value as
        // the RenderTargetBitmap's PixelSize. Bug fix: this previously passed
        // new Vector(dpi, dpi) here on the theory that the DPI vector only affects the
        // bitmap's reported/saved metadata, not the actual rasterization -- that was
        // wrong. RenderTargetBitmap.Render scales the visual's DIP-space Bounds by
        // (declaredDpi / 96) before rasterizing, so declaring 300 DPI made the host's
        // content render ~3.1x too large, clipping everything but one corner out of
        // frame -- exactly the corrupted Digital Copy PDF and garbled Black preview
        // that were reported. (96,96) is the identity scale (no zoom), and the real
        // "print DPI" comes entirely from widthPx/heightPx already being computed at
        // the requested dpi above -- a bigger pixel buffer, not a bigger declared DPI.
        // CardPdfWriter separately computes the PDF page's physical size straight from
        // the document's own WidthMm/HeightMm, so it was never affected by this bitmap
        // metadata anyway.
        var host = new RasterHost(document, side, assetService, options)
        {
            Width = widthPx,
            Height = heightPx,
        };

        // Real Antialiasing="No" support: Avalonia's own RenderOptions.EdgeMode attached
        // property, set directly on the host so the whole card rasterizes with hard,
        // un-smoothed edges instead of the default antialiased pass -- a genuine
        // rendering difference, not a setting with nothing behind it. There is no
        // equivalent per-element (text-only/image-only) knob at this DrawingContext
        // level, which is why "Only Text"/"Only Images" fall back to the same
        // (antialiased) behavior as "Yes" -- see PrintAntialiasMode's own doc comment.
        if (disableAntialiasing)
        {
            RenderOptions.SetEdgeMode(host, EdgeMode.Aliased);
        }

        host.Measure(new Size(widthPx, heightPx));
        host.Arrange(new Rect(0, 0, widthPx, heightPx));

        var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bitmap.Render(host);
        return bitmap;
    }

    /// <summary>Rotates a BGRA buffer 180 degrees in place -- reverses row order and,
    /// within each row, reverses pixel order. A real pixel-level transform (not a CSS-
    /// style display rotation), used by the Rotate 180 / Rotate 180 (Landscape/Portrait)
    /// print options.</summary>
    public static void Rotate180InPlace(byte[] bgra, int width, int height, int rowBytes)
    {
        for (var y = 0; y < height / 2; y++)
        {
            var topRow = y * rowBytes;
            var bottomRow = (height - 1 - y) * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var topPixel = topRow + x * 4;
                var bottomPixel = bottomRow + (width - 1 - x) * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    (bgra[topPixel + channel], bgra[bottomPixel + channel]) = (bgra[bottomPixel + channel], bgra[topPixel + channel]);
                }
            }
        }

        if (height % 2 == 1)
        {
            var midRow = height / 2 * rowBytes;
            for (var x = 0; x < width / 2; x++)
            {
                var left = midRow + x * 4;
                var right = midRow + (width - 1 - x) * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    (bgra[left + channel], bgra[right + channel]) = (bgra[right + channel], bgra[left + channel]);
                }
            }
        }
    }

    /// <summary>Extracts a rendered bitmap's raw BGRA8888 pixel bytes, row-by-row (never
    /// a single flat copy -- the source's own RowBytes can exceed width*4 due to
    /// platform row-padding, exactly the class of bug ImageEditingService.ProcessPixels
    /// and BarcodeRenderer.ToBitmap already guard against). Used by the "Digital Copy"
    /// feature to feed CardPdfWriter without needing to round-trip through a saved PNG
    /// file first.</summary>
    public static byte[] ExtractBgraPixels(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        using var writeable = new WriteableBitmap(size, bitmap.Dpi, PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = writeable.Lock();

        var byteCount = fb.RowBytes * fb.Size.Height;
        bitmap.CopyPixels(new PixelRect(size), fb.Address, byteCount, fb.RowBytes);

        var buffer = new byte[byteCount];
        Marshal.Copy(fb.Address, buffer, 0, byteCount);
        return buffer;
    }

    private sealed class RasterHost : Control
    {
        private readonly CardDesignDocument _document;
        private readonly CardSide _side;
        private readonly IDesignAssetService _assetService;
        private readonly ThumbnailRenderer.RenderOptions? _options;

        public RasterHost(CardDesignDocument document, CardSide side, IDesignAssetService assetService, ThumbnailRenderer.RenderOptions? options)
        {
            _document = document;
            _side = side;
            _assetService = assetService;
            _options = options;
        }

        public override void Render(DrawingContext context)
        {
            var options = _options ?? new ThumbnailRenderer.RenderOptions { ResolveAssetPath = _assetService.ResolveToFullPath };
            ThumbnailRenderer.Render(context, _document, _side, new Rect(Bounds.Size), options);
        }
    }
}
