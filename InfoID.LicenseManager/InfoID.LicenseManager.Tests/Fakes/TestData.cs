using InfoID.Licensing.Core;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Tests.Fakes;

public static class TestData
{
    public static readonly DateOnly Today = new(2026, 10, 19);
    public static readonly DateTime NowUtc = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>One valid activation code (checksum included) for tests that need to know the exact text.</summary>
    public static readonly string KnownCode = ActivationCode.Generate().Value;

    public static string KnownCodeDisplay => InfoID.LicenseManager.Application.Common.ActivationCodeFormat.Display(KnownCode);

    /// <summary>A valid, never-activated one-year Standard subscription for one machine.</summary>
    public static LicenseCode NewLicense(
        LicenseTier tier = LicenseTier.Standard,
        LicenseType type = LicenseType.Subscription,
        DateOnly? expiry = null,
        int? maxCards = null,
        int maxActivations = 1,
        bool allowOffline = false,
        string? code = null)
    {
        var activationCode = code ?? ActivationCode.Generate().Value;
        return LicenseCode.Create(
            activationCode, tier, type,
            type == LicenseType.Perpetual ? null : expiry ?? Today.AddYears(1),
            maxCards, CardLimitPeriod.Monthly, maxActivations, allowOffline, ProductCodes.InfoID, Today);
    }

    public static ActivationRecord ActiveMachine(string fingerprintHex, long id = 0) => new()
    {
        Id = id,
        MachineFingerprint = fingerprintHex,
        Status = ActivationStatus.Active,
        ActivationDate = NowUtc.AddDays(-5),
        IssuedTier = "Standard",
    };

    /// <summary>A fingerprint like InfoID would compute for a machine of that name.</summary>
    public static MachineFingerprint Fingerprint(string machine) =>
        MachineFingerprint.FromComponents(new Dictionary<string, string?> { ["SimulatedMachine"] = machine });
}
