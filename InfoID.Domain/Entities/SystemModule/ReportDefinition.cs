using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.SystemModule;

/// <summary>
/// A saved custom/ad-hoc report built with the report builder (FR-REP-6), in addition to the predefined reports.
/// </summary>
public class ReportDefinition : BaseEntity
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string Name { get; set; } = string.Empty;
    public ReportType ReportType { get; set; }  // Predefined / Custom
    public string? QueryJson { get; set; }  // Filter/column definition for custom reports
    public long? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
}
