using InfoID.Application.Dtos.Licensing;
using InfoID.Application.Interfaces;
using InfoID.Domain.Common.Enums;

namespace InfoID.Application.Services;

/// <summary>Professional and higher: write. Standard: read only.
/// Trial (activated trial or no license): read and preview, no physical writing.</summary>
public sealed class CardEncodingAccess : ICardEncodingAccess
{
    private const string ReadStillWorks = " Reading tags and previewing the content still work.";
    private readonly ILicenseService _license;

    public CardEncodingAccess(ILicenseService license) => _license = license;

    public bool CanReadNfc => true;

    public async Task<FeatureCheckResult> CheckNfcWriteAsync(CancellationToken ct = default)
    {
        var status = await _license.GetCurrentStatusAsync(ct);
        if (status.HasLicense && status.Type == LicenseType.Trial)
            return FeatureCheckResult.Denied("Writing NFC tags isn't included in the trial. Activate an InfoID Professional license to write tags." + ReadStillWorks);

        var check = await _license.CheckFeatureAllowedAsync(LicensedFeatures.NfcEncoding, 0, ct);
        return check.IsAllowed
            ? check
            : FeatureCheckResult.Denied((check.Reason ?? "Writing NFC tags needs an InfoID Professional license.") + ReadStillWorks);
    }
}
