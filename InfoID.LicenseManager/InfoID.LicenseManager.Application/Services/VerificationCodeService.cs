using System.Security.Cryptography;
using System.Text;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Services;

public sealed class VerificationCodeService : IVerificationCodeService
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);
    private const int MaxCodeAttempts = 5;

    private readonly IEmailCodeRepository _codes;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CustomerMailer _mailer;
    private readonly TimeProvider _time;

    public VerificationCodeService(IEmailCodeRepository codes, IUnitOfWork unitOfWork, CustomerMailer mailer, TimeProvider time)
    {
        _codes = codes;
        _unitOfWork = unitOfWork;
        _mailer = mailer;
        _time = time;
    }

    public async Task<VerificationCodeResult> IssueAsync(CustomerAccount account, string purpose, bool respectResendInterval, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var previous = await _codes.GetUnusedAsync(account.Id, purpose, cancellationToken);
        if (respectResendInterval && previous.Any(c => c.CreatedDate > now - ResendInterval))
            return VerificationCodeResult.Throttled;

        foreach (var old in previous) old.UsedUtc = now;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _codes.Add(new EmailCode
        {
            CustomerAccountId = account.Id,
            Purpose = purpose,
            CodeHash = HashCode(account.Id, purpose, code),
            ExpiresUtc = now.Add(CodeLifetime),
        });
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var (subject, body) = purpose == EmailCodePurpose.ResetPassword
            ? ("Your password reset code",
               $"Hello {account.FullName},\n\nYour password reset code is: {code}\n\nIt expires in 15 minutes. If you didn't ask to reset your password, you can ignore this email.")
            : ("Verify your email",
               $"Hello {account.FullName},\n\nYour verification code is: {code}\n\nEnter it in the app to finish creating your account. It expires in 15 minutes.\nIf you didn't create an account, you can ignore this email.");

        var sent = await _mailer.TrySendAsync(account.Email, subject, body, "Customer.EmailFailed",
            error => new { account.Email, purpose, Error = error }, cancellationToken);
        return sent ? VerificationCodeResult.Sent : VerificationCodeResult.SendFailed;
    }

    public async Task<string?> ConsumeAsync(CustomerAccount account, string purpose, string? code, CancellationToken cancellationToken = default)
    {
        const string invalid = "That code is not valid. Check it, or request a new one.";
        const string tooMany = "Too many wrong attempts. Request a new code.";

        var entry = await _codes.FindLatestUnusedAsync(account.Id, purpose, cancellationToken);
        if (entry is null) return invalid;

        var now = _time.GetUtcNow().UtcDateTime;
        if (entry.ExpiresUtc < now) return "That code has expired. Request a new one.";
        if (entry.Attempts >= MaxCodeAttempts) return tooMany;

        var digits = new string((code ?? "").Where(char.IsDigit).ToArray());
        var expected = Encoding.ASCII.GetBytes(entry.CodeHash);
        var actual = Encoding.ASCII.GetBytes(HashCode(account.Id, purpose, digits));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            entry.Attempts++;
            await _unitOfWork.SaveChangesAsync(cancellationToken); // the attempt counts even though the caller then stops
            return entry.Attempts >= MaxCodeAttempts ? tooMany : invalid;
        }

        entry.UsedUtc = now;
        return null;
    }

    /// <summary>Same format the desktop License Manager and the License API use, so a code issued by one is
    /// accepted by the other while it is valid.</summary>
    public static string HashCode(long accountId, string purpose, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"InfoIDEmailCode|{accountId}|{purpose}|{code}")));
}
