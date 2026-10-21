using System.Security.Cryptography;
using System.Text;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Accounts;
using InfoID.LicenseManager.Application.DTOs.Customers;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Security;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace InfoID.LicenseManager.Application.Services;

public sealed class CustomerAccountService : ICustomerAccountService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
    private const string InvalidCredentials = "Email or password is incorrect.";
    private const string InvalidCode = "That code is not valid. Check it, or request a new one.";

    private readonly ICustomerAccountRepository _accounts;
    private readonly ILicenseRequestRepository _requests;
    private readonly IVerificationCodeService _verification;
    private readonly IPasswordHasher _hasher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<CustomerAccountService> _logger;

    public CustomerAccountService(ICustomerAccountRepository accounts, ILicenseRequestRepository requests, IVerificationCodeService verification,
        IPasswordHasher hasher, IUnitOfWork unitOfWork, IAuditService audit, ICurrentUser currentUser, TimeProvider time, ILogger<CustomerAccountService> logger)
    {
        _accounts = accounts;
        _requests = requests;
        _verification = verification;
        _hasher = hasher;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    // ================================================================ sign-up / verify ====

    public async Task<AccountResultDto> SignUpAsync(SignUpInputDto input, CancellationToken cancellationToken = default)
    {
        var email = EmailAddressNormalizer.Normalize(input.Email);
        if (string.IsNullOrWhiteSpace(input.FullName)) return AccountResultDto.Fail("Enter your full name.");
        if (email is null) return AccountResultDto.Fail("Enter a valid email address.");
        if (PasswordProblem(input.Password) is { } weak) return AccountResultDto.Fail(weak);
        if (!input.AcceptTerms) return AccountResultDto.Fail("Please accept the terms and privacy policy to create an account.");

        _unitOfWork.UseActor("CustomerSignUp");
        var account = await _accounts.FindByEmailAsync(email, cancellationToken);

        // A disabled account (License Manager > Customers) keeps its email reserved.
        if (account is { IsDisabled: true }) return AccountResultDto.Fail("This account has been disabled. Please contact support.");
        if (account is { EmailVerified: true })
            return AccountResultDto.Fail("An account with this email already exists. Log in, or use \"Forgot password\".");

        // New account, or a never-verified one being signed up again (a typo in the first attempt): take the latest details.
        if (account is null)
        {
            account = new CustomerAccount { Email = email };
            _accounts.Add(account);
        }

        account.FullName = Clean(input.FullName, 200)!;
        account.CompanyName = Clean(input.CompanyName, 200);
        account.Phone = Clean(input.Phone, 50);
        account.Country = Clean(input.Country, 100);
        account.PasswordHash = _hasher.Hash(input.Password);
        account.TermsAcceptedDate = UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var sent = await _verification.IssueAsync(account, EmailCodePurpose.VerifyEmail, respectResendInterval: false, cancellationToken);
        await _audit.LogAsync("Customer.SignedUp", new { account.Email }, cancellationToken: cancellationToken);

        return sent == VerificationCodeResult.Sent
            ? new AccountResultDto(true, null, NeedsVerification: true)
            : new AccountResultDto(true, "Your account was created, but the verification email couldn't be sent. Click \"Resend code\" in a minute.", NeedsVerification: true);
    }

    public async Task<AccountResultDto> VerifyEmailAsync(string emailText, string code, string? appVersion, CancellationToken cancellationToken = default)
    {
        var email = EmailAddressNormalizer.Normalize(emailText);
        if (email is null) return AccountResultDto.Fail("Enter a valid email address.");

        _unitOfWork.UseActor("CustomerVerify");
        var account = await _accounts.FindByEmailAsync(email, cancellationToken);
        if (account is null or { IsDisabled: true }) return AccountResultDto.Fail(InvalidCode);

        var problem = await _verification.ConsumeAsync(account, EmailCodePurpose.VerifyEmail, code, cancellationToken);
        if (problem is not null) return AccountResultDto.Fail(problem);

        account.EmailVerified = true;
        account.EmailVerifiedDate = UtcNow;
        account.LastLoginDate = UtcNow;
        var token = CreateSession(account, appVersion);
        var linked = await LinkEarlierRequestsAsync(account, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync("Customer.EmailVerified", new { account.Email, LinkedRequests = linked }, cancellationToken: cancellationToken);
        return new AccountResultDto(true, null, Token: token, Profile: ToProfile(account));
    }

    public async Task<AccountResultDto> ResendVerificationAsync(string emailText, CancellationToken cancellationToken = default)
    {
        var email = EmailAddressNormalizer.Normalize(emailText);
        if (email is null) return AccountResultDto.Fail("Enter a valid email address.");

        var account = await _accounts.FindByEmailAsync(email, cancellationToken);
        if (account is { EmailVerified: false, IsDisabled: false }
            && await _verification.IssueAsync(account, EmailCodePurpose.VerifyEmail, respectResendInterval: true, cancellationToken) == VerificationCodeResult.Throttled)
            return AccountResultDto.Fail("Please wait a minute before requesting another code.");

        return new AccountResultDto(true, null); // same answer whether or not the email exists
    }

    // ================================================================ login / session ====

    public async Task<AccountResultDto> LogInAsync(string emailText, string password, string? appVersion, CancellationToken cancellationToken = default)
    {
        var email = EmailAddressNormalizer.Normalize(emailText);
        if (email is null || string.IsNullOrEmpty(password)) return AccountResultDto.Fail(InvalidCredentials);

        _unitOfWork.UseActor("CustomerLogin");
        var account = await _accounts.FindByEmailAsync(email, cancellationToken);
        if (account is null or { IsDisabled: true })
        {
            _hasher.SimulateVerify(password); // similar response time whether or not the account exists
            return AccountResultDto.Fail(InvalidCredentials);
        }

        var now = UtcNow;
        if (account.IsLockedOut(now))
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((account.LockoutEndUtc!.Value - now).TotalMinutes));
            return AccountResultDto.Fail($"Too many failed attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}, or use \"Forgot password\".");
        }

        if (!_hasher.Verify(password, account.PasswordHash))
        {
            account.RegisterFailedLogin(now);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return AccountResultDto.Fail(InvalidCredentials);
        }

        account.ClearLoginFailures();
        if (!account.EmailVerified)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _verification.IssueAsync(account, EmailCodePurpose.VerifyEmail, respectResendInterval: true, cancellationToken);
            return new AccountResultDto(true, "Please verify your email first. We sent you a code.", NeedsVerification: true);
        }

        account.LastLoginDate = now;
        var token = CreateSession(account, appVersion);
        await LinkEarlierRequestsAsync(account, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new AccountResultDto(true, null, Token: token, Profile: ToProfile(account));
    }

    public async Task LogOutAsync(string token, CancellationToken cancellationToken = default)
    {
        var session = await _accounts.FindLiveSessionAsync(HashToken(token), cancellationToken);
        if (session is null) return;

        session.RevokedUtc = UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<AccountSessionDto?> GetSessionAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var session = await _accounts.FindLiveSessionAsync(HashToken(token), cancellationToken);
        var now = UtcNow;
        if (session?.CustomerAccount is not { IsDisabled: false, EmailVerified: true } account || session.ExpiresUtc < now) return null;

        if (session.ExpiresUtc - now < SessionLifetime / 2)
        {
            _unitOfWork.UseActor("CustomerSession");
            session.ExpiresUtc = now.Add(SessionLifetime);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new AccountSessionDto(account.Id, ToProfile(account));
    }

    // ================================================================ password reset ====

    public async Task<AccountResultDto> ForgotPasswordAsync(string emailText, CancellationToken cancellationToken = default)
    {
        var email = EmailAddressNormalizer.Normalize(emailText);
        if (email is null) return AccountResultDto.Fail("Enter a valid email address.");

        var account = await _accounts.FindByEmailAsync(email, cancellationToken);
        if (account is { IsDisabled: false })
            await _verification.IssueAsync(account, EmailCodePurpose.ResetPassword, respectResendInterval: true, cancellationToken);

        return new AccountResultDto(true, null); // same answer whether or not the email exists
    }

    public async Task<AccountResultDto> ResetPasswordAsync(string emailText, string code, string newPassword, string? appVersion, CancellationToken cancellationToken = default)
    {
        var email = EmailAddressNormalizer.Normalize(emailText);
        if (email is null) return AccountResultDto.Fail("Enter a valid email address.");
        if (PasswordProblem(newPassword) is { } weak) return AccountResultDto.Fail(weak);

        _unitOfWork.UseActor("CustomerReset");
        var account = await _accounts.FindByEmailAsync(email, cancellationToken);
        if (account is null or { IsDisabled: true }) return AccountResultDto.Fail(InvalidCode);

        var problem = await _verification.ConsumeAsync(account, EmailCodePurpose.ResetPassword, code, cancellationToken);
        if (problem is not null) return AccountResultDto.Fail(problem);

        account.PasswordHash = _hasher.Hash(newPassword);
        account.ClearLoginFailures();
        // Receiving the code proves the mailbox, so this also verifies the e-mail.
        if (!account.EmailVerified)
        {
            account.EmailVerified = true;
            account.EmailVerifiedDate = UtcNow;
        }

        // Sign out everywhere else: a reset usually means the old password leaked.
        await _accounts.RevokeActiveSessionsAsync(account.Id, UtcNow, cancellationToken);

        account.LastLoginDate = UtcNow;
        var token = CreateSession(account, appVersion);
        await LinkEarlierRequestsAsync(account, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Customer account {AccountId} reset its password", account.Id);
        await _audit.LogAsync("Customer.PasswordReset", new { account.Email }, cancellationToken: cancellationToken);
        return new AccountResultDto(true, null, Token: token, Profile: ToProfile(account));
    }

    // ================================================================ my licenses / requests ====

    public Task<IReadOnlyList<AccountLicenseDto>> GetLicensesAsync(long accountId, CancellationToken cancellationToken = default) =>
        _accounts.GetLicensesAsync(accountId, cancellationToken);

    public Task<IReadOnlyList<AccountRequestDto>> GetRequestsAsync(long accountId, CancellationToken cancellationToken = default) =>
        _accounts.GetRequestsAsync(accountId, cancellationToken);

    // ================================================================ helpers ====

    /// <summary>
    /// Requests sent WITHOUT signing in, from this account's (verified) e-mail, become this account's, together with the
    /// activation codes created from them, so a customer who requests first and signs up later sees everything under My
    /// licenses / My requests. Only for a verified e-mail (the code proves the mailbox), and never takes over anything
    /// already linked to another account. The caller saves.
    /// </summary>
    private async Task<int> LinkEarlierRequestsAsync(CustomerAccount account, CancellationToken cancellationToken)
    {
        if (!account.EmailVerified) return 0;

        var requests = await _requests.GetUnlinkedByEmailAsync(account.Email, cancellationToken);
        foreach (var request in requests)
        {
            request.CustomerAccountId = account.Id;
            if (request.LicenseCode is { CustomerAccountId: null } code)
            {
                code.CustomerAccountId = account.Id;
                if (account.CustomerRecordId is null && account.CustomerRecord is null) account.CustomerRecordId = code.CustomerRecordId;
            }
        }

        return requests.Count;
    }

    private string CreateSession(CustomerAccount account, string? appVersion)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _accounts.AddSession(new CustomerSession
        {
            CustomerAccount = account,
            TokenHash = HashToken(token),
            ExpiresUtc = UtcNow.Add(SessionLifetime),
            CreatedFromIp = Clean(_currentUser.ClientAddress, 50),
            AppVersion = Clean(appVersion, 50),
        });
        return token;
    }

    /// <summary>Only this hash is stored; a database leak does not expose usable tokens. Same format as the legacy server.</summary>
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static AccountProfileDto ToProfile(CustomerAccount a) => new(a.FullName, a.CompanyName, a.Email, a.Phone, a.Country);

    /// <summary>The customer policy (lighter than staff): 8-128 characters with letters and numbers.</summary>
    public static string? PasswordProblem(string? password) =>
        password is null || password.Length < 8 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit)
            ? "Use a password of at least 8 characters with letters and numbers."
            : password.Length > 128 ? "Use a password of at most 128 characters."
            : null;

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
