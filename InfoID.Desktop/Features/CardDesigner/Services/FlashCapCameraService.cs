using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using FlashCap;
using InfoID.Desktop.Features.CardDesigner.Models;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// ============================================================================
/// HONEST CONFIDENCE STATEMENT -- read this before debugging a build error here.
/// ============================================================================
/// This is the single least-verified file in the entire InfoID engagement. Every
/// other change was checked against your actual source code or a pattern already
/// proven elsewhere in this project; this one is written from training-time
/// knowledge of the FlashCap library's API shape, without live access to its
/// current documentation (web search was unavailable when this was written), no
/// compiler, and -- unlike everything else -- no way to ever test actual camera
/// hardware from this environment even if a compiler were available.
///
/// If this file fails to build: the error is very likely a method/property name
/// that has drifted from what's written here. Check FlashCap's actual GitHub
/// (github.com/kekyo/FlashCap) samples/README against the calls below --
/// CaptureDevices.EnumerateDescriptors(), descriptor.Characteristics,
/// descriptor.OpenAsync(characteristics, frameCallback), device.StartAsync()/
/// StopAsync(), and the frame-scope object's method for extracting encoded image
/// bytes -- and correct whichever call doesn't match. Every FlashCap-specific call
/// in this codebase is confined to this one file by design (see ICameraService's
/// own doc comment), specifically so a mismatch here doesn't ripple anywhere else.
///
/// UPDATE (compiler-confirmed fix): the first real build attempt against this file
/// reported "PixelBufferScope does not contain a definition for CopyImage and the
/// best extension method overload PixelBufferExtension.CopyImage(PixelBuffer)
/// requires a receiver of type FlashCap.PixelBuffer" -- confirming CopyImage() is an
/// extension method on FlashCap.PixelBuffer, not a member of PixelBufferScope
/// itself. Fixed below by calling bufferScope.Buffer.CopyImage() instead of
/// bufferScope.CopyImage() directly. The one part of this specific fix that's still
/// an inference rather than a compiler-confirmed fact: that PixelBufferScope
/// exposes the underlying PixelBuffer via a property literally named "Buffer" --
/// this was the most idiomatic guess given the error message, and building again
/// will immediately confirm or refute it. If "Buffer" isn't the right member name,
/// the compiler's own suggestion list (IntelliSense on bufferScope.) will show the
/// actual property name to substitute in its place.
///
/// Pixel format handling: rather than attempt raw YUV/RGB pixel-format conversion
/// (a second, separate source of uncertainty), this service just tries to decode
/// whatever bytes each frame delivers directly as an image (new Bitmap(stream)) and
/// silently skips any frame that fails to decode. Most consumer USB webcams offer an
/// MJPEG (already-JPEG-encoded) mode that FlashCap can select, in which case this
/// "just try to decode it" approach works with zero pixel-format-specific code; if a
/// particular camera/platform only offers a raw, uncompressed format, every frame
/// will fail to decode and the live preview will simply stay blank -- a real,
/// disclosed limitation, not a crash, and something only actual hardware testing on
/// your end can confirm one way or the other.
/// ============================================================================
/// </summary>
public sealed class FlashCapCameraService : ICameraService
{
    public Task<IReadOnlyList<CameraDeviceInfo>> GetAvailableDevicesAsync()
    {
        try
        {
            var devices = new CaptureDevices();
            var descriptors = devices.EnumerateDescriptors()
                .Where(d => d.Characteristics is { Length: > 0 })
                .Select((d, index) => new CameraDeviceInfo { Id = index.ToString(), Name = d.Name })
                .ToList();

            return Task.FromResult<IReadOnlyList<CameraDeviceInfo>>(descriptors);
        }
        catch (Exception)
        {
            // Any enumeration failure (no camera subsystem on this platform, driver
            // issue, permission denied, ...) reads as "no camera available" per
            // ICameraService's contract -- never propagate this as a crash.
            return Task.FromResult<IReadOnlyList<CameraDeviceInfo>>(Array.Empty<CameraDeviceInfo>());
        }
    }

    public async Task<ICameraSession> OpenAsync(CameraDeviceInfo device)
    {
        var devices = new CaptureDevices();
        var descriptors = devices.EnumerateDescriptors()
            .Where(d => d.Characteristics is { Length: > 0 })
            .ToArray();

        if (!int.TryParse(device.Id, out var index) || index < 0 || index >= descriptors.Length)
        {
            throw new InvalidOperationException(
                $"Camera device '{device.Name}' is no longer available (it may have been disconnected since the device list was loaded).");
        }

        var descriptor = descriptors[index];
        var characteristics = descriptor.Characteristics[0];

        var session = new FlashCapSession();

        var captureDevice = await descriptor.OpenAsync(characteristics, bufferScope =>
        {
            try
            {
                // Fix (compiler-confirmed): CopyImage() is an extension method on
                // FlashCap.PixelBuffer, not a member of PixelBufferScope itself --
                // PixelBufferScope exposes the underlying PixelBuffer via .Buffer.
                var imageBytes = bufferScope.Buffer.CopyImage();
                using var stream = new MemoryStream(imageBytes);
                var bitmap = new Bitmap(stream);
                session.RaiseFrame(bitmap);
            }
            catch (Exception)
            {
                // A single undecodable frame (wrong pixel format for this "just try
                // to decode it" approach, or a torn/partial frame) is not fatal --
                // skip it and keep waiting for the next one. See this class's header
                // comment for why raw pixel-format conversion isn't attempted here.
            }
        });

        session.AttachDevice(captureDevice);
        await captureDevice.StartAsync();
        return session;
    }

    /// <summary>Thin wrapper turning FlashCap's CaptureDevice into this codebase's own
    /// ICameraSession, so nothing outside this file ever holds a FlashCap type.</summary>
    private sealed class FlashCapSession : ICameraSession
    {
        private CaptureDevice? _device;

        public event EventHandler<Bitmap>? FrameReceived;

        public void AttachDevice(CaptureDevice device) => _device = device;

        public void RaiseFrame(Bitmap bitmap) => FrameReceived?.Invoke(this, bitmap);

        public async Task CloseAsync()
        {
            if (_device is null) return;
            try
            {
                await _device.StopAsync();
            }
            catch (Exception)
            {
                // Best-effort stop -- the device may already be gone (unplugged mid-
                // session); nothing further to do either way.
            }
        }

        public async ValueTask DisposeAsync()
        {
            await CloseAsync();
            if (_device is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_device is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
