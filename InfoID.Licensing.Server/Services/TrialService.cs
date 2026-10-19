using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Entities;
using InfoID.Shared.Logging;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Licensing.Server.Services;

/// <summary>Server settings for trial printing (appsettings "Trial" section).</summary>
public sealed class TrialOptions
{
    /// <summary>Cards a new account may print in trial mode.</summary>
    public int CardLimit { get; set; } = 10;
    /// <summary>How long a reservation holds credits if the app never reports back.</summary>
    public int ReservationMinutes { get; set; } = 30;
    /// <summary>Largest single trial print job (also bounded by the remaining credits).</summary>
    public int MaxCardsPerReservation { get; set; } = 10;
}

public sealed record TrialStatus(int Limit, int Used, int Reserved)
{
    public int Remaining => Math.Max(0, Limit - Used - Reserved);
}

public sealed record TrialReserveResult(bool Succeeded, Guid? ReservationKey, TrialStatus Status, string? Error, DateTime? ExpiresUtc = null);

public sealed record TrialCompleteResult(bool Succeeded, TrialStatus Status, string? Error);

/// <summary>
/// Trial printing for signed-in customer accounts without an activation code: a fixed
/// number of cards per account, enforced here (the app only asks).
///
/// Reserve -> print -> Commit/Release. Reserving is ONE conditional UPDATE
/// ("add N to Reserved only if Used + Reserved + N &lt;= Limit"), which PostgreSQL applies
/// atomically per row, so any number of simultaneous print requests can never exceed
/// the limit. Reservations an app never reports back on expire and give their credits back.
/// </summary>
public sealed class TrialService
{
    private static readonly AppLogger Log = AppLog.For<TrialService>();

    private readonly IDbContextFactory<LicenseDbContext> _dbFactory;
    private readonly TrialOptions _options;

    public TrialService(IDbContextFactory<LicenseDbContext> dbFactory, TrialOptions options)
    {
        _dbFactory = dbFactory;
        _options = options;
    }

    public async Task<TrialStatus> GetStatusAsync(long accountId, string productCode = ProductCodes.InfoID, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var usage = await EnsureUsageAsync(db, accountId, productCode, ct);
        await ExpireStaleAsync(db, usage.Id, ct);
        return await ReadStatusAsync(db, usage.Id, ct);
    }

    public async Task<TrialReserveResult> ReserveAsync(long accountId, int cards, string? machineFingerprint, string? appVersion,
        string? designName, string productCode = ProductCodes.InfoID, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var usage = await EnsureUsageAsync(db, accountId, productCode, ct);
        await ExpireStaleAsync(db, usage.Id, ct);

        if (cards < 1 || cards > Math.Max(1, _options.MaxCardsPerReservation))
        {
            var status = await ReadStatusAsync(db, usage.Id, ct);
            return new TrialReserveResult(false, null, status,
                cards < 1 ? "Nothing to print." : $"A trial print job can have at most {_options.MaxCardsPerReservation} cards.");
        }

        var now = DateTime.UtcNow;
        var key = Guid.NewGuid();
        var expires = now.AddMinutes(Math.Max(1, _options.ReservationMinutes));

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            // The whole limit check is this one statement.
            var reserved = await db.TrialUsages
                .Where(u => u.Id == usage.Id && u.CardsUsed + u.CardsReserved + cards <= u.CardLimit)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.CardsReserved, u => u.CardsReserved + cards)
                    .SetProperty(u => u.ModifiedDate, now), ct);

            if (reserved == 0)
            {
                await transaction.RollbackAsync(ct);
                var status = await ReadStatusAsync(db, usage.Id, ct);
                var error = status.Remaining == 0
                    ? $"You've used all {status.Limit} trial cards. Activate InfoID to keep printing."
                    : $"Only {status.Remaining} trial card{(status.Remaining == 1 ? "" : "s")} left. Print fewer cards, or activate InfoID.";
                return new TrialReserveResult(false, null, status, error);
            }

            db.TrialReservations.Add(new TrialReservation
            {
                ReservationKey = key,
                TrialUsageId = usage.Id,
                CustomerAccountId = accountId,
                Cards = cards,
                Status = TrialReservationStatus.Reserved,
                ExpiresUtc = expires,
                DesignName = Truncate(designName, 200),
                MachineFingerprint = Truncate(machineFingerprint, 100),
                AppVersion = Truncate(appVersion, 50),
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        Log.Info($"Trial reservation {key}: {cards} card(s) for account {accountId}");
        return new TrialReserveResult(true, key, await ReadStatusAsync(db, usage.Id, ct), null, expires);
    }

    /// <summary>The print was sent: count <paramref name="cardsPrinted"/> (at most what was
    /// reserved) as used and free the rest. Idempotent: committing twice counts once. A
    /// reservation that already expired still counts what was actually printed.</summary>
    public async Task<TrialCompleteResult> CommitAsync(long accountId, Guid reservationKey, int cardsPrinted, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var reservation = await db.TrialReservations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReservationKey == reservationKey && r.CustomerAccountId == accountId, ct);
        if (reservation is null)
            return new TrialCompleteResult(false, new TrialStatus(0, 0, 0), "That print job isn't known to the server.");

        var printed = Math.Clamp(cardsPrinted, 0, reservation.Cards);
        var now = DateTime.UtcNow;

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            // Only the first Reserved -> Committed transition counts.
            var fromReserved = await db.TrialReservations
                .Where(r => r.Id == reservation.Id && r.Status == TrialReservationStatus.Reserved)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, TrialReservationStatus.Committed)
                    .SetProperty(r => r.CardsPrinted, printed)
                    .SetProperty(r => r.CompletedUtc, now)
                    .SetProperty(r => r.ModifiedDate, now), ct);

            if (fromReserved == 1)
            {
                await db.TrialUsages.Where(u => u.Id == reservation.TrialUsageId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.CardsReserved, u => u.CardsReserved - reservation.Cards)
                        .SetProperty(u => u.CardsUsed, u => u.CardsUsed + printed)
                        .SetProperty(u => u.FirstPrintDate, u => u.FirstPrintDate ?? now)
                        .SetProperty(u => u.LastPrintDate, now)
                        .SetProperty(u => u.ModifiedDate, now), ct);
            }
            else
            {
                // Expired (or released) before the app reported: the credits were already
                // given back, but the cards were printed -- count them anyway.
                var fromExpired = await db.TrialReservations
                    .Where(r => r.Id == reservation.Id && (r.Status == TrialReservationStatus.Expired || r.Status == TrialReservationStatus.Released))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, TrialReservationStatus.Committed)
                        .SetProperty(r => r.CardsPrinted, printed)
                        .SetProperty(r => r.CompletedUtc, now)
                        .SetProperty(r => r.ModifiedDate, now), ct);
                if (fromExpired == 1)
                {
                    await db.TrialUsages.Where(u => u.Id == reservation.TrialUsageId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(u => u.CardsUsed, u => u.CardsUsed + printed)
                            .SetProperty(u => u.FirstPrintDate, u => u.FirstPrintDate ?? now)
                            .SetProperty(u => u.LastPrintDate, now)
                            .SetProperty(u => u.ModifiedDate, now), ct);
                }
            }
            await transaction.CommitAsync(ct);
        }

        Log.Info($"Trial reservation {reservationKey}: {printed} card(s) printed");
        return new TrialCompleteResult(true, await ReadStatusAsync(db, reservation.TrialUsageId, ct), null);
    }

    /// <summary>The print failed or was cancelled: give the credits back. Idempotent.</summary>
    public async Task<TrialCompleteResult> ReleaseAsync(long accountId, Guid reservationKey, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var reservation = await db.TrialReservations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReservationKey == reservationKey && r.CustomerAccountId == accountId, ct);
        if (reservation is null)
            return new TrialCompleteResult(false, new TrialStatus(0, 0, 0), "That print job isn't known to the server.");

        await ReleaseOneAsync(db, reservation.Id, reservation.TrialUsageId, reservation.Cards, TrialReservationStatus.Released, ct);
        return new TrialCompleteResult(true, await ReadStatusAsync(db, reservation.TrialUsageId, ct), null);
    }

    // ------------------------------------------------------------------ helpers ----

    private async Task<TrialUsage> EnsureUsageAsync(LicenseDbContext db, long accountId, string productCode, CancellationToken ct)
    {
        var usage = await db.TrialUsages.AsNoTracking()
            .FirstOrDefaultAsync(u => u.CustomerAccountId == accountId && u.ProductCode == productCode, ct);
        if (usage is not null) return usage;

        try
        {
            var created = new TrialUsage { CustomerAccountId = accountId, ProductCode = productCode, CardLimit = Math.Max(0, _options.CardLimit) };
            db.TrialUsages.Add(created);
            await db.SaveChangesAsync(ct);
            db.Entry(created).State = EntityState.Detached;
            return created;
        }
        catch (DbUpdateException)
        {
            // Two first requests at once: the other one created it.
            db.ChangeTracker.Clear();
            return await db.TrialUsages.AsNoTracking()
                .FirstAsync(u => u.CustomerAccountId == accountId && u.ProductCode == productCode, ct);
        }
    }

    /// <summary>Gives back credits held by reservations the app never reported on.</summary>
    private async Task ExpireStaleAsync(LicenseDbContext db, long usageId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var stale = await db.TrialReservations.AsNoTracking()
            .Where(r => r.TrialUsageId == usageId && r.Status == TrialReservationStatus.Reserved && r.ExpiresUtc < now)
            .Select(r => new { r.Id, r.Cards })
            .ToListAsync(ct);
        foreach (var r in stale)
        {
            await ReleaseOneAsync(db, r.Id, usageId, r.Cards, TrialReservationStatus.Expired, ct);
            Log.Warn($"Trial reservation {r.Id} expired without a result; {r.Cards} card(s) given back");
        }
    }

    private static async Task ReleaseOneAsync(LicenseDbContext db, long reservationId, long usageId, int cards, string newStatus, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.TrialReservations
            .Where(r => r.Id == reservationId && r.Status == TrialReservationStatus.Reserved)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, newStatus)
                .SetProperty(r => r.CompletedUtc, now)
                .SetProperty(r => r.ModifiedDate, now), ct);
        if (changed == 1)
        {
            await db.TrialUsages.Where(u => u.Id == usageId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.CardsReserved, u => u.CardsReserved - cards)
                    .SetProperty(u => u.ModifiedDate, now), ct);
        }
        await transaction.CommitAsync(ct);
    }

    private static async Task<TrialStatus> ReadStatusAsync(LicenseDbContext db, long usageId, CancellationToken ct)
    {
        var u = await db.TrialUsages.AsNoTracking().FirstAsync(x => x.Id == usageId, ct);
        return new TrialStatus(u.CardLimit, u.CardsUsed, u.CardsReserved);
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length > max ? value[..max] : value.Trim();
}
