using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using InfoID.Desktop.Features.CardDesigner.Models;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>Camera access abstraction for the "Take Photo / Use Camera" workflow
/// (Priority 12, FR-PHS-1: "Capture photos via connected webcam with live preview,
/// countdown timer and retake"). Deliberately thin and library-agnostic -- the
/// ViewModel/View layer (CameraCaptureDialogViewModel/View) only ever talks to this
/// interface, never to FlashCap (or whatever library implements it) directly, so the
/// entire risk of "did I get the third-party library's exact API right" is contained
/// to FlashCapCameraService.cs alone. See that class's own doc comment for the honest
/// confidence level on this specific piece.</summary>
public interface ICameraService
{
    /// <summary>Lists currently available camera devices. Returns an empty list
    /// (never throws) if no camera is connected, the platform has no camera support,
    /// or the underlying library fails to enumerate for any reason -- callers should
    /// treat "empty list" as the one and only "no camera available" signal.</summary>
    Task<IReadOnlyList<CameraDeviceInfo>> GetAvailableDevicesAsync();

    /// <summary>Opens a session on the given device and starts delivering live preview
    /// frames through the returned session's FrameReceived event. Throws if the device
    /// can't be opened (disconnected since enumeration, in use by another app,
    /// permission denied, etc.) -- callers must catch and show a graceful message
    /// rather than let this propagate to a crash.</summary>
    Task<ICameraSession> OpenAsync(CameraDeviceInfo device);
}

/// <summary>A live, running camera session. Dispose (or CloseAsync) to release the
/// device -- callers must not leave a session open longer than the capture dialog is
/// visible, since most webcams only allow one open handle at a time.</summary>
public interface ICameraSession : IAsyncDisposable
{
    /// <summary>Raised on a background thread for every incoming preview frame, already
    /// decoded to a ready-to-display Bitmap. Subscribers must marshal back to the UI
    /// thread themselves (CameraCaptureDialogViewModel does this via
    /// Avalonia.Threading.Dispatcher.UIThread.Post) before touching any bound
    /// property -- this event is not guaranteed to fire on the UI thread.</summary>
    event EventHandler<Bitmap>? FrameReceived;

    Task CloseAsync();
}
