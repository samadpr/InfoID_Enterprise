using Avalonia.Media.Imaging;

namespace InfoID.Desktop.Features.CardDesigner.Models;

/// <summary>Result of a completed CameraCaptureDialogViewModel session. Exactly one of
/// the two properties is meaningful at a time:
/// - CapturedBitmap set: the user captured a photo and clicked "Use This Photo".
/// - FallThroughToBrowse true: no camera was available (or the user chose to bail out
///   of the camera flow), and CardDesignTabViewModel should fall through to the normal
///   Browse Image file picker instead.
/// A plain "cancelled" close is represented by the dialog returning null entirely
/// (see DialogViewModelBase), not by a CameraCaptureResult instance -- so a non-null
/// result here always means "do something", never "do nothing".</summary>
public sealed class CameraCaptureResult
{
    public Bitmap? CapturedBitmap { get; init; }
    public bool FallThroughToBrowse { get; init; }
}
