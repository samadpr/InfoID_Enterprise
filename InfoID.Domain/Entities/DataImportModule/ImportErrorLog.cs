using InfoID.Domain.Common;

namespace InfoID.Domain.Entities.DataImportModule;

/// <summary>
/// Per-row validation errors surfaced before commit (FR-DAT-3).
/// </summary>
public class ImportErrorLog : BaseEntity
{
    public long ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }
    public int RowNumber { get; set; }
    public string? FieldName { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string? RawValue { get; set; }
}
