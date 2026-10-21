using InfoID.LicenseManager.Application.DTOs.Accounts;
using InfoID.LicenseManager.Application.DTOs.Customers;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>
/// Customer accounts for the app's optional sign-in (one account for all products): sign-up with MANDATORY e-mail
/// verification (6-digit code), login with lockout, sessions (random token, only its hash stored), forgot/reset password.
/// Activation codes never need an account. Answers avoid revealing which e-mails have accounts wherever possible.
/// </summary>
public interface ICustomerAccountService
{
    Task<AccountResultDto> SignUpAsync(SignUpInputDto input, CancellationToken cancellationToken = default);

    Task<AccountResultDto> VerifyEmailAsync(string email, string code, string? appVersion, CancellationToken cancellationToken = default);

    Task<AccountResultDto> ResendVerificationAsync(string email, CancellationToken cancellationToken = default);

    Task<AccountResultDto> LogInAsync(string email, string password, string? appVersion, CancellationToken cancellationToken = default);

    Task LogOutAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>The verified, enabled account behind a session token, or null. Active sessions are extended (sliding expiry).</summary>
    Task<AccountSessionDto?> GetSessionAsync(string? token, CancellationToken cancellationToken = default);

    Task<AccountResultDto> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    Task<AccountResultDto> ResetPasswordAsync(string email, string code, string newPassword, string? appVersion, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountLicenseDto>> GetLicensesAsync(long accountId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountRequestDto>> GetRequestsAsync(long accountId, CancellationToken cancellationToken = default);
}
