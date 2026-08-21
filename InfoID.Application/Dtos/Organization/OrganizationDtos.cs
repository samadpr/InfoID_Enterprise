using InfoID.Domain.Common.Enums;

namespace InfoID.Application.Dtos.Organization;

/// <summary>Read model returned to the UI. Never expose the Domain entity
/// directly to ViewModels -- this keeps EF Core change-tracking internals
/// and future schema changes from leaking into the UI layer.</summary>
public class OrganizationDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public OrganizationType? OrgType { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public string? Address { get; set; }
    public string? LogoPath { get; set; }
    public string? AccentColorHex { get; set; }
    public DateTime CreatedDate { get; set; }
}

public class CreateOrganizationRequest
{
    public string Name { get; set; } = string.Empty;
    public OrganizationType? OrgType { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public string? Address { get; set; }
}

public class UpdateOrganizationRequest
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public OrganizationType? OrgType { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public string? Address { get; set; }
    public string? LogoPath { get; set; }
    public string? AccentColorHex { get; set; }
}
