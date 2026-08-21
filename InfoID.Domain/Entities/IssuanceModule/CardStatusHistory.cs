using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.IssuanceModule;

/// <summary>
/// Append-only log of every lifecycle status transition for a card, with reason and who changed it.
/// </summary>
public class CardStatusHistory : BaseEntity
{
    public long CardId { get; set; }
    public Card? Card { get; set; }
    public CardStatus? FromStatus { get; set; }  // Null on first transition
    public CardStatus ToStatus { get; set; }
    public DateTime ChangedDate { get; set; }
    public string? Reason { get; set; }  // e.g. Lost, Damaged, Renewed
    public long? ChangedByUserId { get; set; }
    public AppUser? ChangedByUser { get; set; }
}
