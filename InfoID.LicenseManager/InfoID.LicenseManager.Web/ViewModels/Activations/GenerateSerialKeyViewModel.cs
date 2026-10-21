using System.ComponentModel.DataAnnotations;
using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.DTOs.Activations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InfoID.LicenseManager.Web.ViewModels.Activations;

/// <summary>"Generate Serial Key" form (FR-ADM-6): the customer's request code plus, for roles that may
/// manage licenses, an optional tier or expiry override.</summary>
public sealed class GenerateSerialKeyViewModel
{
    [Required(ErrorMessage = "Paste the request code from the customer.")]
    [StringLength(200)]
    [Display(Name = "Request code")]
    public string SoftCode { get; set; } = "";

    [Display(Name = "Override edition")]
    public bool OverrideTier { get; set; }

    public LicenseTier OverrideTierValue { get; set; } = LicenseTier.Professional;

    [Display(Name = "Override expiry")]
    public bool OverrideExpiry { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? OverrideExpiryDate { get; set; }

    // ---- results, filled by the controller ----

    public SoftCodePreviewDto? Preview { get; set; }

    public SerialKeyIssuedDto? Issued { get; set; }

    public static IReadOnlyList<SelectListItem> TierOptions { get; } =
        Enum.GetValues<LicenseTier>().Select(t => new SelectListItem(t.ToString(), t.ToString())).ToList();
}
