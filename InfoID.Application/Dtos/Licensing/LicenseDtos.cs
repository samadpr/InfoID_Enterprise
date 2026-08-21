using InfoID.Domain.Common.Enums;

namespace InfoID.Application.Dtos.Licensing;

/// <summary>What the UI actually needs to show a license status banner/screen
/// -- deliberately does not expose MachineFingerprint or the raw SerialKey.</summary>
public class LicenseStatusDto
{
    public bool HasLicense { get; set; }
    public LicenseTier? Tier { get; set; }
    public LicenseType? Type { get; set; }
    public LicenseStatus? Status { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? MaxCardsPerPeriod { get; set; }
    public int CardsIssuedThisPeriod { get; set; }
    public bool IsInGracePeriod { get; set; }
    public int? GracePeriodDaysRemaining { get; set; }
}

/// <summary>
/// Result of a feature gate check (FR-LIC-17: gates issuance/printing/encoding
/// only -- never blocks opening the app or general navigation).
/// </summary>
public class FeatureCheckResult
{
    public bool IsAllowed { get; set; }
    public string? Reason { get; set; }

    public static FeatureCheckResult Allowed() => new() { IsAllowed = true };
    public static FeatureCheckResult Denied(string reason) => new() { IsAllowed = false, Reason = reason };
}
