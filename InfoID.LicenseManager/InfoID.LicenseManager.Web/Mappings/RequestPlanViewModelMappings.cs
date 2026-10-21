using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.DTOs.Plans;
using InfoID.LicenseManager.Application.DTOs.Requests;
using InfoID.LicenseManager.Web.ViewModels.Plans;
using InfoID.LicenseManager.Web.ViewModels.Requests;

namespace InfoID.LicenseManager.Web.Mappings;

/// <summary>Maps requests and plans to their forms and back. Only whitelisted form fields are read.</summary>
public static class RequestPlanViewModelMappings
{
    /// <summary>Pre-fills the approval form from the plan that was requested and the number of computers asked for.</summary>
    public static ApproveFormViewModel ToApproveForm(this LicenseRequestDto request, DateOnly today) => new()
    {
        Tier = Enum.TryParse<LicenseTier>(request.PlanTier, out var tier) ? tier : LicenseTier.Standard,
        Type = request.PlanDurationMonths is null ? LicenseType.Perpetual : LicenseType.Subscription,
        ExpiryDate = today.AddMonths(request.PlanDurationMonths ?? 12),
        UnlimitedCards = request.PlanMaxCards is null,
        MaxCardsPerPeriod = request.PlanMaxCards ?? 1000,
        LimitPeriod = Enum.TryParse<CardLimitPeriod>(request.PlanLimitPeriod, out var period) ? period : CardLimitPeriod.Monthly,
        MaxActivations = Math.Clamp(request.Machines, 1, 100),
        AllowOfflineActivation = false,
    };

    public static ApproveLicenseRequestDto ToRequest(this ApproveFormViewModel form, long requestId) => new(
        requestId,
        form.Tier,
        form.Type,
        form.Type == LicenseType.Perpetual ? null : form.ExpiryDate,
        form.UnlimitedCards ? null : form.MaxCardsPerPeriod ?? 0,
        form.LimitPeriod,
        form.MaxActivations,
        form.AllowOfflineActivation,
        form.Notes);

    public static PlanInputDto ToInput(this PlanFormViewModel form) => new(
        form.Name,
        form.Tier,
        form.Tagline,
        form.FeaturesText,
        form.Perpetual ? null : form.DurationMonths ?? 12,
        form.DefaultMachines,
        form.UnlimitedCards ? null : form.MaxCardsPerPeriod ?? 0,
        form.LimitPeriod,
        form.PriceText,
        form.SortOrder,
        form.IsActive);

    public static PlanFormViewModel ToForm(this PlanDto plan) => new()
    {
        Name = plan.Name,
        Tier = Enum.TryParse<LicenseTier>(plan.Tier, out var tier) ? tier : LicenseTier.Standard,
        Tagline = plan.Tagline,
        FeaturesText = plan.FeaturesText,
        Perpetual = plan.DurationMonths is null,
        DurationMonths = plan.DurationMonths ?? 12,
        DefaultMachines = plan.DefaultMachines,
        UnlimitedCards = plan.MaxCardsPerPeriod is null,
        MaxCardsPerPeriod = plan.MaxCardsPerPeriod ?? 1000,
        LimitPeriod = Enum.TryParse<CardLimitPeriod>(plan.CardLimitPeriod, out var period) ? period : CardLimitPeriod.Monthly,
        PriceText = plan.PriceText,
        SortOrder = plan.SortOrder,
        IsActive = plan.IsActive,
    };
}
