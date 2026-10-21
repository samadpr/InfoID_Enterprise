using InfoID.Licensing.Core;

namespace InfoID.LicenseManager.Application.DTOs.Plans;

/// <summary>What the plan form submits (create or edit). The id of an edited plan is never part of it.</summary>
public sealed record PlanInputDto(
    string Name,
    LicenseTier Tier,
    string? Tagline,
    string? FeaturesText,
    int? DurationMonths,
    int DefaultMachines,
    int? MaxCardsPerPeriod,
    CardLimitPeriod LimitPeriod,
    string? PriceText,
    int SortOrder,
    bool IsActive);

public sealed record UpdatePlanRequestDto(long PlanId, PlanInputDto Plan);

public sealed record PlanDto(
    long Id,
    string Name,
    string Tier,
    string? Tagline,
    string? FeaturesText,
    int? DurationMonths,
    int DefaultMachines,
    int? MaxCardsPerPeriod,
    string CardLimitPeriod,
    string? PriceText,
    int SortOrder,
    bool IsActive);
