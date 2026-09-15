using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Infrastructure.Persistence;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>Default <see cref="IThumbnailService"/>. Renders via a throwaway, never-
/// shown Control (manually Measure/Arrange'd -- standard technique for off-screen
/// Avalonia rendering without a live Window) so ThumbnailRenderer's DrawingContext-based
/// drawing can be captured into a RenderTargetBitmap and saved as PNG.</summary>
public sealed class ThumbnailService : IThumbnailService
{
    private const int ThumbnailWidthPx = 320;

    private readonly string _thumbnailFolder;
    private readonly IDesignAssetService _assetService;

    public ThumbnailService(IDesignAssetService assetService)
    {
        _assetService = assetService;
        _thumbnailFolder = Path.Combine(DatabaseLocation.GetApplicationRoot(), "Thumbnails");
    }

    public async Task<string> GenerateThumbnailAsync(CardDesignDocument document, long templateId, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_thumbnailFolder);
        var path = GetPathFor(templateId);

        using var bitmap = RenderToBitmap(document);

        var tempPath = path + ".tmp";
        await using (var stream = File.OpenWrite(tempPath))
        {
            bitmap.Save(stream);
        }
        File.Copy(tempPath, path, overwrite: true);
        File.Delete(tempPath);

        return path;
    }

    public string? GetThumbnailPath(long templateId)
    {
        var path = GetPathFor(templateId);
        return File.Exists(path) ? path : null;
    }

    public Task<IImage?> RenderInMemoryAsync(CardDesignDocument document, CancellationToken ct = default)
    {
        try
        {
            return Task.FromResult<IImage?>(RenderToBitmap(document));
        }
        catch
        {
            // Never let a bad file-backed document (corrupt asset reference, unusual
            // font, ...) break the whole Recent Cards list over one tile's thumbnail.
            return Task.FromResult<IImage?>(null);
        }
    }

    private RenderTargetBitmap RenderToBitmap(CardDesignDocument document)
    {
        var aspect = document.HeightMm <= 0 ? 0.63 : document.HeightMm / Math.Max(1, document.WidthMm);
        var height = (int)Math.Round(ThumbnailWidthPx * aspect);
        var size = new PixelSize(ThumbnailWidthPx, Math.Max(1, height));

        var host = new ThumbnailHost(document, _assetService) { Width = size.Width, Height = size.Height };
        host.Measure(new Size(size.Width, size.Height));
        host.Arrange(new Rect(0, 0, size.Width, size.Height));

        var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(host);
        return bitmap;
    }

    private string GetPathFor(long templateId) => Path.Combine(_thumbnailFolder, $"{templateId}.png");

    /// <summary>Minimal Control whose only job is to give ThumbnailRenderer a
    /// DrawingContext to draw into -- never added to a visual tree or shown. Also
    /// carries the asset service so real Image/Photo/Signature files render instead of
    /// a placeholder box.</summary>
    private sealed class ThumbnailHost : Control
    {
        private readonly CardDesignDocument _document;
        private readonly IDesignAssetService _assetService;

        public ThumbnailHost(CardDesignDocument document, IDesignAssetService assetService)
        {
            _document = document;
            _assetService = assetService;
        }

        public override void Render(DrawingContext context)
        {
            var options = new ThumbnailRenderer.RenderOptions
            {
                ResolveAssetPath = _assetService.ResolveToFullPath,
            };
            ThumbnailRenderer.Render(context, _document, CardSide.Front, new Rect(Bounds.Size), options);
        }
    }
}
