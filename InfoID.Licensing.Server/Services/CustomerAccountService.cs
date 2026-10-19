using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using InfoID.Licensing.Core;
using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Licensing.Server.Services;

public sealed record AccountResult(bool Succeeded, string? Error, string? Token = null, CustomerAccount? Account = null, bool NeedsVerification = false)
{
    public static AccountResult Fail(string error) => new(false, error);
}

public sealed record SignUpInput(string FullName, string? CompanyName, string Email, string? Phone, string? Country, string Password, bool AcceptTerms);

/// <summary>A license that belongs to an account ("My licenses" in the app).</summary>
public sealed record AccountLicense(string ActivationCode, string ProductCode, string Tier, string LicenseType, DateOnly? ExpiryDate,
    int MaxActivations, int ActiveMachines, string Status, bool AllowOfflineActivation);

public sealed record AccountRequest(long Id, string PlanName, int Machines, string Status, string? StatusReason, DateTime CreatedDate, string? ActivationCode);

/// <summary>
/// Customer accounts for the app's optional sign-in (one account for all products):
/// sign-up with MANDATORY email verification (6-digit code), login with lockout,
/// sessions (random token, only its hash stored), forgot/reset password.
///
/// Security: passwords PBKDF2 (same hasher as staff); codes hashed, 15-minute expiry,
/// 5 attempts, resend at most once a minute; generic answers where needed so the API
/// doesn't reveal which emails have accounts.
/// </summary>
public sealed class CustomerAccountService
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<CustomerAccountService>();

    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const int MaxCodeAttempts = 5;
    private const int MaxFailedLogins = 5;

    private readonly IDbContextFactory<LicenseDbContext> _dbFactory;
    private readonly IEmailSender _email;
    private readonly AuditService _audit;
    private readonly AdminContext _context;

    public CustomerAccountService(IDbContextFactory<LicenseDbContext> dbFactory, IEmailSender email, AuditService audit, AdminContext context)
    {
        _dbFactory = dbFactory;
        _email = email;
        _audit = audit;
        _context = context;
    }

    // ================================================================ sign-up / verify ====

    public async Task<AccountResult> SignUpAsync(SignUpInput input)
    {
        var email = NormalizeEmail(input.Email);
        if (string.IsNullOrWhiteSpace(input.FullName)) return AccountResult.Fail("Enter your full name.");
        if (email is null) return AccountResult.Fail("Enter a valid email address.");
        if (PasswordProblem(input.Password) is { } weak) return AccountResult.Fail(weak);
        if (!input.AcceptTerms) return AccountResult.Fail("Please accept the terms and privacy policy to create an account.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = "CustomerSignUp";

        // A disabled account (License Manager > Customers) keeps its email reserved.
        if (await db.CustomerAccounts.AnyAsync(a => a.Email == email && a.Cancelled))
            return AccountResult.Fail("This account has been disabled. Please contact support.");

        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Email == email && !a.Cancelled);

        if (account is { EmailVerified: true })
            return AccountResult.Fail("An account with this email already exists. Log in, or use \"Forgot password\".");

        // New account, or a never-verified one being signed up again (e.g. typo in the
        // first attempt's details): take the latest details and send a fresh code.
        if (account is null)
        {
            account = new CustomerAccount { Email = email };
            db.CustomerAccounts.Add(account);
        }

        account.FullName = input.FullName.Trim();
        account.CompanyName = Clean(input.CompanyName);
        account.Phone = Clean(input.Phone);
        account.Country = Clean(input.Country);
        account.PasswordHash = PasswordHasher.Hash(input.Password);
        account.TermsAcceptedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var sent = await SendCodeAsync(db, account, EmailCodePurpose.VerifyEmail, respectResendInterval: false);
        await _audit.LogAsync("Customer.SignedUp", new { account.Email });

        return sent
            ? new AccountResult(true, null, Account: account, NeedsVerification: true)
            : new AccountResult(true, "Your account was created, but the verification email couldn't be sent. Click \"Resend code\" in a minute.", Account: account, NeedsVerification: true);
    }

    public async Task<AccountResult> VerifyEmailAsync(string emailText, string code, string? appVersion)
    {
        var email = NormalizeEmail(emailText);
        if (email is null) return AccountResult.Fail("Enter a valid email address.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = "CustomerVerify";
        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Email == email && !a.Cancelled);
        if (account is null) return AccountResult.Fail("That code is not valid. Check it, or request a new one.");

        var problem = await ConsumeCodeAsync(db, account, EmailCodePurpose.VerifyEmail, code);
        if (problem is not null) return AccountResult.Fail(problem);

        account.EmailVerified = true;
        account.EmailVerifiedDate = DateTime.UtcNow;
        account.LastLoginDate = DateTime.UtcNow;
        var token = CreateSession(db, account, appVersion);
        var linked = await LinkEarlierRequestsAsync(db, account);
        await db.SaveChangesAsync();

        await _audit.LogAsync("Customer.EmailVerified", new { account.Email, LinkedRequests = linked });
        return new AccountResult(true, null, token, account);
    }

    public async Task<AccountResult> ResendVerificationAsync(string emailText)
    {
        var email = NormalizeEmail(emailText);
        if (email is null) return AccountResult.Fail("Enter a valid email address.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Email == email && !a.Cancelled);
        if (account is { EmailVerified: false })
        {
            if (!await SendCodeAsync(db, account, EmailCodePurpose.VerifyEmail, respectResendInterval: true))
                return AccountResult.Fail("Please wait a minute before requesting another code.");
        }

        // Same answer whether or not the email exists.
        return new AccountResult(true, null);
    }

    // ================================================================ login / session ====

    public async Task<AccountResult> LogInAsync(string emailText, string password, string? appVersion)
    {
        var email = NormalizeEmail(emailText);
        const string invalid = "Email or password is incorrect.";
        if (email is null || string.IsNullOrEmpty(password)) return AccountResult.Fail(invalid);

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = "CustomerLogin";
        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Email == email && !a.Cancelled);
        if (account is null)
        {
            PasswordHasher.Verify(password, PasswordHasher.Hash("timing-equalizer")); // similar response time
            return AccountResult.Fail(invalid);
        }

        if (account.LockoutEndUtc is { } until && until > DateTime.UtcNow)
            return AccountResult.Fail($"Too many failed attempts. Try again after {until.ToLocalTime():t}, or use \"Forgot password\".");

        if (!PasswordHasher.Verify(password, account.PasswordHash))
        {
            account.FailedLoginCount++;
            if (account.FailedLoginCount >= MaxFailedLogins)
            {
                account.LockoutEndUtc = DateTime.UtcNow.Add(LockoutDuration);
                account.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync();
            return AccountResult.Fail(invalid);
        }

        account.FailedLoginCount = 0;
        account.LockoutEndUtc = null;

        if (!account.EmailVerified)
        {
            await db.SaveChangesAsync();
            await SendCodeAsync(db, account, EmailCodePurpose.VerifyEmail, respectResendInterval: true);
            return new AccountResult(true, "Please verify your email first. We sent you a code.", Account: account, NeedsVerification: true);
        }

        account.LastLoginDate = DateTime.UtcNow;
        var token = CreateSession(db, account, appVersion);
        await LinkEarlierRequestsAsync(db, account);
        await db.SaveChangesAsync();
        return new AccountResult(true, null, token, account);
    }

    public async Task LogOutAsync(string token)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var hash = HashToken(token);
        var session = await db.CustomerSessions.FirstOrDefaultAsync(s => s.TokenHash == hash);
        if (session is null) return;
        session.RevokedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>The verified account behind a session token, or null. Sliding expiry:
    /// active sessions are extended.</summary>
    public async Task<CustomerAccount?> GetAccountByTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var hash = HashToken(token);
        var session = await db.CustomerSessions.Include(s => s.CustomerAccount)
            .FirstOrDefaultAsync(s => s.TokenHash == hash && s.RevokedUtc == null && !s.Cancelled);
        if (session?.CustomerAccount is not { Cancelled: false, EmailVerified: true } account || session.ExpiresUtc < DateTime.UtcNow)
            return null;

        if (session.ExpiresUtc - DateTime.UtcNow < SessionLifetime / 2)
        {
            session.ExpiresUtc = DateTime.UtcNow.Add(SessionLifetime);
            await db.SaveChangesAsync();
        }
        return account;
    }

    // ================================================================ password reset ====

    public async Task<AccountResult> ForgotPasswordAsync(string emailText)
    {
        var email = NormalizeEmail(emailText);
        if (email is null) return AccountResult.Fail("Enter a valid email address.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Email == email && !a.Cancelled);
        if (account is not null)
            await SendCodeAsync(db, account, EmailCodePurpose.ResetPassword, respectResendInterval: true);

        // Same answer whether or not the email exists.
        return new AccountResult(true, null);
    }

    public async Task<AccountResult> ResetPasswordAsync(string emailText, string code, string newPassword, string? appVersion)
    {
        var email = NormalizeEmail(emailText);
        if (email is null) return AccountResult.Fail("Enter a valid email address.");
        if (PasswordProblem(newPassword) is { } weak) return AccountResult.Fail(weak);

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = "CustomerReset";
        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Email == email && !a.Cancelled);
        if (account is null) return AccountResult.Fail("That code is not valid. Check it, or request a new one.");

        var problem = await ConsumeCodeAsync(db, account, EmailCodePurpose.ResetPassword, code);
        if (problem is not null) return AccountResult.Fail(problem);

        account.PasswordHash = PasswordHasher.Hash(newPassword);
        account.FailedLoginCount = 0;
        account.LockoutEndUtc = null;
        // Receiving the code proves the mailbox, so this also verifies the email.
        if (!account.EmailVerified)
        {
            account.EmailVerified = true;
            account.EmailVerifiedDate = DateTime.UtcNow;
        }

        // Sign out everywhere else: a reset usually means the old password leaked.
        foreach (var session in await db.CustomerSessions.Where(s => s.CustomerAccountId == account.Id && s.RevokedUtc == null).ToListAsync())
            session.RevokedUtc = DateTime.UtcNow;

        account.LastLoginDate = DateTime.UtcNow;
        var token = CreateSession(db, account, appVersion);
        await LinkEarlierRequestsAsync(db, account);
        await db.SaveChangesAsync();

        await _audit.LogAsync("Customer.PasswordReset", new { account.Email });
        return new AccountResult(true, null, token, account);
    }

    // ================================================================ my licenses / requests ====

    public async Task<IReadOnlyList<AccountLicense>> GetLicensesAsync(long accountId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var codes = await db.LicenseCodes.AsNoTracking().Include(c => c.Activations)
            .Where(c => c.CustomerAccountId == accountId && !c.Cancelled)
            .OrderByDescending(c => c.CreatedDate)
            .ToListAsync();

        return codes.Select(c => new AccountLicense(
            ActivationCode.TryParse(c.ActivationCode, out var parsed) ? parsed!.ToString() : c.ActivationCode,
            c.ProductCode, c.Tier, c.LicenseType, c.ExpiryDate, c.MaxActivations,
            c.Activations.Count(a => a.Status == ActivationStatus.Active && !a.Cancelled),
            c.Status, c.AllowOfflineActivation)).ToList();
    }

    public async Task<IReadOnlyList<AccountRequest>> GetRequestsAsync(long accountId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var requests = await db.LicenseRequests.AsNoTracking().Include(r => r.Plan).Include(r => r.LicenseCode)
            .Where(r => r.CustomerAccountId == accountId && !r.Cancelled)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync();

        return requests.Select(r => new AccountRequest(r.Id, r.Plan?.Name ?? "", r.Machines, r.Status, r.StatusReason, r.CreatedDate,
            r.LicenseCode is { } c && ActivationCode.TryParse(c.ActivationCode, out var parsed) ? parsed!.ToString() : null)).ToList();
    }

    // ================================================================ helpers ====

    /// <summary>
    /// Requests sent WITHOUT signing in, from this account's (verified) email, become
    /// this account's -- together with the activation codes created from them. So a
    /// customer who requests first and signs up later sees everything under My licenses /
    /// My requests. Only ever runs for a verified email (the code proves the mailbox),
    /// and never takes over anything already linked to another account.
    /// Returns how many requests were linked. Caller saves.
    /// </summary>
    private static async Task<int> LinkEarlierRequestsAsync(LicenseDbContext db, CustomerAccount account)
    {
        if (!account.EmailVerified) return 0;

        var requests = await db.LicenseRequests.Include(r => r.LicenseCode)
            .Where(r => r.Email == account.Email && r.CustomerAccountId == null && !r.Cancelled)
            .ToListAsync();

        foreach (var request in requests)
        {
            request.CustomerAccountId = account.Id;
            if (request.LicenseCode is { CustomerAccountId: null } code)
            {
                code.CustomerAccountId = account.Id;
                account.CustomerRecordId ??= code.CustomerRecordId;
            }
        }
        return requests.Count;
    }

    /// <summary>Creates and emails a new 6-digit code (older unused codes of the same
    /// purpose stop working). False if throttled or the email couldn't be sent.</summary>
    private async Task<bool> SendCodeAsync(LicenseDbContext db, CustomerAccount account, string purpose, bool respectResendInterval)
    {
        var previous = await db.EmailCodes
            .Where(c => c.CustomerAccountId == account.Id && c.Purpose == purpose && c.UsedUtc == null)
            .ToListAsync();
        if (respectResendInterval && previous.Any(c => c.CreatedDate > DateTime.UtcNow - ResendInterval))
            return false;
        foreach (var old in previous) old.UsedUtc = DateTime.UtcNow;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.EmailCodes.Add(new EmailCode
        {
            CustomerAccountId = account.Id,
            Purpose = purpose,
            CodeHash = HashCode(account.Id, purpose, code),
            ExpiresUtc = DateTime.UtcNow.Add(CodeLifetime),
        });
        await db.SaveChangesAsync();

        var (subject, body) = purpose == EmailCodePurpose.ResetPassword
            ? ("Your password reset code",
               $"Hello {account.FullName},\n\nYour password reset code is: {code}\n\nIt expires in 15 minutes. If you didn't ask to reset your password, you can ignore this email.")
            : ("Verify your email",
               $"Hello {account.FullName},\n\nYour verification code is: {code}\n\nEnter it in the app to finish creating your account. It expires in 15 minutes.\nIf you didn't create an account, you can ignore this email.");
        try
        {
            await _email.SendAsync(account.Email, subject, body);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"{purpose} email to {InfoID.Shared.Logging.LogText.MaskEmail(account.Email)} could not be sent", ex);
            await _audit.LogAsync("Customer.EmailFailed", new { account.Email, purpose, Error = ex.GetBaseException().Message });
            return false;
        }
    }

    /// <summary>Checks a code; null on success (code marked used), else the reason.</summary>
    private static async Task<string?> ConsumeCodeAsync(LicenseDbContext db, CustomerAccount account, string purpose, string code)
    {
        const string invalid = "That code is not valid. Check it, or request a new one.";
        var entry = await db.EmailCodes
            .Where(c => c.CustomerAccountId == account.Id && c.Purpose == purpose && c.UsedUtc == null)
            .OrderByDescending(c => c.CreatedDate)
            .FirstOrDefaultAsync();
        if (entry is null) return invalid;
        if (entry.ExpiresUtc < DateTime.UtcNow) return "That code has expired. Request a new one.";
        if (entry.Attempts >= MaxCodeAttempts) return "Too many wrong attempts. Request a new code.";

        var digits = new string((code ?? "").Where(char.IsDigit).ToArray());
        var expected = Encoding.ASCII.GetBytes(entry.CodeHash);
        var actual = Encoding.ASCII.GetBytes(HashCode(account.Id, purpose, digits));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            entry.Attempts++;
            await db.SaveChangesAsync();
            return entry.Attempts >= MaxCodeAttempts ? "Too many wrong attempts. Request a new code." : invalid;
        }

        entry.UsedUtc = DateTime.UtcNow;
        return null;
    }

    private string CreateSession(LicenseDbContext db, CustomerAccount account, string? appVersion)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        db.CustomerSessions.Add(new CustomerSession
        {
            CustomerAccount = account,
            TokenHash = HashToken(token),
            ExpiresUtc = DateTime.UtcNow.Add(SessionLifetime),
            CreatedFromIp = _context.ClientAddress,
            AppVersion = appVersion?.Length > 50 ? appVersion[..50] : appVersion,
        });
        return token;
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string HashCode(long accountId, string purpose, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"InfoIDEmailCode|{accountId}|{purpose}|{code}")));

    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var trimmed = email.Trim().ToLowerInvariant();
        try
        {
            return new MailAddress(trimmed).Address == trimmed && trimmed.Contains('.') ? trimmed : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? PasswordProblem(string? password) =>
        password is null || password.Length < 8 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit)
            ? "Use a password of at least 8 characters with letters and numbers."
            : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
