using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;

namespace InfoID.Domain.Entities.OrganizationModule;

/// <summary>
/// The single license-holder/operator profile for this installation. No roles/permissions inside the desktop app — this table mainly stores identity, preferences and security options.
/// </summary>
public class AppUser : BaseEntity
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }  // Optional contact per FR-LIC-2
    public string? PreferredLanguage { get; set; }  // Localization (FR-MOD-1)
    public ThemeMode? ThemeMode { get; set; }  // Light / Dark (FR-MOD-2)
    public bool IsPasswordProtected { get; set; }  // Default 0 (FR-SEC-4)
    public string? PasswordHash { get; set; }  // Only used if password protection enabled
    public int? AutoLockMinutes { get; set; }  // 5/15/30/60 (FR-SEC-6)
    public DateTime? LastLoginDate { get; set; }
}
