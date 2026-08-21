using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.RfidModule;

/// <summary>
/// Configuration for a secure card technology: data block/sector mapping and encrypted key material (FR-RFD-4).
/// </summary>
public class RfidKeyProfile : BaseEntity
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string Name { get; set; } = string.Empty;
    public RfidCardTechnology CardTechnology { get; set; }  // MifareClassic / DESFire / HIDiCLASS / NFC
    public string? SectorMappingJson { get; set; }
    public string? KeyDataEncrypted { get; set; }  // AES-256 encrypted key material
}
