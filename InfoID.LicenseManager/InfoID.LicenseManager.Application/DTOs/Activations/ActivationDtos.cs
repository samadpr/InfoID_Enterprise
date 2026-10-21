using InfoID.Licensing.Core;

namespace InfoID.LicenseManager.Application.DTOs.Activations;

/// <summary>One thing wrong with an activation request, shown to staff before a key is issued.</summary>
public sealed record ActivationProblemDto(string Message, bool IsBlocking);

/// <summary>What the "Generate Serial Key" screen shows after decoding a request code (FR-ADM-6
/// "auto-decode"), before anything is issued. The license fields are null when the code is unknown.</summary>
public sealed record SoftCodePreviewDto(
    string ActivationCode,
    string FingerprintHex,
    DateOnly RequestDate,
    long? LicenseId,
    string? CodeStatus,
    string? Tier,
    string? LicenseType,
    DateOnly? ExpiryDate,
    int? MaxCardsPerPeriod,
    string? CardLimitPeriod,
    string? Customer,
    int ActiveMachines,
    int MaxActivations,
    bool IsReactivationOfSameMachine,
    bool AllowOfflineActivation,
    IReadOnlyList<ActivationProblemDto> Problems)
{
    public bool CanIssue => LicenseId is not null && Problems.All(p => !p.IsBlocking);
}

/// <summary>Request-code text plus the optional overrides (FR-ADM-6 "custom expiry or override tier").</summary>
public sealed record IssueSerialKeyRequestDto(string SoftCode, LicenseTier? TierOverride, DateOnly? ExpiryOverride);

public sealed record SerialKeyIssuedDto(
    string SerialKey,
    LicenseTier Tier,
    LicenseType Type,
    DateOnly? ExpiryDate,
    int? MaxCardsPerPeriod,
    CardLimitPeriod LimitPeriod);

// ---- Super Admin customer simulator (plays the customer's side of offline activation) ----

public sealed record SimulatedRequestCodeDto(string RequestCode, string FingerprintHex);

public sealed record SimulatedVerificationDto(
    LicenseTier Tier,
    LicenseType Type,
    DateOnly? ExpiryDate,
    int? MaxCardsPerPeriod,
    CardLimitPeriod LimitPeriod);

/// <summary>Answer to an online activation or check-in: a signed license token, or why not.
/// <see cref="Status"/> is Active / Refused / Revoked / Deactivated / Unknown / Error.</summary>
public sealed record OnlineLicenseResultDto(bool Succeeded, string? License, string? Error, string Status)
{
    public static OnlineLicenseResultDto Fail(string error, string status = "Refused") => new(false, null, error, status);
}
