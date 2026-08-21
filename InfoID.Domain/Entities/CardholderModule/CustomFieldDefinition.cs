using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.CardholderModule;

/// <summary>
/// Organization-defined custom fields (e.g., 'Emergency Contact', 'Blood Group') available for binding on templates (FR-DES-11, 8.1).
/// </summary>
public class CustomFieldDefinition : BaseEntity
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string FieldKey { get; set; } = string.Empty;  // Stable key used in template bindings
    public string Label { get; set; } = string.Empty;  // Display label
    public CustomFieldDataType DataType { get; set; }  // Text / Number / Date / Boolean / List
    public bool IsRequired { get; set; }  // Default 0
    public string? AppliesToCategory { get; set; }  // e.g. Student, Employee, Visitor — used for conditional visibility
}
