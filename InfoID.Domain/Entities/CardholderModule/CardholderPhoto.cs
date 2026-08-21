using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;

namespace InfoID.Domain.Entities.CardholderModule;

/// <summary>
/// A captured/uploaded photo, with version history for reissue scenarios (FR-PHS-8).
/// </summary>
public class CardholderPhoto : BaseEntity
{
    public long CardholderId { get; set; }
    public Cardholder? Cardholder { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public PhotoCaptureSource? CaptureSource { get; set; }  // Webcam / MobileUpload / FileUpload
    public PhotoComplianceStatus? ComplianceStatus { get; set; }  // Pass / Warning / Fail (FR-PHS-7)
    public int VersionNumber { get; set; }  // Default 1
    public bool IsCurrent { get; set; }  // Default 1
}
