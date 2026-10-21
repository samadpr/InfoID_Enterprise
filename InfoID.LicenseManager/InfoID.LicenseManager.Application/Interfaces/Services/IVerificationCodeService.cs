using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

public enum VerificationCodeResult
{
    Sent,
    /// <summary>A code was sent less than a minute ago.</summary>
    Throttled,
    /// <summary>The code was stored but the e-mail could not be delivered.</summary>
    SendFailed,
}

/// <summary>
/// One-time 6-digit codes sent by e-mail (verification, password reset). Only a hash is stored; a code
/// expires after 15 minutes. Older unused codes of the same purpose stop working when a new one is issued.
/// </summary>
public interface IVerificationCodeService
{
    /// <param name="respectResendInterval">When true a second code within a minute is refused.</param>
    Task<VerificationCodeResult> IssueAsync(CustomerAccount account, string purpose, bool respectResendInterval, CancellationToken cancellationToken = default);

    /// <summary>Checks a typed code (at most 5 attempts, 15 minutes). Returns null when it is right (the code is marked used,
    /// the caller saves), otherwise the reason to show. A wrong attempt is counted and saved immediately.</summary>
    Task<string?> ConsumeAsync(CustomerAccount account, string purpose, string? code, CancellationToken cancellationToken = default);
}
