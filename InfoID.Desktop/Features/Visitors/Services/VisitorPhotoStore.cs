using System;
using System.IO;
using System.Threading.Tasks;
using InfoID.Infrastructure.Persistence;
using SkiaSharp;

namespace InfoID.Desktop.Features.Visitors.Services;

/// <summary>
/// Visitor photos, kept in InfoID's Assets folder under "Visitors" (so Backup includes
/// them). Each photo is checked by actually decoding it, turned upright from the phone's
/// orientation tag, reduced to at most 1200 px on its longest side and saved as JPEG under
/// a random name. The stored reference is relative ("Visitors/….jpg").
/// </summary>
public sealed class VisitorPhotoStore
{
    public const string FolderName = "Visitors";
    private const long MaxSourceBytes = 25L * 1024 * 1024;
    private const int MaxSide = 1200;

    private readonly string _assetsDirectory;

    public VisitorPhotoStore() : this(DatabaseLocation.GetAssetsDirectory()) { }

    /// <summary>For tests: a different Assets folder.</summary>
    public VisitorPhotoStore(string assetsDirectory) => _assetsDirectory = assetsDirectory;

    private string VisitorsDirectory => Path.Combine(_assetsDirectory, FolderName);

    /// <summary>Full path of a stored photo, or null when the reference is empty or the file is gone.</summary>
    public string? GetFullPath(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var path = SafeFullPath(reference);
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>Copies an image into the store. Throws InvalidOperationException with a
    /// readable reason when the file is missing, too large or not an image InfoID can read.</summary>
    public Task<string> ImportAsync(string sourcePath) => Task.Run(() => Import(sourcePath));

    /// <summary>Deletes a stored photo (only files inside the Visitors photo folder).</summary>
    public void Delete(string? reference)
    {
        var path = string.IsNullOrWhiteSpace(reference) ? null : SafeFullPath(reference);
        if (path is null) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            InfoID.Shared.Logging.AppLog.For<VisitorPhotoStore>().Warn("Couldn't delete a visitor photo", ex);
        }
    }

    /// <summary>The checks of <see cref="ImportAsync"/> without copying: null when the file
    /// is usable, otherwise the reason it isn't.</summary>
    public static string? CheckImage(string sourcePath)
    {
        try
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists) return "Photo file not found.";
            if (info.Length > MaxSourceBytes) return "Photo is larger than 25 MB.";
            using var codec = SKCodec.Create(sourcePath);
            return codec is null ? UnreadableMessage(sourcePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "Photo can't be opened: " + ex.Message;
        }
    }

    private string Import(string sourcePath)
    {
        var problem = CheckImage(sourcePath);
        if (problem is not null) throw new InvalidOperationException(problem);

        using var codec = SKCodec.Create(sourcePath) ?? throw new InvalidOperationException(UnreadableMessage(sourcePath));
        using var decoded = SKBitmap.Decode(codec) ?? throw new InvalidOperationException(UnreadableMessage(sourcePath));
        using var upright = ApplyOrientation(decoded, codec.EncodedOrigin);
        using var sized = Shrink(upright);

        Directory.CreateDirectory(VisitorsDirectory);
        var reference = $"{FolderName}/{Guid.NewGuid():N}.jpg";
        var destination = SafeFullPath(reference)!;
        using (var image = SKImage.FromBitmap(sized))
        using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90))
        using (var stream = File.Create(destination))
        {
            data.SaveTo(stream);
        }
        return reference;
    }

    private static string UnreadableMessage(string path) =>
        Path.GetExtension(path).Equals(".heic", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".heif", StringComparison.OrdinalIgnoreCase)
            ? "iPhone HEIC photos can't be read. Save the photo as JPG or PNG."
            : "This file isn't an image InfoID can read (use JPG or PNG).";

    /// <summary>Resolves a reference inside the Visitors folder; null for anything outside it.</summary>
    private string? SafeFullPath(string reference)
    {
        var root = Path.GetFullPath(VisitorsDirectory) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(_assetsDirectory, reference.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static SKBitmap Shrink(SKBitmap source)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= MaxSide) return source.Copy();
        var scale = (double)MaxSide / longest;
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
        return source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
               ?? throw new InvalidOperationException("The photo couldn't be resized.");
    }

    /// <summary>Phone photos are often stored sideways with an orientation tag; draw them upright.</summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return source.Copy();

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(result);
        var w = source.Width;
        var h = source.Height;
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, w / 2f, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, w / 2f, h / 2f); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, h / 2f); break;
            case SKEncodedOrigin.LeftTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); canvas.Scale(1, -1, 0, h / 2f); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(h, 0); canvas.RotateDegrees(90); canvas.Scale(-1, -1, w / 2f, h / 2f); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, w); canvas.RotateDegrees(270); break;
        }
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }
}
