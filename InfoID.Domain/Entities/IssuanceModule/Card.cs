using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.CardholderModule;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Entities.TemplateModule;

namespace InfoID.Domain.Entities.IssuanceModule;

/// <summary>
/// One physical/issued card, linking a cardholder to the template used and (if applicable) its RFID identifier.
/// </summary>
public class Card : BaseEntity
{
    public long CardholderId { get; set; }
    public Cardholder? Cardholder { get; set; }
    public long TemplateId { get; set; }
    public Template? Template { get; set; }
    public long? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public string CardNumber { get; set; } = string.Empty;  // Unique, often Branch.NumberingPrefix + sequence
    public string? RFIDUid { get; set; }  // Chip UID once encoded
    public CardStatus Status { get; set; }  // Draft / Issued / Active / Suspended / Expired / Replaced / Revoked
    public DateTime? IssuedDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public long? ReplacesCardId { get; set; }  // Points to the old card this one replaces (reissue chain)
    public Card? ReplacesCard { get; set; }
}
