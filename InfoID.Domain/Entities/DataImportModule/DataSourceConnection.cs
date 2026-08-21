using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.DataImportModule;

/// <summary>
/// A saved connection profile to an external data source used for cardholder import.
/// </summary>
public class DataSourceConnection : BaseEntity
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string Name { get; set; } = string.Empty;
    public DataSourceType SourceType { get; set; }  // Excel / CSV / SQLServer / MySQL / PostgreSQL / Oracle / Access / RestApi
    public string? ConnectionStringEncrypted { get; set; }  // AES-256 encrypted (FR-SEC-1)
    public bool IsScheduled { get; set; }  // Default 0 (FR-DAT-4)
    public string? ScheduleCron { get; set; }  // Recurrence expression if scheduled
    public DateTime? LastImportDate { get; set; }
}
