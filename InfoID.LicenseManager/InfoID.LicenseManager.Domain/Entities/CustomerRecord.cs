namespace InfoID.LicenseManager.Domain.Entities;

/// <summary>Module N CustomerRecord (FR-ADM-10).</summary>
public class CustomerRecord : EntityBase
{
    public string OrganizationName { get; set; } = "";
    public string? OrganizationType { get; set; }
    public string? Country { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }

    public List<LicenseCode> LicenseCodes { get; set; } = new();
}
