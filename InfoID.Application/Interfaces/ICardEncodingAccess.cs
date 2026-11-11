using InfoID.Application.Dtos.Licensing;

namespace InfoID.Application.Interfaces;

/// <summary>The one place card-encoding licensing is decided (NFC now; printer-integrated
/// encoding, vCard, protected tags later). Reading and previewing are never gated.</summary>
public interface ICardEncodingAccess
{
    bool CanReadNfc { get; }
    Task<FeatureCheckResult> CheckNfcWriteAsync(CancellationToken ct = default);
}
