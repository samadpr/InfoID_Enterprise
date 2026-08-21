using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.OrganizationModule;

namespace InfoID.Domain.Entities.LicensingModule;

/// <summary>
/// The locally-stored, encrypted license record created after activation (FR-LIC-8). Enforces feature-tier gating and card issuance limits at runtime.
/// </summary>
public class LicenseRecord : BaseEntity
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string ActivationCode { get; set; } = string.Empty;  // Code originally provided at purchase
    public string? SerialKey { get; set; }  // Issued by License Server / Admin Portal
    public string MachineFingerprint { get; set; } = string.Empty;  // Hardware hash (CPU+board+disk+MAC+OS GUID)
    public LicenseTier LicenseTier { get; set; }  // Standard / Professional / Enterprise
    public LicenseType LicenseType { get; set; }  // Subscription / Perpetual / Trial
    public ActivationMode? ActivationMode { get; set; }  // Online / Offline
    public DateTime? ActivationDate { get; set; }
    public DateTime? ExpiryDate { get; set; }  // Null for Perpetual
    public int? MaxCardsPerPeriod { get; set; }  // Monthly/yearly cap
    public int CardsIssuedThisPeriod { get; set; }  // Default 0, reset by Admin Portal action
    public LicenseStatus Status { get; set; }  // Active / Expired / GracePeriod / Deactivated
    public int GracePeriodDays { get; set; }  // Default 7 (FR-LIC-12)
    public DateTime? LastValidationDate { get; set; }
    public int RevalidationIntervalDays { get; set; }  // Default 30 (FR-LIC-13)
}
