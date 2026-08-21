using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.AccessControlModule;

/// <summary>
/// A configured connection to an external access-control system.
/// </summary>
public class AccessControlPlatform : BaseEntity
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public AccessControlPlatformName PlatformName { get; set; }  // ZKTeco / Suprema / HID / BioStar
    public string? ApiEndpoint { get; set; }
    public string? CredentialsEncrypted { get; set; }
    public string? FieldMappingJson { get; set; }  // Maps InfoID fields to vendor schema
    public SyncMode? SyncMode { get; set; }  // RealTime / Scheduled
}
