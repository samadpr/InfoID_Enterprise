using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;

namespace InfoID.Domain.Entities.OrganizationModule;

/// <summary>
/// The organization that owns this installation (captured at license activation). Normally one row per installation, but modeled properly in case of re-activation history.
/// </summary>
public class Organization : BaseEntity
{
    public string Name { get; set; } = string.Empty;  // Organization / company name
    public OrganizationType? OrgType { get; set; }  // School / Corporate / Hospital / Government / Event / Membership / Other
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public string? Address { get; set; }
    public string? LogoPath { get; set; }  // Path to org logo used in templates/branding
    public string? AccentColorHex { get; set; }  // Brand accent color for theme (FR-MOD-2)
}
