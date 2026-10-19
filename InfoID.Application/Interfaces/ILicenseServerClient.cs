using InfoID.Shared.Contracts.Licensing;

namespace InfoID.Application.Interfaces;

/// <summary>HTTPS client for InfoID.LicenseApi. Implemented by the desktop app.
/// Throws <see cref="LicenseServerUnavailableException"/> when the server can't be
/// reached (no internet, server down), so callers can fall back to offline.</summary>
public interface ILicenseServerClient
{
    /// <summary>False when this build has no license server address.</summary>
    bool IsConfigured { get; }

    Task<OnlineActivationResponse> ActivateAsync(OnlineActivationRequest request, CancellationToken ct = default);

    Task<LicenseCheckInResponse> CheckInAsync(LicenseCheckInRequest request, CancellationToken ct = default);

    Task<LicenseDeactivationResponse> DeactivateAsync(LicenseDeactivationRequest request, CancellationToken ct = default);

    /// <summary>The phone page URL encoded in the offline-activation QR code.</summary>
    string? GetPhoneActivationUrl(string requestCode);
}

public sealed class LicenseServerUnavailableException : Exception
{
    public LicenseServerUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
