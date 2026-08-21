using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.IssuanceModule;

namespace InfoID.Domain.Entities.AccessControlModule;

/// <summary>
/// Each push/pull sync event between InfoID and the access-control platform (FR-ACS-2, FR-ACS-3).
/// </summary>
public class AccessControlSyncLog : BaseEntity
{
    public long AccessControlPlatformId { get; set; }
    public AccessControlPlatform? AccessControlPlatform { get; set; }
    public long? CardId { get; set; }
    public Card? Card { get; set; }
    public SyncDirection Direction { get; set; }  // Push / Pull
    public DateTime SyncDate { get; set; }
    public SyncResult Result { get; set; }  // Success / Failed
    public string? Notes { get; set; }
}
