using InfoID.Domain.Common;
using InfoID.Domain.Entities.DataImportModule;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.CardholderModule;

/// <summary>
/// One person/record eligible for card issuance (student, employee, patient, visitor, member, etc.).
/// </summary>
public class Cardholder : BaseEntity
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public long? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public long? ImportBatchId { get; set; }  // Null if entered manually
    public ImportBatch? ImportBatch { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? ExternalRefId { get; set; }  // ID from source HR/SIS/ERP system
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public string? IdNumber { get; set; }  // Roll no. / employee no. / national ID etc.
    public long? CurrentPhotoId { get; set; }  // Points to the active photo version
    public CardholderPhoto? CurrentPhoto { get; set; }
    public long? CurrentSignatureId { get; set; }  // Points to the active signature version
    public CardholderSignature? CurrentSignature { get; set; }
    public bool IsAnonymized { get; set; }  // Default 0 — set true after GDPR erasure
}
