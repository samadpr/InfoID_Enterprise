using InfoID.Domain.Common;
using InfoID.Domain.Entities.CardholderModule;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.VisitorModule;

/// <summary>
/// A time-bound badge issued to a visitor (visitors are stored as Cardholder records with a linked VisitorBadge).
/// </summary>
public class VisitorBadge : BaseEntity
{
    public long CardholderId { get; set; }
    public Cardholder? Cardholder { get; set; }
    public long? HostAppUserId { get; set; }  // The employee/host being visited
    public AppUser? HostAppUser { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public bool AutoExpired { get; set; }  // Default 0
}
