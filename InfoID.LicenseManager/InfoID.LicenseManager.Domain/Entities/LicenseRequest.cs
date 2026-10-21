using InfoID.LicenseManager.Domain.Exceptions;

namespace InfoID.LicenseManager.Domain.Entities;

public static class LicenseRequestStatus
{
    public const string New = "New";
    public const string InProgress = "InProgress";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

/// <summary>A customer's request for a plan (from the app). Staff confirm payment
/// outside the system, then Approve -- which creates the activation code.</summary>
public class LicenseRequest : EntityBase
{
    public string ProductCode { get; set; } = ProductCodes.InfoID;
    public long PlanId { get; set; }
    public Plan? Plan { get; set; }
    public long? CustomerAccountId { get; set; }
    public CustomerAccount? CustomerAccount { get; set; }
    public int Machines { get; set; } = 1;

    public string FullName { get; set; } = "";
    public string? CompanyName { get; set; }
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public string? StateProvince { get; set; }
    public string? Message { get; set; }

    public string Status { get; set; } = LicenseRequestStatus.New;
    public string? StatusReason { get; set; }
    public long? HandledByAdminUserId { get; set; }
    public AdminUser? HandledByAdminUser { get; set; }
    public DateTime? HandledDate { get; set; }
    /// <summary>The activation code created on approval.</summary>
    public long? LicenseCodeId { get; set; }
    public LicenseCode? LicenseCode { get; set; }

    /// <summary>The requesting PC (for support); not used for activation.</summary>
    public string? MachineFingerprint { get; set; }
    public string? AppVersion { get; set; }

    public const int MaxOpenRequestsPerEmail = 5;

    /// <summary>A customer asks for a plan (from the app). Text is trimmed and cut to the column sizes.</summary>
    public static LicenseRequest Submit(Plan plan, int machines, string fullName, string email, long? customerAccountId,
        string? companyName, string? phone, string? country, string? stateProvince, string? message, string? machineFingerprint, string? appVersion)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainRuleException("Enter your name.");
        if (machines is < 1 or > LicenseCode.MaxMachinesPerCode) throw new DomainRuleException("Number of computers must be between 1 and 100.");

        return new LicenseRequest
        {
            ProductCode = plan.ProductCode,
            PlanId = plan.Id,
            CustomerAccountId = customerAccountId,
            Machines = machines,
            FullName = Clean(fullName, 200)!,
            CompanyName = Clean(companyName, 200),
            Email = email,
            Phone = Clean(phone, 50),
            Country = Clean(country, 100),
            StateProvince = Clean(stateProvince, 100),
            Message = Clean(message, 2000),
            MachineFingerprint = Clean(machineFingerprint, 100),
            AppVersion = Clean(appVersion, 50),
        };
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    /// <summary>New or in progress: staff can still approve or reject it.</summary>
    public bool IsOpen => Status is LicenseRequestStatus.New or LicenseRequestStatus.InProgress;

    public void MarkInProgress(long? adminUserId, DateTime utcNow)
    {
        EnsureOpen();
        Status = LicenseRequestStatus.InProgress;
        Handle(adminUserId, utcNow);
    }

    /// <summary>Approving links the activation code created for it. The code is stored with the request,
    /// so the customer sees it under "My licenses"/"My requests".</summary>
    public void Approve(LicenseCode licenseCode, long? adminUserId, DateTime utcNow)
    {
        EnsureOpen();
        Status = LicenseRequestStatus.Approved;
        StatusReason = null;
        LicenseCode = licenseCode;
        Handle(adminUserId, utcNow);
    }

    /// <summary>The reason is shown to the customer, so it is required.</summary>
    public void Reject(string reason, long? adminUserId, DateTime utcNow)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("Enter a reason (the customer sees it).");

        Status = LicenseRequestStatus.Rejected;
        StatusReason = reason.Trim();
        Handle(adminUserId, utcNow);
    }

    private void Handle(long? adminUserId, DateTime utcNow)
    {
        HandledByAdminUserId = adminUserId;
        HandledDate = utcNow;
    }

    public void EnsureOpen()
    {
        if (!IsOpen)
            throw new DomainRuleException($"This request is already {Status.ToLowerInvariant()}.");
    }
}
