using System.ComponentModel.DataAnnotations;
using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.DTOs.Requests;
using InfoID.LicenseManager.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InfoID.LicenseManager.Web.ViewModels.Requests;

public sealed class RequestListQuery
{
    public string? Status { get; set; } = RequestStatusFilter.Open;

    [StringLength(200)]
    public string? Text { get; set; }
}

public sealed class RequestListViewModel
{
    public RequestListViewModel(RequestListQuery query, IReadOnlyList<LicenseRequestDto> rows)
    {
        Query = query;
        Rows = rows;
    }

    public RequestListQuery Query { get; }

    public IReadOnlyList<LicenseRequestDto> Rows { get; }

    public static IReadOnlyList<SelectListItem> StatusOptions { get; } =
    [
        new("Open (new + in progress)", RequestStatusFilter.Open),
        new(LicenseRequestStatus.New, LicenseRequestStatus.New),
        new("In progress", LicenseRequestStatus.InProgress),
        new(LicenseRequestStatus.Approved, LicenseRequestStatus.Approved),
        new(LicenseRequestStatus.Rejected, LicenseRequestStatus.Rejected),
        new("All", RequestStatusFilter.All),
    ];
}

/// <summary>The approval form: terms pre-filled from the plan and the request, editable by staff.</summary>
public sealed class ApproveFormViewModel
{
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

    [Range(1, LicenseCode.MaxMachinesPerCode, ErrorMessage = "Machines must be between 1 and 100.")]
    [Display(Name = "Computers")]
    public int MaxActivations { get; set; } = 1;

    [Display(Name = "Allow offline activation (phone / QR)")]
    public bool AllowOfflineActivation { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public static IReadOnlyList<SelectListItem> TierOptions { get; } =
        Enum.GetValues<LicenseTier>().Select(t => new SelectListItem(t.ToString(), t.ToString())).ToList();

    public static IReadOnlyList<SelectListItem> TypeOptions { get; } =
    [
        new(nameof(LicenseType.Subscription), nameof(LicenseType.Subscription)),
        new(nameof(LicenseType.Perpetual), nameof(LicenseType.Perpetual)),
    ];

    public static IReadOnlyList<SelectListItem> LimitPeriodOptions { get; } =
        Enum.GetValues<CardLimitPeriod>().Select(p => new SelectListItem(p.ToString(), p.ToString())).ToList();
}

public sealed class RejectFormViewModel
{
    [Required(ErrorMessage = "Enter a reason (the customer sees it).")]
    [StringLength(300)]
    public string Reason { get; set; } = "";
}

public sealed class RequestDetailsViewModel
{
    public RequestDetailsViewModel(LicenseRequestDto request, ApproveFormViewModel approve)
    {
        Request = request;
        Approve = approve;
    }

    public LicenseRequestDto Request { get; }

    public ApproveFormViewModel Approve { get; }

    public RejectFormViewModel Reject { get; } = new();
}
