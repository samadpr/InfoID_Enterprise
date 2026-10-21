namespace InfoID.LicenseManager.Application.DTOs.Customers;

public enum CustomerFilter
{
    All,
    /// <summary>Verified, active, and nothing requested or licensed yet: a lead for sales.</summary>
    Leads,
    Verified,
    NotVerified,
    Disabled,
}

public enum CustomerStatus
{
    Disabled,
    NotVerified,
    Lead,
    Customer,
}

public sealed record CustomerSearchDto(CustomerFilter Filter = CustomerFilter.All, string? Text = null, int Take = 1000);

public sealed record CustomerListItemDto(
    long Id,
    string FullName,
    string? CompanyName,
    string Email,
    string? Phone,
    string? Country,
    DateTime CreatedDate,
    bool EmailVerified,
    DateTime? LastLoginDate,
    bool Disabled,
    int RequestCount,
    int LicenseCount)
{
    public bool IsLead => EmailVerified && !Disabled && RequestCount == 0 && LicenseCount == 0;

    public CustomerStatus Status => Disabled ? CustomerStatus.Disabled
        : !EmailVerified ? CustomerStatus.NotVerified
        : IsLead ? CustomerStatus.Lead
        : CustomerStatus.Customer;
}

/// <summary>A license that belongs to an account ("My licenses" in the app).</summary>
public sealed record AccountLicenseDto(
    string ActivationCode,
    string ProductCode,
    string Tier,
    string LicenseType,
    DateOnly? ExpiryDate,
    int MaxActivations,
    int ActiveMachines,
    string Status,
    bool AllowOfflineActivation);

public sealed record AccountRequestDto(
    long Id,
    string PlanName,
    int Machines,
    string Status,
    string? StatusReason,
    DateTime CreatedDate,
    string? ActivationCode);

public sealed record CustomerDetailsDto(
    CustomerListItemDto Customer,
    IReadOnlyList<AccountLicenseDto> Licenses,
    IReadOnlyList<AccountRequestDto> Requests);

public sealed record SetCustomerDisabledRequestDto(long AccountId, bool Disabled, string? Reason);
