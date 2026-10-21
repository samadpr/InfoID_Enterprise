using InfoID.LicenseManager.Domain.Exceptions;
using InfoID.LicenseManager.Domain.ValueObjects;

namespace InfoID.LicenseManager.Domain.Entities;

/// <summary>A plan customers can request (edition + duration + machines), managed in
/// License Manager and shown on the app's Editions page -- change plans without
/// releasing a new app.</summary>
public class Plan : EntityBase
{
    public string ProductCode { get; set; } = ProductCodes.InfoID;
    public string Name { get; set; } = "";
    /// <summary>Standard / Professional / Enterprise (InfoID.Licensing.Core.LicenseTier).</summary>
    public string Tier { get; set; } = "Standard";
    /// <summary>One-line audience description, e.g. "Small companies, occasional printing".</summary>
    public string? Tagline { get; set; }
    /// <summary>Feature list, one per line.</summary>
    public string? FeaturesText { get; set; }
    /// <summary>Subscription length in months; null = perpetual.</summary>
    public int? DurationMonths { get; set; } = 12;
    public int DefaultMachines { get; set; } = 1;
    public int? MaxCardsPerPeriod { get; set; }
    public string CardLimitPeriod { get; set; } = "Monthly";
    /// <summary>Display only, e.g. "Contact sales" -- payment happens outside the system.</summary>
    public string? PriceText { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public static Plan Create(string productCode, PlanDetails details)
    {
        var plan = new Plan { ProductCode = productCode };
        plan.Update(details);
        return plan;
    }

    /// <summary>Replaces the plan's content with <paramref name="details"/> after validating it.</summary>
    public void Update(PlanDetails details)
    {
        var problem = details.Validate();
        if (problem is not null) throw new DomainRuleException(problem);

        Name = details.Name.Trim();
        Tier = details.Tier.ToString();
        Tagline = Clean(details.Tagline);
        FeaturesText = Clean(details.FeaturesText);
        DurationMonths = details.DurationMonths;
        DefaultMachines = details.DefaultMachines;
        MaxCardsPerPeriod = details.MaxCardsPerPeriod;
        CardLimitPeriod = details.LimitPeriod.ToString();
        PriceText = Clean(details.PriceText);
        SortOrder = details.SortOrder;
        IsActive = details.IsActive;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
