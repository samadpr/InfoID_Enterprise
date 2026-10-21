namespace InfoID.LicenseManager.Application.DTOs.Accounts;

public sealed record SignUpInputDto(string FullName, string? CompanyName, string Email, string? Phone, string? Country, string Password, bool AcceptTerms);

public sealed record AccountProfileDto(string FullName, string? CompanyName, string Email, string? Phone, string? Country);

/// <summary>A verified, enabled account behind a valid session token.</summary>
public sealed record AccountSessionDto(long AccountId, AccountProfileDto Profile);

/// <param name="Message">The error when <paramref name="Succeeded"/> is false, or an informational note.</param>
/// <param name="NeedsVerification">The e-mail must be verified with a code before signing in.</param>
/// <param name="Token">The session token (only the caller ever sees it; the database keeps a hash).</param>
public sealed record AccountResultDto(bool Succeeded, string? Message, bool NeedsVerification = false, string? Token = null, AccountProfileDto? Profile = null)
{
    public static AccountResultDto Fail(string message) => new(false, message);
}

/// <summary>A plan request from the app. <see cref="Email"/> is ignored when a signed-in account is attached.</summary>
public sealed record SubmitLicenseRequestDto(
    long PlanId, int Machines, string FullName, string? CompanyName, string Email, string? Phone, string? Country,
    string? StateProvince, string? Message, string? MachineFingerprint, string? AppVersion);

public sealed record SubmittedRequestDto(bool Succeeded, string? Message, long? RequestId);
