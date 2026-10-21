using InfoID.Licensing.Core;

namespace InfoID.LicenseManager.Application.DTOs.Requests;

/// <summary>Status filter values besides the real statuses.</summary>
public static class RequestStatusFilter
{
    public const string Open = "Open";
    public const string All = "All";
}

/// <param name="Status">"Open" (new + in progress, the default), "All", or one real status.</param>
public sealed record LicenseRequestSearchDto(string? Status = RequestStatusFilter.Open, string? Text = null, int Take = 500);

public sealed record LicenseRequestDto(
    long Id,
    DateTime CreatedDate,
    string Status,
    string PlanName,
    string PlanTier,
    int? PlanDurationMonths,
    int? PlanMaxCards,
    string PlanLimitPeriod,
    int Machines,
    string FullName,
    string? CompanyName,
    string Email,
    string? Phone,
    string? Country,
    string? StateProvince,
    string? Message,
    bool HasAccount,
    string? StatusReason,
    string? ActivationCode,
    string? HandledBy,
    DateTime? HandledDate)
{
    public bool IsOpen => Status is "New" or "InProgress";
}

/// <summary>What staff confirm when approving (pre-filled from the plan and the request).</summary>
public sealed record ApproveLicenseRequestDto(
    long RequestId,
    LicenseTier Tier,
    LicenseType Type,
    DateOnly? ExpiryDate,
    int? MaxCardsPerPeriod,
    CardLimitPeriod LimitPeriod,
    int MaxActivations,
    bool AllowOfflineActivation,
    string? Notes);

public sealed record RejectLicenseRequestDto(long RequestId, string Reason);

/// <param name="EmailSent">False when the customer e-mail could not be sent; staff must pass the code on.</param>
public sealed record ApprovedLicenseRequestDto(string ActivationCode, bool EmailSent);
