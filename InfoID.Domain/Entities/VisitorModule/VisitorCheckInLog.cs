using InfoID.Domain.Common;

namespace InfoID.Domain.Entities.VisitorModule;

/// <summary>
/// Check-in/check-out timestamps for a visitor visit (FR-VIS-3).
/// </summary>
public class VisitorCheckInLog : BaseEntity
{
    public long VisitorBadgeId { get; set; }
    public VisitorBadge? VisitorBadge { get; set; }
    public DateTime? CheckInDate { get; set; }
    public DateTime? CheckOutDate { get; set; }
}
