using InfoID.Domain.Common.Enums;

namespace InfoID.Application.Dtos.Licensing;

/// <summary>What the UI needs to show license status (FR-LIC-9) -- deliberately does
/// not expose the raw serial key or the full machine fingerprint.</summary>
public class LicenseStatusDto
{
    /// <summary>A license record exists on this machine (it may still be invalid).</summary>
    public bool HasLicense { get; set; }

    /// <summary>The stored serial key verified for THIS machine and activation code.
    /// False when the record was tampered with or copied from another computer.</summary>
    public bool IsValid { get; set; }

    /// <summary>Why the license isn't usable, in plain words (null when fine).</summary>
    public string? Problem { get; set; }

    public LicenseTier? Tier { get; set; }
    public LicenseType? Type { get; set; }
    public LicenseStatus? Status { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? DaysUntilExpiry { get; set; }
    public int? MaxCardsPerPeriod { get; set; }
    public string? CardLimitPeriod { get; set; }
    public int CardsIssuedThisPeriod { get; set; }
    public bool IsInGracePeriod { get; set; }
    public int? GracePeriodDaysRemaining { get; set; }

    public string? ActivationCode { get; set; }
    public string? MachineIdMasked { get; set; }
    public string? OrganizationName { get; set; }
    public DateTime? ActivationDate { get; set; }
    public ActivationMode? ActivationMode { get; set; }
    /// <summary>Last successful online confirmation (online licenses).</summary>
    public DateTime? LastConfirmedOnline { get; set; }
    /// <summary>Online licenses: must check in before this, or printing pauses.</summary>
    public DateTime? CheckInDeadline { get; set; }
}

/// <summary>Result of a feature gate check (FR-LIC-12: gates issuance/printing/encoding
/// only -- never blocks opening the app or general navigation, FR-SEC-7).</summary>
public class FeatureCheckResult
{
    public bool IsAllowed { get; set; }
    public string? Reason { get; set; }

    /// <summary>Cards that may still be issued this period (null = unlimited).</summary>
    public int? CardsRemaining { get; set; }

    public static FeatureCheckResult Allowed(int? cardsRemaining = null) => new() { IsAllowed = true, CardsRemaining = cardsRemaining };
    public static FeatureCheckResult Denied(string reason) => new() { IsAllowed = false, Reason = reason };
}

/// <summary>Organization/user details captured on the Activation screen (FR-LIC-2).</summary>
public class ActivationDetailsDto
{
    public string OrganizationName { get; set; } = string.Empty;
    public string? OrganizationEmail { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string? UserPhone { get; set; }
    public string? Country { get; set; }
    public OrganizationType? InstallationPurpose { get; set; }
}

/// <summary>Request code shown to the customer for offline activation (FR-LIC-6).</summary>
public class RequestCodeDto
{
    public string ActivationCode { get; set; } = string.Empty;
    public string RequestCode { get; set; } = string.Empty;
    public string MachineIdMasked { get; set; } = string.Empty;
    /// <summary>Phone page for the QR code (null if no license server is configured).</summary>
    public string? PhoneActivationUrl { get; set; }
}

/// <summary>What the customer gives the vendor after deactivating, so the vendor can
/// free the machine slot (FR-LIC-10).</summary>
public class DeactivationInfoDto
{
    public string ActivationCode { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
}

/// <summary>Result of online activation.</summary>
public sealed class OnlineActivationResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    /// <summary>True when the server couldn't be reached: offer offline activation.</summary>
    public bool IsServerUnreachable { get; init; }
    public LicenseStatusDto? Status { get; init; }
}
