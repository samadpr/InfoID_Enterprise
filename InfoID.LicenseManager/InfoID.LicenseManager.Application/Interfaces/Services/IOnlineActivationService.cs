using InfoID.LicenseManager.Application.DTOs.Activations;
using InfoID.Shared.Common;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>
/// The online side of activation (FR-LIC-5, FR-LIC-10, FR-LIC-13), called by InfoID on the customer's machine with
/// the activation code and the machine fingerprint. Anonymous by design: possession of a valid code is the credential,
/// so every call is rate limited and audited. Uses the same rules as staff-issued keys.
/// </summary>
public interface IOnlineActivationService
{
    /// <summary>Activation code + fingerprint in, ECDSA-signed license token out. Re-activating the same machine never uses a new slot.</summary>
    Task<OnlineLicenseResultDto> ActivateAsync(string activationCode, string fingerprintHex, string? appVersion, CancellationToken cancellationToken = default);

    /// <summary>Periodic re-validation. Returns a fresh token with the CURRENT terms (a renewal reaches the PC), or a
    /// Revoked / Deactivated / Unknown status so the PC stops printing.</summary>
    Task<OnlineLicenseResultDto> CheckInAsync(string activationCode, string fingerprintHex, int? cardsIssued, string? appVersion, CancellationToken cancellationToken = default);

    /// <summary>The customer moves to another PC: frees the machine's slot.</summary>
    Task<Result> DeactivateAsync(string activationCode, string fingerprintHex, CancellationToken cancellationToken = default);
}
