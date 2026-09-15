using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.Rendering;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Real barcode/QR Code generation for BarcodeElement/QrCodeElement, via ZXing.Net (see
/// InfoID.Desktop.csproj's own doc comment on the package reference for why a real
/// library, not a hand-rolled implementation). Single source of truth shared by
/// CardCanvasView (the live canvas) and ThumbnailRenderer (thumbnails/Print Preview),
/// same reasoning as ShapeRenderer: one place this logic lives, so both render paths
/// can never drift out of sync.
///
/// Generates at a fixed, content-driven pixel resolution (NOT the element's current
/// on-screen size) and caches the result by every parameter that affects the output
/// pixels -- callers then scale-to-fit (letterboxed, aspect-preserving, same as an
/// ImageElement's own "Fit" mode) into whatever rect the element currently occupies.
/// This decouples expensive re-encoding from interactive pan/zoom/move/resize (which
/// change the on-screen rect constantly but not the barcode's actual content), and
/// guarantees the code's own modules are never non-uniformly stretched -- which for a
/// 2D code especially can break scanning.
///
/// "QRCode Format: Industry" from the ID-ALL reference this was modeled against has no
/// equivalent in ZXing.Net's QR encoder (it isn't a distinct real encoding mode there,
/// unlike Standard vs GS1 which map directly to QrCodeEncodingOptions.GS1Format) -- not
/// offered here rather than faked. Likewise "Optimize" (ID-ALL's toggle for
/// numeric/alphanumeric/byte mode auto-selection) isn't a separate option here because
/// ZXing.Net's encoder already always performs that optimization automatically; there's
/// no real "off" state to expose.
/// </summary>
public static class BarcodeRenderer
{
    private const int QrPixelSize = 480;
    private const int LinearPixelWidth = 640;
    private const int LinearPixelHeight = 220;

    private static readonly Dictionary<string, WriteableBitmap?> Cache = new();

    /// <summary>Never disposed by callers -- Cache owns every bitmap's lifetime for the
    /// process's duration, same "never evicted, fine for one editing session's realistic
    /// distinct-barcode count" reasoning CardCanvasView's own _bitmapCache already
    /// uses.</summary>
    public static WriteableBitmap? TryGenerate(QrCodeElement qr)
    {
        var key = string.Join('|', "qr", qr.Value, qr.ErrorCorrection, qr.Version, qr.MaskPattern,
            qr.CharacterSet, qr.Format, qr.ForegroundColorHex, qr.BackgroundColorHex, qr.MarginMm);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        WriteableBitmap? result;
        try
        {
            var options = new QrCodeEncodingOptions
            {
                Width = QrPixelSize,
                Height = QrPixelSize,
                Margin = Math.Max(0, (int)Math.Round(qr.MarginMm)),
                ErrorCorrection = qr.ErrorCorrection switch
                {
                    QrErrorCorrection.Low => ZXing.QrCode.Internal.ErrorCorrectionLevel.L,
                    QrErrorCorrection.Quartile => ZXing.QrCode.Internal.ErrorCorrectionLevel.Q,
                    QrErrorCorrection.High => ZXing.QrCode.Internal.ErrorCorrectionLevel.H,
                    _ => ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
                },
                GS1Format = qr.Format == QrFormat.Gs1,
            };
            if (qr.Version is { } version) options.QrVersion = version;
            if (qr.MaskPattern is { } mask) options.QrMaskPattern = mask;
            if (!string.IsNullOrWhiteSpace(qr.CharacterSet)) options.CharacterSet = qr.CharacterSet;

            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = options,
                Renderer = BuildRenderer(qr.ForegroundColorHex, qr.BackgroundColorHex),
            };

            result = ToBitmap(writer.Write(qr.Value ?? string.Empty));
        }
        catch
        {
            // Invalid combination (e.g. content too large for a forced Version), or any
            // other encoder failure -- caller falls back to a placeholder, never a crash.
            result = null;
        }

        Cache[key] = result;
        return result;
    }

    public static WriteableBitmap? TryGenerate(BarcodeElement barcode)
    {
        var key = string.Join('|', "bc", barcode.Symbology, barcode.Value,
            barcode.ForegroundColorHex, barcode.BackgroundColorHex, barcode.QuietZoneMm);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        WriteableBitmap? result;
        try
        {
            var format = ToZXingFormat(barcode.Symbology);
            var options = new EncodingOptions
            {
                Width = LinearPixelWidth,
                Height = LinearPixelHeight,
                Margin = Math.Max(0, (int)Math.Round(barcode.QuietZoneMm)),
            };

            var writer = new BarcodeWriterPixelData
            {
                Format = format,
                Options = options,
                Renderer = BuildRenderer(barcode.ForegroundColorHex, barcode.BackgroundColorHex),
            };

            result = ToBitmap(writer.Write(barcode.Value ?? string.Empty));
        }
        catch
        {
            // Common real failures: wrong value length/checksum for EAN/UPC, odd-length
            // input for ITF, unsupported characters for Code39, etc. -- these are
            // genuine "this value is invalid for this symbology" errors, not bugs, so
            // degrading to a placeholder (rather than crashing) is the correct outcome,
            // not a fallback being used to paper over a broken encoder.
            result = null;
        }

        Cache[key] = result;
        return result;
    }

    private static BarcodeFormat ToZXingFormat(BarcodeSymbology symbology) => symbology switch
    {
        BarcodeSymbology.Code39 => BarcodeFormat.CODE_39,
        BarcodeSymbology.Ean13 => BarcodeFormat.EAN_13,
        BarcodeSymbology.Ean8 => BarcodeFormat.EAN_8,
        BarcodeSymbology.Upc => BarcodeFormat.UPC_A,
        BarcodeSymbology.Itf => BarcodeFormat.ITF,
        BarcodeSymbology.DataMatrix => BarcodeFormat.DATA_MATRIX,
        BarcodeSymbology.Pdf417 => BarcodeFormat.PDF_417,
        BarcodeSymbology.Aztec => BarcodeFormat.AZTEC,
        _ => BarcodeFormat.CODE_128,
    };

    private static PixelDataRenderer BuildRenderer(string foregroundHex, string backgroundHex) => new()
    {
        Foreground = new PixelDataRenderer.Color(ToArgb(foregroundHex, fallback: unchecked((int)0xFF000000))),
        Background = new PixelDataRenderer.Color(ToArgb(backgroundHex, fallback: unchecked((int)0xFFFFFFFF))),
    };

    private static int ToArgb(string hex, int fallback)
    {
        try { return unchecked((int)Color.Parse(hex).ToUInt32()); }
        catch { return fallback; }
    }

    /// <summary>PixelData.Pixels is RGBA8888, row-major, tightly packed (width*4 bytes
    /// per row, no padding) -- confirmed by direct test against a known foreground/
    /// background color pair (ZXing.Net's own documentation doesn't state this
    /// explicitly). Do not change to Bgra8888 without re-verifying, or foreground/
    /// background colors swap red and blue.
    ///
    /// Copied row-by-row rather than one flat Marshal.Copy: the DESTINATION
    /// WriteableBitmap's own row stride (fb.RowBytes) can exceed width*4 due to
    /// platform-specific row padding (same reasoning ImageEditingService.ProcessPixels
    /// already documents for exactly this class of bug) -- a single flat copy would
    /// silently misalign every row after the first on a platform where that padding is
    /// non-zero, corrupting the image instead of crashing.</summary>
    private static WriteableBitmap ToBitmap(PixelData pixelData)
    {
        var size = new PixelSize(pixelData.Width, pixelData.Height);
        var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);

        using var fb = bitmap.Lock();
        var sourceRowBytes = pixelData.Width * 4;
        var rowsToCopy = Math.Min(pixelData.Height, fb.Size.Height);
        for (var y = 0; y < rowsToCopy; y++)
        {
            Marshal.Copy(pixelData.Pixels, y * sourceRowBytes, IntPtr.Add(fb.Address, y * fb.RowBytes), sourceRowBytes);
        }

        return bitmap;
    }
}
