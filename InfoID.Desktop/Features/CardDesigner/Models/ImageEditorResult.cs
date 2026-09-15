namespace InfoID.Desktop.Features.CardDesigner.Models;

/// <summary>Result of a completed (not cancelled) ImageEditorDialogViewModel session --
/// the caller (CardDesignTabViewModel) imports <see cref="EditedFilePath"/> into
/// IDesignAssetService and then deletes it, exactly mirroring how a freshly-picked,
/// un-edited file was already handled before Priority 12 (Add Image workflow).</summary>
public sealed class ImageEditorResult
{
    public required string EditedFilePath { get; init; }
}
