using InfoID.LicenseManager.Application.DTOs.Accounts;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.Security;
using InfoID.Shared.Contracts.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InfoID.LicenseManager.Web.Api;

/// <summary>Customer accounts for the app: sign-up, e-mail verification, login, password reset, "My licenses" and
/// "My requests". Sign-in is optional for customers; activation codes never need it.</summary>
[Route("api/v1/account")]
[EnableRateLimiting(RateLimitPolicies.Account)]
public sealed class AccountApiController : ApiControllerBase
{
    private readonly ICustomerAccountService _accounts;

    public AccountApiController(ICustomerAccountService accounts) => _accounts = accounts;

    [HttpPost("signup")]
    public async Task<AccountResponse> SignUp(SignUpRequest r, CancellationToken ct) =>
        ToResponse(await _accounts.SignUpAsync(new SignUpInputDto(r.FullName, r.CompanyName, r.Email, r.Phone, r.Country, r.Password, r.AcceptTerms), ct));

    [HttpPost("verify")]
    public async Task<AccountResponse> Verify(VerifyEmailRequest r, CancellationToken ct) =>
        ToResponse(await _accounts.VerifyEmailAsync(r.Email, r.Code, r.AppVersion, ct));

    [HttpPost("resend")]
    public async Task<AccountResponse> Resend(EmailOnlyRequest r, CancellationToken ct) =>
        ToResponse(await _accounts.ResendVerificationAsync(r.Email, ct));

    [HttpPost("login")]
    public async Task<AccountResponse> LogIn(LogInRequest r, CancellationToken ct) =>
        ToResponse(await _accounts.LogInAsync(r.Email, r.Password, r.AppVersion, ct));

    [HttpPost("forgot")]
    public async Task<AccountResponse> Forgot(EmailOnlyRequest r, CancellationToken ct) =>
        ToResponse(await _accounts.ForgotPasswordAsync(r.Email, ct));

    [HttpPost("reset")]
    public async Task<AccountResponse> Reset(ResetPasswordRequest r, CancellationToken ct) =>
        ToResponse(await _accounts.ResetPasswordAsync(r.Email, r.Code, r.NewPassword, r.AppVersion, ct));

    [HttpPost("logout")]
    public async Task<AccountResponse> LogOut(CancellationToken ct)
    {
        if (BearerToken() is { } token) await _accounts.LogOutAsync(token, ct);
        return new AccountResponse { Success = true };
    }

    [HttpGet("me")]
    public async Task<ActionResult<AccountResponse>> Me(CancellationToken ct) =>
        await GetSessionAsync(_accounts, ct) is { } session
            ? new AccountResponse { Success = true, Account = ToProfile(session.Profile) }
            : Unauthorized();

    [HttpGet("licenses")]
    public async Task<ActionResult<List<AccountLicenseDto>>> Licenses(CancellationToken ct)
    {
        if (await GetSessionAsync(_accounts, ct) is not { } session) return Unauthorized();
        var list = await _accounts.GetLicensesAsync(session.AccountId, ct);
        return list.Select(l => new AccountLicenseDto
        {
            ActivationCode = l.ActivationCode,
            ProductCode = l.ProductCode,
            Tier = l.Tier,
            LicenseType = l.LicenseType,
            ExpiryDate = l.ExpiryDate,
            MaxActivations = l.MaxActivations,
            ActiveMachines = l.ActiveMachines,
            Status = l.Status,
        }).ToList();
    }

    [HttpGet("requests")]
    public async Task<ActionResult<List<AccountRequestDto>>> Requests(CancellationToken ct)
    {
        if (await GetSessionAsync(_accounts, ct) is not { } session) return Unauthorized();
        var list = await _accounts.GetRequestsAsync(session.AccountId, ct);
        return list.Select(r => new AccountRequestDto
        {
            Id = r.Id,
            PlanName = r.PlanName,
            Machines = r.Machines,
            Status = r.Status,
            StatusReason = r.StatusReason,
            CreatedDate = r.CreatedDate,
            ActivationCode = r.ActivationCode,
        }).ToList();
    }

    private static AccountResponse ToResponse(AccountResultDto result) => new()
    {
        Success = result.Succeeded,
        Message = result.Message,
        NeedsVerification = result.NeedsVerification,
        Token = result.Token,
        Account = result.Profile is { } profile && result.Token is not null ? ToProfile(profile) : null,
    };

    private static InfoID.Shared.Contracts.Accounts.AccountProfileDto ToProfile(InfoID.LicenseManager.Application.DTOs.Accounts.AccountProfileDto p) => new()
    {
        FullName = p.FullName,
        CompanyName = p.CompanyName,
        Email = p.Email,
        Phone = p.Phone,
        Country = p.Country,
    };
}
