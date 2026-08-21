using InfoID.Domain.Common;

namespace InfoID.Domain.Entities.CardholderModule;

/// <summary>
/// The actual value of one custom field for one cardholder (key/value store, one row per field per person).
/// </summary>
public class CardholderCustomFieldValue : BaseEntity
{
    public long CardholderId { get; set; }
    public Cardholder? Cardholder { get; set; }
    public long CustomFieldDefinitionId { get; set; }
    public CustomFieldDefinition? CustomFieldDefinition { get; set; }
    public string? Value { get; set; }
}
