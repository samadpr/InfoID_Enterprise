using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Licensing.Server.Services;

public sealed record DashboardSummary(
    int TotalCodes,
    int AvailableCodes,
    int ActiveCodes,
    int RevokedCodes,
    int ActiveMachines,
    int ExpiringIn30Days,
    int ExpiringIn60Days,
    int ExpiringIn90Days,
    int ActivationsLast30Days);

/// <summary>License Dashboard numbers (FR-ADM-5, first slice).</summary>
public sealed class DashboardService
{
    private readonly IDbContextFactory<LicenseDbContext> _dbFactory;

    public DashboardService(IDbContextFactory<LicenseDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<DashboardSummary> GetSummaryAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var codes = db.LicenseCodes.AsNoTracking().Where(c => !c.Cancelled);

        return new DashboardSummary(
            await codes.CountAsync(),
            await codes.CountAsync(c => c.Status == LicenseCodeStatus.Available),
            await codes.CountAsync(c => c.Status == LicenseCodeStatus.Active),
            await codes.CountAsync(c => c.Status == LicenseCodeStatus.Revoked),
            await db.ActivationRecords.CountAsync(a => a.Status == ActivationStatus.Active && !a.Cancelled),
            await codes.CountAsync(c => c.Status != LicenseCodeStatus.Revoked && c.ExpiryDate >= today && c.ExpiryDate <= today.AddDays(30)),
            await codes.CountAsync(c => c.Status != LicenseCodeStatus.Revoked && c.ExpiryDate >= today && c.ExpiryDate <= today.AddDays(60)),
            await codes.CountAsync(c => c.Status != LicenseCodeStatus.Revoked && c.ExpiryDate >= today && c.ExpiryDate <= today.AddDays(90)),
            await db.ActivationRecords.CountAsync(a => a.ActivationDate >= DateTime.UtcNow.AddDays(-30)));
    }
}
