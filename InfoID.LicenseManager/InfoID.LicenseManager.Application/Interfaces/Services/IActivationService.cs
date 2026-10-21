using InfoID.LicenseManager.Application.DTOs.Activations;
using InfoID.Shared.Common;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>Staff-issued offline activation: request (soft) code in, 20-character serial key out (FR-ADM-6).
/// Online activation, check-in and the phone page join this service in a later phase so every
/// path applies the same rules.</summary>
public interface IActivationService
{
    /// <summary>Decodes a request code and reports what it refers to and whether a key can be issued.</summary>
    Task<Result<SoftCodePreviewDto>> PreviewAsync(string softCodeText, CancellationToken cancellationToken = default);

    /// <summary>Issues the key. The slot check and the new activation happen under a row lock, so two
    /// simultaneous requests can never both take the last free machine slot.</summary>
    Task<Result<SerialKeyIssuedDto>> IssueSerialKeyAsync(IssueSerialKeyRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>The phone/QR page of InfoID: the customer scans the code shown in the app and reads the key off
    /// the phone. Anonymous, rate limited, and only for licenses staff allowed to activate offline.</summary>
    Task<Result<SerialKeyIssuedDto>> IssueForPhoneAsync(string softCodeText, CancellationToken cancellationToken = default);
}
