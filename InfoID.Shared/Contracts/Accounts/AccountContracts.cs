namespace InfoID.Shared.Contracts.Accounts;

/// <summary>
/// HTTPS messages between the app and the License API for customer accounts, plans and
/// license requests. Signed-in calls send "Authorization: Bearer &lt;token&gt;".
/// </summary>
public class SignUpRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public string Password { get; set; } = string.Empty;
    public bool AcceptTerms { get; set; }
}

public class VerifyEmailRequest
{
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
}

public class EmailOnlyRequest
{
    public string Email { get; set; } = string.Empty;
}

public class LogInRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
}

public class ResetPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
}

/// <summary>Answer to sign-up / verify / login / reset.</summary>
public class AccountResponse
{
    public bool Success { get; set; }
    /// <summary>Error, or an informational note when Success is true.</summary>
    public string? Message { get; set; }
    /// <summary>The email must be verified with a code before signing in.</summary>
    public bool NeedsVerification { get; set; }
    /// <summary>Session token (signed in) -- keep it private.</summary>
    public string? Token { get; set; }
    public AccountProfileDto? Account { get; set; }
}

public class AccountProfileDto
{
    public string FullName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Country { get; set; }
}

public class AccountLicenseDto
{
    public string ActivationCode { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string LicenseType { get; set; } = string.Empty;
    public DateOnly? ExpiryDate { get; set; }
    public int MaxActivations { get; set; }
    public int ActiveMachines { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class AccountRequestDto
{
    public long Id { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public int Machines { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? StatusReason { get; set; }
    public DateTime CreatedDate { get; set; }
    public string? ActivationCode { get; set; }
}

public class PlanDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string? Tagline { get; set; }
    public List<string> Features { get; set; } = new();
    public int? DurationMonths { get; set; }
    public int DefaultMachines { get; set; }
    public int? MaxCardsPerPeriod { get; set; }
    public string CardLimitPeriod { get; set; } = string.Empty;
    public string? PriceText { get; set; }
}

public class PlanRequestSubmission
{
    public long PlanId { get; set; }
    public int Machines { get; set; } = 1;
    public string FullName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    /// <summary>Ignored when signed in (the account's verified email is used).</summary>
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public string? StateProvince { get; set; }
    public string? Message { get; set; }
    public string? MachineFingerprint { get; set; }
    public string? AppVersion { get; set; }
}

public class PlanRequestResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public long? RequestId { get; set; }
}
