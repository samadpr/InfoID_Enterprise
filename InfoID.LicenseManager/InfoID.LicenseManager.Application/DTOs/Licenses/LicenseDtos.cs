using InfoID.Licensing.Core;

namespace InfoID.LicenseManager.Application.DTOs.Licenses;

/// <summary>What the License Creation form submits (FR-ADM-4). Privileged fields (product, customer
/// account, creator) are deliberately absent: the service sets them, never the browser.</summary>
public sealed record CreateLicenseRequestDto(
    int Quantity,
    LicenseTier Tier,
    LicenseType Type,
    DateOnly? ExpiryDate,
    int? MaxCardsPerPeriod,
    CardLimitPeriod LimitPeriod,
    int MaxActivations,
    bool AllowOfflineActivation,
    string? OrganizationName,
    string? OrganizationType,
    string? Country,
    string? ContactEmail,
    string? ContactPhone,
    string? Notes);

/// <summary>Search box and status filter of the activation-code list (FR-ADM-7).</summary>
public sealed record LicenseSearchDto(string? Text = null, string? Status = null, int Take = 500, Guid? BatchId = null);

/// <summary>Outcome of creating codes: the batch (null for a single code), the first code's id and every code in display form.</summary>
public sealed record CreatedLicensesDto(Guid? BatchId, long FirstLicenseId, IReadOnlyList<string> Codes);

/// <summary>Repository-side search parameters, with the typed text already normalised for code matching.</summary>
public sealed record LicenseSearchCriteria(string? Text, string? CodeFragment, string? Status, int Take, Guid? BatchId);

public sealed record LicenseListItemDto(
    long Id,
    string ActivationCode,
    string Tier,
    string LicenseType,
    DateOnly? ExpiryDate,
    int? MaxCardsPerPeriod,
    string Status,
    int ActiveMachines,
    int MaxActivations,
    string? Customer,
    DateTime CreatedDate,
    bool AllowOfflineActivation);

public sealed record LicenseActivationDto(
    long Id,
    string MachineFingerprint,
    string Mode,
    string Status,
    DateTime ActivationDate,
    DateTime? DeactivatedDate,
    DateTime? LastValidationDate,
    string IssuedTier,
    DateOnly? IssuedExpiryDate,
    string? AppVersion);

public sealed record LicenseDetailsDto(
    long Id,
    string ActivationCode,
    string Tier,
    string LicenseType,
    DateOnly? ExpiryDate,
    int? MaxCardsPerPeriod,
    string CardLimitPeriod,
    string Status,
    int MaxActivations,
    int ActiveMachines,
    bool AllowOfflineActivation,
    string? Customer,
    string? Notes,
    DateTime CreatedDate,
    DateTime? RevokedDate,
    IReadOnlyList<LicenseActivationDto> Activations);

public sealed record RevokeLicenseRequestDto(long LicenseId, string Reason);

public sealed record ExtendLicenseRequestDto(long LicenseId, DateOnly NewExpiryDate);

public sealed record FreeMachineSlotsRequestDto(long LicenseId, string Reason);

public sealed record SetOfflineActivationRequestDto(long LicenseId, bool Allow);
