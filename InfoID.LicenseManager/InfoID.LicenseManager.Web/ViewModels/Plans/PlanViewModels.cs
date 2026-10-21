using System.ComponentModel.DataAnnotations;
using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.DTOs.Plans;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InfoID.LicenseManager.Web.ViewModels.Plans;

public sealed class PlanListViewModel
{
    public PlanListViewModel(IReadOnlyList<PlanDto> plans) => Plans = plans;

    public IReadOnlyList<PlanDto> Plans { get; }
}

/// <summary>Create/edit form. The plan id and product are never form fields: the id comes from the URL.</summary>
public sealed class PlanFormViewModel
{
    [Required(ErrorMessage = "Enter a plan name.")]
    [StringLength(150)]
    public string Name { get; set; } = "";

    public LicenseTier Tier { get; set; } = LicenseTier.Standard;

    [StringLength(300)]
    public string? Tagline { get; set; }

    [Display(Name = "Features (one per line)")]
    [StringLength(4000)]
    public string? FeaturesText { get; set; }

    [Display(Name = "Perpetual (no expiry)")]
    public bool Perpetual { get; set; }

    [Range(1, 120, ErrorMessage = "Duration must be 1-120 months.")]
    [Display(Name = "Duration (months)")]
    public int? DurationMonths { get; set; } = 12;

    [Range(1, 100, ErrorMessage = "Machines must be between 1 and 100.")]
    [Display(Name = "Default machines")]
    public int DefaultMachines { get; set; } = 1;

    [Display(Name = "Unlimited cards")]
    public bool UnlimitedCards { get; set; } = true;

    [Range(100, SerialKey.MaxCardLimit, ErrorMessage = "The card limit must be between 100 and 102,300.")]
    [Display(Name = "Cards per period")]
    public int? MaxCardsPerPeriod { get; set; } = 1000;

    [Display(Name = "Limit period")]
    public CardLimitPeriod LimitPeriod { get; set; } = CardLimitPeriod.Monthly;

    [StringLength(100)]
    [Display(Name = "Price text")]
    public string? PriceText { get; set; } = "Contact sales";

    [Display(Name = "Sort order")]
    public int SortOrder { get; set; } = 1;

    [Display(Name = "Active (offered to customers)")]
    public bool IsActive { get; set; } = true;

    public static IReadOnlyList<SelectListItem> TierOptions { get; } =
        Enum.GetValues<LicenseTier>().Select(t => new SelectListItem(t.ToString(), t.ToString())).ToList();

    public static IReadOnlyList<SelectListItem> LimitPeriodOptions { get; } =
        Enum.GetValues<CardLimitPeriod>().Select(p => new SelectListItem(p.ToString(), p.ToString())).ToList();
}
