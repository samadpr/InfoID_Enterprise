using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.DataImportModule;

/// <summary>
/// One execution of an import (manual or scheduled) with summary counts (FR-DAT-3, FR-DAT-4).
/// </summary>
public class ImportBatch : BaseEntity
{
    public long DataSourceConnectionId { get; set; }
    public DataSourceConnection? DataSourceConnection { get; set; }
    public long? FieldMappingProfileId { get; set; }
    public FieldMappingProfile? FieldMappingProfile { get; set; }
    public long? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public DateTime StartedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public int TotalRecords { get; set; }  // Default 0
    public int SuccessCount { get; set; }  // Default 0
    public int ErrorCount { get; set; }  // Default 0
    public ImportBatchStatus Status { get; set; }  // Running / Completed / Failed / PartialSuccess
}
