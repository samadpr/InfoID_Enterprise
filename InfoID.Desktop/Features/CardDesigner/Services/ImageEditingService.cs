using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>Which built-in color-mode preset to apply (Priority 12, "enterprise" Image
/// Editor pass -- see ImageEditorDialogViewModel for the reference/UX inspiration).</summary>
public enum ImageColorMode
{
    Color,
    Grayscale,
    Sepia,
    Negative,
}

/// <summary>Priority 12 (Add Image workflow / Image Editor). Real, non-stub image
/// editing operations used by ImageEditorDialogViewModel before an image is inserted
/// onto the canvas.
///
/// Two different techniques are used here, deliberately:
///
/// Rotate/Flip/Crop use the "throwaway, never-shown Control, manually Measure/
/// Arrange'd, rendered into a RenderTargetBitmap" technique <see cref="ThumbnailService"/>
/// already ships with -- pure geometric operations (moving/copying whole pixels, never
/// changing a pixel's own color value), which Avalonia's normal drawing pipeline
/// handles natively and safely.
///
/// AdjustColors/ApplyColorMode/RemoveBackgroundColor need to change each pixel's actual
/// color value (brightness/contrast/saturation math, grayscale/sepia conversion, color-
/// distance comparison for background removal) -- there is no safe way to do this
/// through DrawingContext compositing alone (an earlier version of this file tried an
/// alpha-overlay approximation for brightness only, and left contrast/saturation
/// disabled entirely for exactly this reason). This version reads and writes real pixel
/// bytes via WriteableBitmap + Marshal.Copy into a plain managed byte[] -- deliberately
/// NOT using C# `unsafe`/pointer code (which would also require adding
/// &lt;AllowUnsafeBlocks&gt; to the .csproj, a build-configuration change on top of
/// everything else here): Marshal.Copy moves bytes between the locked framebuffer's
/// native IntPtr and an ordinary bounds-checked managed array, so every read/write in
/// the actual per-pixel loops below is normal, safe, bounds-checked C# array indexing.
///
/// HONEST CONFIDENCE NOTE: the specific APIs this relies on --
/// Avalonia.Platform.PixelFormat.Bgra8888/AlphaFormat.Unpremul, WriteableBitmap's
/// (PixelSize, Vector, PixelFormat, AlphaFormat) constructor, ILockedFramebuffer's
/// Address/RowBytes/Size properties, and Bitmap.CopyPixels(PixelRect, IntPtr, int, int)
/// -- are all real, standard, long-established Avalonia imaging APIs used for exactly
/// this "extract/process/write back raw pixels" purpose, but this codebase has zero
/// prior usage of any of them to cross-check against, and there is no compiler in the
/// environment this was written in. If ProcessPixels (the shared helper at the bottom
/// of this file) fails to build, that's the one place to look -- every color-adjustment
/// method above it (AdjustColors, ApplyColorMode, RemoveBackgroundColor) is pure,
/// ordinary math operating on a plain byte[] and shouldn't need to change.
///
/// Unit handling for the geometric operations (Rotate/Flip/Crop): every operation works
/// in device-independent units taken from the source Bitmap's own <c>Size</c> property,
/// and every output RenderTargetBitmap is created at a fixed, hard-coded 96 DPI --
/// matching ThumbnailService's own proven configuration. See the previous revision of
/// this file (or ask) for the full DPI reasoning if needed; unchanged in this pass.
/// </summary>
public interface IImageEditingService
{
    /// <summary>Loads an image file from disk. Thin wrapper kept only so callers never
    /// need to reference Avalonia.Media.Imaging.Bitmap's constructor directly.</summary>
    Bitmap Load(string path);

    /// <summary>Rotates 90 degrees. The output genuinely swaps width/height -- callers
    /// should not assume the result keeps the input's aspect ratio.</summary>
    Bitmap Rotate90(Bitmap source, bool clockwise);

    Bitmap FlipHorizontal(Bitmap source);

    Bitmap FlipVertical(Bitmap source);

    /// <summary>Crops to <paramref name="regionFraction"/>, a rectangle expressed as
    /// 0..1 fractions of the source image's own width/height (not pixels or DIPs) --
    /// fraction-based so the caller (the crop UI) never needs to know which unit system
    /// this service settled on internally. Clamped defensively to the unit square.</summary>
    Bitmap Crop(Bitmap source, Rect regionFraction);

    /// <summary>Real, combined per-pixel brightness/contrast/saturation adjustment.
    /// Each parameter is -100..100, 0 = unchanged, clamped defensively. Brightness is a
    /// simple additive offset; contrast scales each channel around mid-gray (128);
    /// saturation interpolates each pixel toward (negative) or away from
    /// (positive, up to double) its own luminance-derived gray value. All three are
    /// computed together in one pass for a given pixel rather than three separate
    /// passes, so intermediate rounding doesn't compound.</summary>
    Bitmap AdjustColors(Bitmap source, double brightnessPercent, double contrastPercent, double saturationPercent);

    /// <summary>Applies a whole-image color-mode preset (grayscale/sepia/negative).
    /// ImageColorMode.Color returns the source unchanged.</summary>
    Bitmap ApplyColorMode(Bitmap source, ImageColorMode mode);

    /// <summary>Makes pixels within <paramref name="tolerancePercent"/> color-distance
    /// of <paramref name="targetColor"/> fully transparent -- a real (if simple)
    /// same-color background removal, comparing each pixel's RGB distance to the target
    /// color against a threshold derived from the tolerance percentage. Works best on a
    /// flat/solid-color backdrop; it is not a general subject/background segmentation
    /// tool (that would need an actual ML segmentation model, a separate library
    /// decision -- see ImageEditorDialogViewModel's doc comment on Face Detection for
    /// the same class of honest limitation).</summary>
    Bitmap RemoveBackgroundColor(Bitmap source, Color targetColor, double tolerancePercent);

    /// <summary>Saves to a new temporary PNG file (in the OS temp directory) and
    /// returns its path. Callers are responsible for deleting the temp file once
    /// they're done with it (e.g. after importing it into IDesignAssetService) -- this
    /// service does not track or clean up files it creates.</summary>
    Task<string> SaveTempPngAsync(Bitmap bitmap);
}

public sealed class ImageEditingService : IImageEditingService
{
    /// <summary>Fixed output DPI for every RenderTargetBitmap this service creates --
    /// matching ThumbnailService's own proven configuration exactly.</summary>
    private static readonly Vector OutputDpi = new(96, 96);

    public Bitmap Load(string path) => new(path);

    public Bitmap Rotate90(Bitmap source, bool clockwise)
    {
        var sourceSize = source.Size;
        var outputSize = new Size(sourceSize.Height, sourceSize.Width);
        var angleRadians = (clockwise ? 90.0 : -90.0) * Math.PI / 180.0;

        return RenderTo(outputSize, context =>
        {
            var outputCenter = new Point(outputSize.Width / 2.0, outputSize.Height / 2.0);
            var transform = Matrix.CreateTranslation(-outputCenter.X, -outputCenter.Y)
                            * Matrix.CreateRotation(angleRadians)
                            * Matrix.CreateTranslation(outputCenter.X, outputCenter.Y);

            using var rotate = context.PushTransform(transform);
            var destRect = new Rect(
                outputCenter.X - sourceSize.Width / 2.0,
                outputCenter.Y - sourceSize.Height / 2.0,
                sourceSize.Width, sourceSize.Height);
            context.DrawImage(source, new Rect(sourceSize), destRect);
        });
    }

    public Bitmap FlipHorizontal(Bitmap source) => Flip(source, scaleX: -1, scaleY: 1);

    public Bitmap FlipVertical(Bitmap source) => Flip(source, scaleX: 1, scaleY: -1);

    private static Bitmap Flip(Bitmap source, double scaleX, double scaleY)
    {
        var size = source.Size;
        return RenderTo(size, context =>
        {
            var center = new Point(size.Width / 2.0, size.Height / 2.0);
            var transform = Matrix.CreateTranslation(-center.X, -center.Y)
                            * new Matrix(scaleX, 0, 0, scaleY, 0, 0)
                            * Matrix.CreateTranslation(center.X, center.Y);

            using var flip = context.PushTransform(transform);
            var rect = new Rect(size);
            context.DrawImage(source, rect, rect);
        });
    }

    public Bitmap Crop(Bitmap source, Rect regionFraction)
    {
        var size = source.Size;

        var left = Math.Clamp(regionFraction.X, 0, 1);
        var top = Math.Clamp(regionFraction.Y, 0, 1);
        var right = Math.Clamp(regionFraction.X + regionFraction.Width, 0, 1);
        var bottom = Math.Clamp(regionFraction.Y + regionFraction.Height, 0, 1);

        if (right <= left || bottom <= top)
        {
            left = 0; top = 0; right = 1; bottom = 1;
        }

        var region = new Rect(left * size.Width, top * size.Height,
            (right - left) * size.Width, (bottom - top) * size.Height);

        return RenderTo(region.Size, context =>
        {
            context.DrawImage(source, region, new Rect(region.Size));
        });
    }

    public Bitmap AdjustColors(Bitmap source, double brightnessPercent, double contrastPercent, double saturationPercent)
    {
        brightnessPercent = Math.Clamp(brightnessPercent, -100, 100);
        contrastPercent = Math.Clamp(contrastPercent, -100, 100);
        saturationPercent = Math.Clamp(saturationPercent, -100, 100);
        if (brightnessPercent == 0 && contrastPercent == 0 && saturationPercent == 0) return source;

        var brightnessOffset = brightnessPercent / 100.0 * 255.0;
        var contrastFactor = (100.0 + contrastPercent) / 100.0;
        var saturationFactor = (100.0 + saturationPercent) / 100.0;

        return ProcessPixels(source, (buffer, rowBytes, width, height) =>
        {
            for (var y = 0; y < height; y++)
            {
                var rowStart = y * rowBytes;
                for (var x = 0; x < width; x++)
                {
                    var i = rowStart + x * 4;
                    double b = buffer[i];
                    double g = buffer[i + 1];
                    double r = buffer[i + 2];

                    r += brightnessOffset; g += brightnessOffset; b += brightnessOffset;

                    r = (r - 128.0) * contrastFactor + 128.0;
                    g = (g - 128.0) * contrastFactor + 128.0;
                    b = (b - 128.0) * contrastFactor + 128.0;

                    var gray = 0.299 * r + 0.587 * g + 0.114 * b;
                    r = gray + (r - gray) * saturationFactor;
                    g = gray + (g - gray) * saturationFactor;
                    b = gray + (b - gray) * saturationFactor;

                    buffer[i] = ClampByte(b);
                    buffer[i + 1] = ClampByte(g);
                    buffer[i + 2] = ClampByte(r);
                }
            }
        });
    }

    public Bitmap ApplyColorMode(Bitmap source, ImageColorMode mode)
    {
        if (mode == ImageColorMode.Color) return source;

        return ProcessPixels(source, (buffer, rowBytes, width, height) =>
        {
            for (var y = 0; y < height; y++)
            {
                var rowStart = y * rowBytes;
                for (var x = 0; x < width; x++)
                {
                    var i = rowStart + x * 4;
                    double b = buffer[i];
                    double g = buffer[i + 1];
                    double r = buffer[i + 2];

                    switch (mode)
                    {
                        case ImageColorMode.Grayscale:
                        {
                            var gray = 0.299 * r + 0.587 * g + 0.114 * b;
                            r = g = b = gray;
                            break;
                        }
                        case ImageColorMode.Sepia:
                        {
                            var sr = 0.393 * r + 0.769 * g + 0.189 * b;
                            var sg = 0.349 * r + 0.686 * g + 0.168 * b;
                            var sb = 0.272 * r + 0.534 * g + 0.131 * b;
                            r = sr; g = sg; b = sb;
                            break;
                        }
                        case ImageColorMode.Negative:
                            r = 255 - r; g = 255 - g; b = 255 - b;
                            break;
                    }

                    buffer[i] = ClampByte(b);
                    buffer[i + 1] = ClampByte(g);
                    buffer[i + 2] = ClampByte(r);
                }
            }
        });
    }

    public Bitmap RemoveBackgroundColor(Bitmap source, Color targetColor, double tolerancePercent)
    {
        tolerancePercent = Math.Clamp(tolerancePercent, 0, 100);
        // Maximum possible RGB distance is sqrt(255^2 * 3) =~ 441.67 -- scale the 0-100
        // tolerance slider against that so 100% tolerance removes the whole image and
        // 0% removes only exact color matches.
        var toleranceDistance = tolerancePercent / 100.0 * 441.6729559;

        return ProcessPixels(source, (buffer, rowBytes, width, height) =>
        {
            for (var y = 0; y < height; y++)
            {
                var rowStart = y * rowBytes;
                for (var x = 0; x < width; x++)
                {
                    var i = rowStart + x * 4;
                    double b = buffer[i];
                    double g = buffer[i + 1];
                    double r = buffer[i + 2];

                    var db = b - targetColor.B;
                    var dg = g - targetColor.G;
                    var dr = r - targetColor.R;
                    var distance = Math.Sqrt(dr * dr + dg * dg + db * db);

                    if (distance <= toleranceDistance)
                    {
                        buffer[i + 3] = 0; // alpha -- fully transparent
                    }
                }
            }
        });
    }

    private static byte ClampByte(double value) => (byte)Math.Clamp(value, 0, 255);

    public async Task<string> SaveTempPngAsync(Bitmap bitmap)
    {
        var path = Path.Combine(Path.GetTempPath(), $"infoid-imgedit-{Guid.NewGuid():N}.png");
        await using var stream = File.Create(path);
        bitmap.Save(stream);
        return path;
    }

    /// <summary>Shared "render this drawing into a new bitmap" helper -- the same
    /// throwaway-Control-plus-RenderTargetBitmap technique ThumbnailService.RenderToBitmap
    /// already uses, generalized to take an arbitrary draw callback and a DIP-unit
    /// output size instead of always drawing a whole CardDesignDocument.</summary>
    private static Bitmap RenderTo(Size dipSize, Action<DrawingContext> draw)
    {
        var pixelWidth = Math.Max(1, (int)Math.Round(dipSize.Width));
        var pixelHeight = Math.Max(1, (int)Math.Round(dipSize.Height));

        var host = new RenderHost(draw) { Width = pixelWidth, Height = pixelHeight };
        host.Measure(new Size(pixelWidth, pixelHeight));
        host.Arrange(new Rect(0, 0, pixelWidth, pixelHeight));

        var target = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), OutputDpi);
        target.Render(host);
        return target;
    }

    /// <summary>Shared "read every pixel, run processBuffer over the raw bytes, write
    /// the result back out as a new Bitmap" helper for the real color-adjustment
    /// methods above (AdjustColors/ApplyColorMode/RemoveBackgroundColor). See this
    /// file's own header doc comment for the full honest-confidence explanation of the
    /// APIs used here.
    ///
    /// Pixel format: Bgra8888, Unpremul -- each pixel is 4 bytes in Blue, Green, Red,
    /// Alpha order. processBuffer receives the raw row-major byte buffer, the actual
    /// row stride in bytes (rowBytes -- may exceed width*4 due to platform-specific row
    /// padding, which is exactly why callers must index using rowBytes and never assume
    /// width*4), and the image's width/height in pixels.</summary>
    private static Bitmap ProcessPixels(Bitmap source, Action<byte[], int, int, int> processBuffer)
    {
        var pixelSize = source.PixelSize;
        var writeable = new WriteableBitmap(pixelSize, source.Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);

        using (var fb = writeable.Lock())
        {
            var byteCount = fb.RowBytes * fb.Size.Height;

            // Bring the source's pixels into the new WriteableBitmap's native buffer
            // first (CopyPixels only writes to an IntPtr, it has no managed-array
            // overload), then Marshal.Copy that native buffer into an ordinary
            // managed byte[] for safe, bounds-checked processing.
            source.CopyPixels(new PixelRect(pixelSize), fb.Address, byteCount, fb.RowBytes);

            var buffer = new byte[byteCount];
            Marshal.Copy(fb.Address, buffer, 0, byteCount);

            processBuffer(buffer, fb.RowBytes, pixelSize.Width, pixelSize.Height);

            Marshal.Copy(buffer, 0, fb.Address, byteCount);
        }

        return writeable;
    }

    /// <summary>Minimal Control whose only job is to hand ImageEditingService a
    /// DrawingContext to draw into -- never added to a visual tree or shown, same role
    /// as ThumbnailService's own private ThumbnailHost.</summary>
    private sealed class RenderHost : Control
    {
        private readonly Action<DrawingContext> _draw;

        public RenderHost(Action<DrawingContext> draw)
        {
            _draw = draw;
        }

        public override void Render(DrawingContext context) => _draw(context);
    }
}
