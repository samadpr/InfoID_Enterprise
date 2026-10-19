using InfoID.Application.Dtos.Licensing;
using InfoID.Shared.Common;


namespace InfoID.Application.Interfaces;

public interface ILicenseService
{
    /// <summary>Current license for the License Status page and banners. Always
    /// re-verifies the stored serial key against this machine.</summary>
    Task<LicenseStatusDto> GetCurrentStatusAsync(CancellationToken ct = default);

    /// <summary>Call before issuance, printing or RFID encoding (see LicensedFeatures).
    /// <paramref name="cardCount"/> = how many cards the operation will produce, so a
    /// batch that would exceed the limit is refused up front. Never call this to gate
    /// opening the app, navigation or read-only screens (FR-SEC-7).</summary>
    Task<FeatureCheckResult> CheckFeatureAllowedAsync(string featureName, int cardCount = 0, CancellationToken ct = default);

    /// <summary>Call once cards are actually produced, so the period counter stays accurate.</summary>
    Task IncrementCardsIssuedAsync(int count = 1, CancellationToken ct = default);

    /// <summary>Online activation (FR-LIC-5): sends the activation code and this
    /// machine's fingerprint to the License API and stores the signed license.
    /// Fails with a clear message (and IsServerUnreachable) when there's no internet,
    /// so the UI can offer offline activation.</summary>
    Task<OnlineActivationResult> ActivateOnlineAsync(string activationCode, CancellationToken ct = default);

    /// <summary>Re-validation (FR-LIC-13) for online licenses: picks up renewals and
    /// revocations. Does nothing for offline licenses, or when the last check was
    /// recent (unless <paramref name="force"/>). Never throws for network problems.</summary>
    Task<Result<string>> CheckInAsync(bool force = false, CancellationToken ct = default);

    /// <summary>Offline activation step 1 (FR-LIC-3/4/6): checks the activation code's
    /// format and builds the request code for this machine.</summary>
    Task<Result<RequestCodeDto>> CreateRequestCodeAsync(string activationCode, CancellationToken ct = default);

    /// <summary>Offline activation step 2 (FR-LIC-6/7/8): verifies the serial key for this
    /// activation code on this machine and stores the license.</summary>
    Task<Result<LicenseStatusDto>> ActivateOfflineAsync(string activationCode, string serialKey, ActivationDetailsDto? details = null, CancellationToken ct = default);

    /// <summary>Deactivate / transfer (FR-LIC-10). Online licenses free their slot on
    /// the server (needs internet); offline licenses return what the vendor needs.</summary>
    Task<Result<DeactivationInfoDto>> DeactivateAsync(CancellationToken ct = default);
}
