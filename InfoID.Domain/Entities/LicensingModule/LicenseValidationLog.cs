using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;

namespace InfoID.Domain.Entities.LicensingModule;

/// <summary>
/// History of periodic re-validation checks (online or cached/offline) for reliability and support diagnostics.
/// </summary>
public class LicenseValidationLog : BaseEntity
{
    public long LicenseRecordId { get; set; }
    public LicenseRecord? LicenseRecord { get; set; }
    public DateTime ValidationDate { get; set; }
    public ValidationResult ValidationResult { get; set; }  // Success / Failed / OfflineCached
    public string? Notes { get; set; }
}
