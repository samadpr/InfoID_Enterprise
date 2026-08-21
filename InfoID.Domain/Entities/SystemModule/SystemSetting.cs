using InfoID.Domain.Common;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.SystemModule;

/// <summary>
/// Generic key/value application settings (local, per installation or per organization).
/// </summary>
public class SystemSetting : BaseEntity
{
    public long? OrganizationId { get; set; }  // Null = machine-wide setting
    public Organization? Organization { get; set; }
    public string SettingKey { get; set; } = string.Empty;
    public string? SettingValue { get; set; }
}
