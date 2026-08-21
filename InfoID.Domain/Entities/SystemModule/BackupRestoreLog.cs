using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;

namespace InfoID.Domain.Entities.SystemModule;

/// <summary>
/// History of local database backup/restore operations (FR-ENT-4).
/// </summary>
public class BackupRestoreLog : BaseEntity
{
    public BackupOperationType OperationType { get; set; }  // Backup / Restore
    public string? FilePath { get; set; }
    public DateTime StartedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public BackupStatus Status { get; set; }  // Success / Failed
}
