using InfoID.Application.Dtos.Licensing;
using InfoID.Application.Interfaces;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Common.Interfaces;
using LicenseEntity = InfoID.Domain.Entities.LicensingModule.LicenseRecord;

namespace InfoID.Application.Services;

/// <summary>
/// Local-only license logic for now -- no License Server exists yet (that's
/// a later phase, see InfoID.Shared's Contracts/Licensing DTOs for the
/// planned HTTPS shape). This service reads/writes only the local
/// LicenseRecord row, which is enough to build and test every other module
/// without waiting on the Admin Portal/License Server to exist.
///
/// Per FR-LIC-17: licensing gates issuance/printing/encoding ONLY. Nothing
/// in this service should ever be called to gate login or navigation.
/// </summary>
public class LicenseService : ILicenseService
{
    private readonly IUnitOfWork _uow;

    public LicenseService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<LicenseStatusDto> GetCurrentStatusAsync(CancellationToken ct = default)
    {
        var record = await GetLicenseRecordAsync(ct);
        if (record is null)
        {
            return new LicenseStatusDto { HasLicense = false };
        }

        var (inGrace, daysRemaining) = ComputeGracePeriod(record);

        return new LicenseStatusDto
        {
            HasLicense = true,
            Tier = record.LicenseTier,
            Type = record.LicenseType,
            Status = record.Status,
            ExpiryDate = record.ExpiryDate,
            MaxCardsPerPeriod = record.MaxCardsPerPeriod,
            CardsIssuedThisPeriod = record.CardsIssuedThisPeriod,
            IsInGracePeriod = inGrace,
            GracePeriodDaysRemaining = daysRemaining,
        };
    }

    public async Task<FeatureCheckResult> CheckFeatureAllowedAsync(string featureName, CancellationToken ct = default)
    {
        var record = await GetLicenseRecordAsync(ct);
        if (record is null)
        {
            return FeatureCheckResult.Denied("No license is activated on this machine.");
        }

        if (record.Status == LicenseStatus.Deactivated)
        {
            return FeatureCheckResult.Denied("This license has been deactivated.");
        }

        var (inGrace, _) = ComputeGracePeriod(record);
        var isExpired = record.ExpiryDate.HasValue && record.ExpiryDate.Value < DateTime.UtcNow;

        if (isExpired && !inGrace)
        {
            return FeatureCheckResult.Denied("Your license has expired. Renew to continue using licensed features.");
        }

        if (record.MaxCardsPerPeriod.HasValue && record.CardsIssuedThisPeriod >= record.MaxCardsPerPeriod.Value)
        {
            return FeatureCheckResult.Denied($"Card issuance limit ({record.MaxCardsPerPeriod} per period) reached for this license.");
        }

        return FeatureCheckResult.Allowed();
    }

    public async Task IncrementCardsIssuedAsync(CancellationToken ct = default)
    {
        var record = await GetLicenseRecordAsync(ct);
        if (record is null) return;

        record.CardsIssuedThisPeriod += 1;
        _uow.Repository<LicenseEntity>().Update(record);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<LicenseEntity?> GetLicenseRecordAsync(CancellationToken ct)
    {
        var all = await _uow.Repository<LicenseEntity>().GetAllAsync(ct);
        return all.OrderByDescending(l => l.CreatedDate).FirstOrDefault();
    }

    /// <summary>FR-LIC-12: a fixed grace window after expiry before features
    /// actually stop working, so a lapsed renewal doesn't halt operations
    /// mid-day.</summary>
    private static (bool inGrace, int? daysRemaining) ComputeGracePeriod(LicenseEntity record)
    {
        if (!record.ExpiryDate.HasValue) return (false, null);

        var graceEnd = record.ExpiryDate.Value.AddDays(record.GracePeriodDays);
        var now = DateTime.UtcNow;

        if (now <= record.ExpiryDate.Value) return (false, null); // not even expired yet
        if (now > graceEnd) return (false, 0); // grace period over

        var remaining = (int)Math.Ceiling((graceEnd - now).TotalDays);
        return (true, remaining);
    }
}
