using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Face detection for the Image Editor's "Detect Face" auto-crop. Runs the bundled
/// UltraFace RFB-320 model (Assets/Models/version-RFB-320.onnx, MIT-licensed --
/// github.com/Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB) through ONNX Runtime,
/// entirely offline/on-device -- the photo never leaves the machine and no network call
/// is ever made.
/// </summary>
public interface IFaceDetectionService
{
    /// <summary>Runs face detection against <paramref name="source"/> and returns the raw
    /// bounding box of the most confident detected face, as 0..1 fractions of the source
    /// image's own width/height -- the exact same convention ImageEditingService.Crop and
    /// ImageEditorDialogViewModel.CropRegionFraction already use. This is a tight box
    /// around just the face -- the caller (ImageEditorDialogViewModel) is responsible for
    /// padding it out into a usable head-and-shoulders crop via its own user-adjustable
    /// Margin*Percent properties. Returns null if no face was found above the confidence
    /// threshold, or if the model couldn't be loaded/run for any reason -- this is always
    /// an enhancement on top of the manual crop tool, never a hard requirement, so it
    /// never throws.</summary>
    Task<Rect?> DetectFaceAsync(Bitmap source, CancellationToken ct = default);
}

public sealed class FaceDetectionService : IFaceDetectionService, IDisposable
{
    // Fixed by the bundled model's own input shape (1, 3, 240, 320).
    private const int InputWidth = 320;
    private const int InputHeight = 240;

    private const float ConfidenceThreshold = 0.7f;

    private readonly Lazy<InferenceSession?> _session = new(LoadSession);

    private static InferenceSession? LoadSession()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "version-RFB-320.onnx");
            return File.Exists(path) ? new InferenceSession(path) : null;
        }
        catch
        {
            // Corrupt model file, unsupported ONNX Runtime execution provider on this
            // machine, etc. -- every caller just sees "no face found".
            return null;
        }
    }

    public Task<Rect?> DetectFaceAsync(Bitmap source, CancellationToken ct = default) =>
        Task.Run(() => DetectFace(source), ct);

    private Rect? DetectFace(Bitmap source)
    {
        if (_session.Value is not { } session) return null;

        try
        {
            using var resized = ResizeTo(source, new PixelSize(InputWidth, InputHeight));
            var inputName = session.InputMetadata.Keys.First();
            var input = NamedOnnxValue.CreateFromTensor(inputName, ExtractInputTensor(resized));

            using var results = session.Run(new[] { input });

            // Selected by output shape rather than by name -- the official export names
            // these "scores"/"boxes", but shape (last dimension 2 vs 4) is what actually
            // identifies them and doesn't depend on that naming holding exactly.
            var scores = results.First(r => r.AsTensor<float>().Dimensions[^1] == 2).AsTensor<float>();
            var boxes = results.First(r => r.AsTensor<float>().Dimensions[^1] == 4).AsTensor<float>();

            var anchorCount = scores.Dimensions[1];
            var bestScore = ConfidenceThreshold;
            var bestIndex = -1;

            // Only the single most confident detection is used -- the Image Editor
            // needs one primary-subject crop, not a multi-face candidate list, so
            // picking the top-scoring anchor directly (instead of running full
            // multi-box non-max-suppression) already gives the right answer.
            for (var i = 0; i < anchorCount; i++)
            {
                var faceScore = scores[0, i, 1];
                if (faceScore > bestScore)
                {
                    bestScore = faceScore;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0) return null;

            return ToUnitBox(
                boxes[0, bestIndex, 0], boxes[0, bestIndex, 1],
                boxes[0, bestIndex, 2], boxes[0, bestIndex, 3]);
        }
        catch
        {
            // Unexpected model output shape, an ONNX Runtime error, etc. -- degrade to
            // "no face found" rather than surface a crash out of the Image Editor.
            return null;
        }
    }

    /// <summary>The model's regressed box coordinates are already normalized 0..1
    /// against the input frame, but anchors near an edge can regress slightly outside
    /// it -- clamped defensively to the unit square here so every caller can trust the
    /// result is a valid crop-fraction rectangle.</summary>
    private static Rect? ToUnitBox(float x1, float y1, float x2, float y2)
    {
        var left = Math.Clamp(x1, 0, 1);
        var top = Math.Clamp(y1, 0, 1);
        var right = Math.Clamp(x2, 0, 1);
        var bottom = Math.Clamp(y2, 0, 1);

        if (right <= left || bottom <= top) return null;
        return new Rect(left, top, right - left, bottom - top);
    }

    // ------------------------------------------------------- image -> tensor plumbing ----

    /// <summary>Same "throwaway Control + RenderTargetBitmap" technique ImageEditingService
    /// and ThumbnailService already use elsewhere in this codebase, specialized to always
    /// resize to the model's fixed input size.</summary>
    private static Bitmap ResizeTo(Bitmap source, PixelSize target)
    {
        var host = new ResizeHost(source, target) { Width = target.Width, Height = target.Height };
        host.Measure(new Size(target.Width, target.Height));
        host.Arrange(new Rect(0, 0, target.Width, target.Height));

        var result = new RenderTargetBitmap(target, new Vector(96, 96));
        result.Render(host);
        return result;
    }

    /// <summary>Reads the resized bitmap's raw BGRA pixels (same WriteableBitmap +
    /// CopyPixels + Marshal.Copy technique as ImageEditingService.ProcessPixels) and
    /// converts to the tensor layout UltraFace expects: RGB channel order, each value
    /// normalized to (pixel - 127) / 128, laid out NCHW (channel, then row, then column)
    /// with a leading batch dimension of 1.</summary>
    private static DenseTensor<float> ExtractInputTensor(Bitmap resized)
    {
        var pixelSize = resized.PixelSize;
        var writeable = new WriteableBitmap(pixelSize, resized.Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var tensor = new DenseTensor<float>(new[] { 1, 3, pixelSize.Height, pixelSize.Width });

        using var fb = writeable.Lock();
        var byteCount = fb.RowBytes * fb.Size.Height;
        resized.CopyPixels(new PixelRect(pixelSize), fb.Address, byteCount, fb.RowBytes);

        var buffer = new byte[byteCount];
        Marshal.Copy(fb.Address, buffer, 0, byteCount);

        for (var y = 0; y < pixelSize.Height; y++)
        {
            var rowStart = y * fb.RowBytes;
            for (var x = 0; x < pixelSize.Width; x++)
            {
                var i = rowStart + x * 4;
                tensor[0, 0, y, x] = (buffer[i + 2] - 127f) / 128f; // R
                tensor[0, 1, y, x] = (buffer[i + 1] - 127f) / 128f; // G
                tensor[0, 2, y, x] = (buffer[i] - 127f) / 128f;     // B
            }
        }

        return tensor;
    }

    /// <summary>Minimal Control whose only job is to draw <see cref="_source"/> stretched
    /// to fill <see cref="_targetSize"/> -- never added to a visual tree or shown, same
    /// role as ImageEditingService's own private RenderHost.</summary>
    private sealed class ResizeHost : Control
    {
        private readonly Bitmap _source;
        private readonly PixelSize _targetSize;

        public ResizeHost(Bitmap source, PixelSize targetSize)
        {
            _source = source;
            _targetSize = targetSize;
        }

        public override void Render(DrawingContext context) =>
            context.DrawImage(_source, new Rect(_source.Size), new Rect(0, 0, _targetSize.Width, _targetSize.Height));
    }

    public void Dispose()
    {
        if (_session.IsValueCreated) _session.Value?.Dispose();
    }
}
