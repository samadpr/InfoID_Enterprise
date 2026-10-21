namespace InfoID.LicenseManager.Domain.Entities;

/// <summary>Module N EmailTemplate (FR-ADM-11).</summary>
public class EmailTemplate : EntityBase
{
    public string Name { get; set; } = "";
    public string Subject { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public string TemplateType { get; set; } = "";
}
