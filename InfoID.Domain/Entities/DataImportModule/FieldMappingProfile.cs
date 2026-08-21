using InfoID.Domain.Common;
using InfoID.Domain.Entities.TemplateModule;

namespace InfoID.Domain.Entities.DataImportModule;

/// <summary>
/// A reusable mapping of source columns → template/cardholder fields (FR-DAT-2).
/// </summary>
public class FieldMappingProfile : BaseEntity
{
    public long DataSourceConnectionId { get; set; }
    public DataSourceConnection? DataSourceConnection { get; set; }
    public long? TemplateId { get; set; }  // Optional — mapping can target a specific template's fields
    public Template? Template { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MappingJson { get; set; } = string.Empty;  // Column → field dictionary
}
