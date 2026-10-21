using InfoID.Licensing.Core;

namespace InfoID.LicenseManager.Domain.ValueObjects;

/// <summary>The editable content of a plan, as one validated unit (FR-LIC plans managed in License Manager).</summary>
/// <param name="DurationMonths">Subscription length in months; null = perpetual.</param>
/// <param name="MaxCardsPerPeriod">null = unlimited; otherwise a multiple of 100 the serial-key format can carry.</param>
public sealed record PlanDetails(
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
    bool IsActive)
{
    public const int MaxDurationMonths = 120;

    /// <summary>Null when valid, otherwise the first problem as a user-facing message.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Enter a plan name.";
        if (Name.Trim().Length > 150) return "The plan name must be at most 150 characters.";
        if (!Enum.IsDefined(Tier) || !Enum.IsDefined(LimitPeriod)) return "Choose a valid edition and card-limit period.";
        if (DurationMonths is < 1 or > MaxDurationMonths) return $"Duration must be 1-{MaxDurationMonths} months, or empty for perpetual.";
        if (DefaultMachines is < 1 or > 100) return "Machines must be between 1 and 100.";
        if (MaxCardsPerPeriod is { } max && (max <= 0 || max % 100 != 0 || max > SerialKey.MaxCardLimit))
            return $"The card limit must be a multiple of 100 between 100 and {SerialKey.MaxCardLimit.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}, or empty for unlimited.";
        if (Tagline?.Trim().Length > 300) return "The tagline must be at most 300 characters.";
        if (PriceText?.Trim().Length > 100) return "The price text must be at most 100 characters.";
        return null;
    }
}
