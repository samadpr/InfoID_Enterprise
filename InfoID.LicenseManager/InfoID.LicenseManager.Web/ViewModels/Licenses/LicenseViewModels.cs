using System.ComponentModel.DataAnnotations;
using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InfoID.LicenseManager.Web.ViewModels.Licenses;

/// <summary>Query-string filters of the code list (all optional).</summary>
public sealed class LicenseListQuery
{
    [StringLength(200)]
    public string? Text { get; set; }

    public string? Status { get; set; }

    public Guid? BatchId { get; set; }
}

public sealed class LicenseListViewModel
{
    public LicenseListViewModel(LicenseListQuery query, IReadOnlyList<LicenseListItemDto> rows)
    {
        Query = query;
        Rows = rows;
    }

    public LicenseListQuery Query { get; }

    public IReadOnlyList<LicenseListItemDto> Rows { get; }

    public static IReadOnlyList<SelectListItem> StatusOptions { get; } =
    [
        new("All statuses", ""),
        new(LicenseCodeStatus.Available, LicenseCodeStatus.Available),
        new(LicenseCodeStatus.Active, LicenseCodeStatus.Active),
        new(LicenseCodeStatus.Revoked, LicenseCodeStatus.Revoked),
    ];

    /// <summary>Codes of the batch just created, one per line, ready to copy or e-mail.</summary>
    public string BatchCodesText => string.Join(Environment.NewLine, Rows.Select(r => r.ActivationCode));
}

/// <summary>The License Creation form. Only the fields staff may choose are here: product, customer
/// account, creator and status are set by the server.</summary>
public sealed class CreateLicenseViewModel
{
    [Range(1, 1000, ErrorMessage = "Quantity must be between 1 and 1000.")]
    public int Quantity { get; set; } = 1;

    public LicenseTier Tier { get; set; } = LicenseTier.Standard;

    public LicenseType Type { get; set; } = LicenseType.Subscription;

    [DataType(DataType.Date)]
    [Display(Name = "Expiry date")]
    public DateOnly? ExpiryDate { get; set; }

    [Display(Name = "Unlimited cards")]
    public bool UnlimitedCards { get; set; } = true;

    [Range(100, SerialKey.MaxCardLimit, ErrorMessage = "The card limit must be between 100 and 102,300.")]
    [Display(Name = "Cards per period")]
    public int? MaxCardsPerPeriod { get; set; } = 1000;

    [Display(Name = "Limit period")]
    public CardLimitPeriod LimitPeriod { get; set; } = CardLimitPeriod.Monthly;

    [Range(1, LicenseCode.MaxMachinesPerCode, ErrorMessage = "Machines per code must be between 1 and 100.")]
    [Display(Name = "Machines per code")]
    public int MaxActivations { get; set; } = 1;

    [Display(Name = "Allow offline activation (phone / QR)")]
    public bool AllowOfflineActivation { get; set; }

    [StringLength(200)] [Display(Name = "Organization")] public string? OrganizationName { get; set; }
    [StringLength(50)] [Display(Name = "Organization type")] public string? OrganizationType { get; set; }
    [StringLength(100)] public string? Country { get; set; }
    [StringLength(200)] [Display(Name = "Contact email")] public string? ContactEmail { get; set; }
    [StringLength(50)] [Display(Name = "Contact phone")] public string? ContactPhone { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }

    public static IReadOnlyList<SelectListItem> TierOptions { get; } =
        Enum.GetValues<LicenseTier>().Select(t => new SelectListItem(t.ToString(), t.ToString())).ToList();

    /// <summary>Trial licenses are not created here (the desktop manager did not offer them either).</summary>
    public static IReadOnlyList<SelectListItem> TypeOptions { get; } =
    [
        new(nameof(LicenseType.Subscription), nameof(LicenseType.Subscription)),
        new(nameof(LicenseType.Perpetual), nameof(LicenseType.Perpetual)),
    ];

    public static IReadOnlyList<SelectListItem> LimitPeriodOptions { get; } =
        Enum.GetValues<CardLimitPeriod>().Select(p => new SelectListItem(p.ToString(), p.ToString())).ToList();
}

public sealed class LicenseDetailsViewModel
{
    public LicenseDetailsViewModel(LicenseDetailsDto license) => License = license;

    public LicenseDetailsDto License { get; }

    public bool IsRevoked => License.Status == LicenseCodeStatus.Revoked;

    public bool IsPerpetual => License.LicenseType == nameof(LicenseType.Perpetual);

    public RevokeFormViewModel Revoke { get; } = new();

    public ExtendFormViewModel Extend { get; } = new();

    public FreeSlotsFormViewModel FreeSlots { get; } = new();
}

public sealed class RevokeFormViewModel
{
    [Required(ErrorMessage = "Enter a reason for revoking.")]
    [StringLength(300)]
    public string Reason { get; set; } = "";
}

public sealed class ExtendFormViewModel
{
    [Required(ErrorMessage = "Choose the new expiry date.")]
    [DataType(DataType.Date)]
    [Display(Name = "New expiry date")]
    public DateOnly? NewExpiryDate { get; set; }
}

public sealed class FreeSlotsFormViewModel
{
    [Required(ErrorMessage = "Enter a reason (for example: customer moved to a new PC).")]
    [StringLength(300)]
    public string Reason { get; set; } = "";
}
