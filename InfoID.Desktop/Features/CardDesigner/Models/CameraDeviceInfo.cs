namespace InfoID.Desktop.Features.CardDesigner.Models;

/// <summary>A single enumerated camera device, as reported by ICameraService. Kept
/// deliberately thin (no FlashCap types leak out of FlashCapCameraService) so the
/// ViewModel/View layer never needs to reference FlashCap directly -- if the camera
/// library is ever swapped out, only the service implementation changes.</summary>
public sealed class CameraDeviceInfo
{
    /// <summary>Opaque identifier used to re-select this exact device;
    /// implementation-defined (FlashCapCameraService uses the device's index).</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }
}
