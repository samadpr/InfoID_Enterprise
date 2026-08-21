using InfoID.Application.Dtos.Licensing;

namespace InfoID.Application.Interfaces;

public interface ILicenseService
{
    Task<LicenseStatusDto> GetCurrentStatusAsync(CancellationToken ct = default);

    /// <summary>Call before allowing issuance, printing, or RFID encoding
    /// (FR-LIC-17 -- the ONLY three gated actions). Never call this to gate
    /// login, navigation, or read-only screens.</summary>
    Task<FeatureCheckResult> CheckFeatureAllowedAsync(string featureName, CancellationToken ct = default);

    /// <summary>Call once a card issuance actually completes, so
    /// CardsIssuedThisPeriod stays accurate for the next check.</summary>
    Task IncrementCardsIssuedAsync(CancellationToken ct = default);
}
