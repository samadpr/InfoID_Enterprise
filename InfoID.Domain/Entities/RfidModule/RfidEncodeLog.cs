using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.IssuanceModule;

namespace InfoID.Domain.Entities.RfidModule;

/// <summary>
/// Write/verify outcome per card, including bulk pre-encoding of blank stock (FR-RFD-3, FR-RFD-5).
/// </summary>
public class RfidEncodeLog : BaseEntity
{
    public long CardId { get; set; }
    public Card? Card { get; set; }
    public long? RFIDKeyProfileId { get; set; }
    public RfidKeyProfile? RFIDKeyProfile { get; set; }
    public RfidOperation Operation { get; set; }  // Encode / Verify / BulkPreEncode
    public RfidResult Result { get; set; }  // Success / Failed
    public DateTime EncodedDate { get; set; }
    public string? FailureReason { get; set; }
}
