namespace InfoID.LicenseManager.Application.Interfaces.Repositories;

public interface IBlacklistRepository
{
    /// <summary>True when the machine fingerprint or the activation code is on the blacklist (non-cancelled entries).</summary>
    Task<bool> IsBlacklistedAsync(string fingerprintHex, string normalizedActivationCode, CancellationToken cancellationToken = default);
}
