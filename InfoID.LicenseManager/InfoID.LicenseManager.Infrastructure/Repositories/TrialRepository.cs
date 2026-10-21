using InfoID.LicenseManager.Application.DTOs.Trial;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Infrastructure.Repositories;

internal sealed class TrialRepository : ITrialRepository
{
    private readonly LicenseManagerDbContext _db;

    public TrialRepository(LicenseManagerDbContext db) => _db = db;

    public async Task<long> EnsureUsageAsync(long accountId, string productCode, int defaultLimit, CancellationToken cancellationToken = default)
    {
        var existing = await _db.TrialUsages.AsNoTracking()
            .Where(u => u.CustomerAccountId == accountId && u.ProductCode == productCode)
            .Select(u => (long?)u.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is { } id) return id;

        try
        {
            var created = new TrialUsage { CustomerAccountId = accountId, ProductCode = productCode, CardLimit = defaultLimit };
            _db.TrialUsages.Add(created);
            await _db.SaveChangesAsync(cancellationToken);
            return created.Id;
        }
        catch (DbUpdateException)
        {
            // Two first requests at once: the other one created it (unique index on account + product).
            _db.ChangeTracker.Clear();
            return await _db.TrialUsages.AsNoTracking()
                .Where(u => u.CustomerAccountId == accountId && u.ProductCode == productCode)
                .Select(u => u.Id).FirstAsync(cancellationToken);
        }
    }

    public async Task<int> ExpireStaleAsync(long usageId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var stale = await _db.TrialReservations.AsNoTracking()
            .Where(r => r.TrialUsageId == usageId && r.Status == TrialReservationStatus.Reserved && r.ExpiresUtc < utcNow)
            .Select(r => new { r.Id, r.Cards })
            .ToListAsync(cancellationToken);

        var expired = 0;
        foreach (var reservation in stale)
            if (await ReleaseOneAsync(reservation.Id, usageId, reservation.Cards, TrialReservationStatus.Expired, utcNow, cancellationToken)) expired++;
        return expired;
    }

    public async Task<TrialStatusDto> ReadStatusAsync(long usageId, CancellationToken cancellationToken = default)
    {
        var u = await _db.TrialUsages.AsNoTracking().FirstAsync(x => x.Id == usageId, cancellationToken);
        return new TrialStatusDto(u.CardLimit, u.CardsUsed, u.CardsReserved);
    }

    public async Task<bool> TryReserveAsync(long usageId, long accountId, int cards, Guid reservationKey, DateTime expiresUtc, DateTime utcNow,
        string? designName, string? machineFingerprint, string? appVersion, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // The whole limit check is this one statement; PostgreSQL applies it atomically per row.
        var reserved = await _db.TrialUsages
            .Where(u => u.Id == usageId && u.CardsUsed + u.CardsReserved + cards <= u.CardLimit)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.CardsReserved, u => u.CardsReserved + cards)
                .SetProperty(u => u.ModifiedDate, utcNow), cancellationToken);
        if (reserved == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        _db.TrialReservations.Add(new TrialReservation
        {
            ReservationKey = reservationKey,
            TrialUsageId = usageId,
            CustomerAccountId = accountId,
            Cards = cards,
            Status = TrialReservationStatus.Reserved,
            ExpiresUtc = expiresUtc,
            DesignName = designName,
            MachineFingerprint = machineFingerprint,
            AppVersion = appVersion,
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<long?> CommitAsync(long accountId, Guid reservationKey, int cardsPrinted, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var reservation = await _db.TrialReservations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReservationKey == reservationKey && r.CustomerAccountId == accountId, cancellationToken);
        if (reservation is null) return null;

        var printed = Math.Clamp(cardsPrinted, 0, reservation.Cards);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Only the first Reserved -> Committed transition counts.
        var fromReserved = await _db.TrialReservations
            .Where(r => r.Id == reservation.Id && r.Status == TrialReservationStatus.Reserved)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, TrialReservationStatus.Committed)
                .SetProperty(r => r.CardsPrinted, printed)
                .SetProperty(r => r.CompletedUtc, utcNow)
                .SetProperty(r => r.ModifiedDate, utcNow), cancellationToken);

        if (fromReserved == 1)
        {
            await _db.TrialUsages.Where(u => u.Id == reservation.TrialUsageId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.CardsReserved, u => u.CardsReserved - reservation.Cards)
                    .SetProperty(u => u.CardsUsed, u => u.CardsUsed + printed)
                    .SetProperty(u => u.FirstPrintDate, u => u.FirstPrintDate ?? utcNow)
                    .SetProperty(u => u.LastPrintDate, utcNow)
                    .SetProperty(u => u.ModifiedDate, utcNow), cancellationToken);
        }
        else
        {
            // Expired (or released) before the app reported: the credits were already given back, but the cards were printed -- count them anyway.
            var fromExpired = await _db.TrialReservations
                .Where(r => r.Id == reservation.Id && (r.Status == TrialReservationStatus.Expired || r.Status == TrialReservationStatus.Released))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, TrialReservationStatus.Committed)
                    .SetProperty(r => r.CardsPrinted, printed)
                    .SetProperty(r => r.CompletedUtc, utcNow)
                    .SetProperty(r => r.ModifiedDate, utcNow), cancellationToken);
            if (fromExpired == 1)
            {
                await _db.TrialUsages.Where(u => u.Id == reservation.TrialUsageId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.CardsUsed, u => u.CardsUsed + printed)
                        .SetProperty(u => u.FirstPrintDate, u => u.FirstPrintDate ?? utcNow)
                        .SetProperty(u => u.LastPrintDate, utcNow)
                        .SetProperty(u => u.ModifiedDate, utcNow), cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return reservation.TrialUsageId;
    }

    public async Task<long?> ReleaseAsync(long accountId, Guid reservationKey, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var reservation = await _db.TrialReservations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReservationKey == reservationKey && r.CustomerAccountId == accountId, cancellationToken);
        if (reservation is null) return null;

        await ReleaseOneAsync(reservation.Id, reservation.TrialUsageId, reservation.Cards, TrialReservationStatus.Released, utcNow, cancellationToken);
        return reservation.TrialUsageId;
    }

    private async Task<bool> ReleaseOneAsync(long reservationId, long usageId, int cards, string newStatus, DateTime utcNow, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await _db.TrialReservations
            .Where(r => r.Id == reservationId && r.Status == TrialReservationStatus.Reserved)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, newStatus)
                .SetProperty(r => r.CompletedUtc, utcNow)
                .SetProperty(r => r.ModifiedDate, utcNow), cancellationToken);
        if (changed == 1)
        {
            await _db.TrialUsages.Where(u => u.Id == usageId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.CardsReserved, u => u.CardsReserved - cards)
                    .SetProperty(u => u.ModifiedDate, utcNow), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }
}
