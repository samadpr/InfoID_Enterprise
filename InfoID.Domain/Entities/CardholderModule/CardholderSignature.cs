using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;

namespace InfoID.Domain.Entities.CardholderModule;

/// <summary>
/// A captured/uploaded signature, with version history (FR-PHS-6, FR-PHS-8).
/// </summary>
public class CardholderSignature : BaseEntity
{
    public long CardholderId { get; set; }
    public Cardholder? Cardholder { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public SignatureCaptureSource? CaptureSource { get; set; }  // SignaturePad / ImageUpload
    public int VersionNumber { get; set; }  // Default 1
    public bool IsCurrent { get; set; }  // Default 1
}
