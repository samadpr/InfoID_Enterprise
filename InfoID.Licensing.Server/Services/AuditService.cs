using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Licensing.Server.Services;

/// <summary>Append-only, hash-chained admin audit log (FR-ADM-3). Each row's hash
/// covers its content plus the previous row's hash, so editing or deleting a past row
/// breaks the chain and <see cref="VerifyChainAsync"/> reports where.</summary>
public sealed class AuditService
{
    private readonly IDbContextFactory<LicenseDbContext> _dbFactory;
    private readonly AdminContext _admin;
    private static readonly SemaphoreSlim ChainLock = new(1, 1);

    public AuditService(IDbContextFactory<LicenseDbContext> dbFactory, AdminContext admin)
    {
        _dbFactory = dbFactory;
        _admin = admin;
    }

    public async Task LogAsync(string action, object? details = null, long? adminUserIdOverride = null)
    {
        await ChainLock.WaitAsync();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.CurrentUser = _admin.Email;

            // The License API and License Manager can write at the same moment from
            // different processes; a PostgreSQL advisory lock keeps the hash chain
            // strictly sequential across all of them (the SemaphoreSlim only covers
            // one process).
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734000101)");

            var previousHash = await db.AdminAuditLogs
                .OrderByDescending(a => a.Id)
                .Select(a => a.RecordHash)
                .FirstOrDefaultAsync();

            var entry = new AdminAuditLog
            {
                AdminUserId = adminUserIdOverride ?? _admin.CurrentAdmin?.Id,
                Action = action,
                IPAddress = _admin.ClientAddress,
                // PostgreSQL stores microseconds; truncate so the hash recomputed from
                // the stored row matches the one computed here.
                ActionDate = TruncateToMicroseconds(DateTime.UtcNow),
                DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
                PreviousHash = previousHash,
            };
            entry.RecordHash = ComputeHash(entry);

            db.AdminAuditLogs.Add(entry);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            ChainLock.Release();
        }
    }

    /// <summary>Returns null if the whole chain is intact, else the first broken row id.</summary>
    public async Task<long?> VerifyChainAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        string? previous = null;
        await foreach (var row in db.AdminAuditLogs.OrderBy(a => a.Id).AsNoTracking().AsAsyncEnumerable())
        {
            if (row.PreviousHash != previous || row.RecordHash != ComputeHash(row))
                return row.Id;
            previous = row.RecordHash;
        }
        return null;
    }

    public async Task<List<AdminAuditLog>> GetRecentAsync(int take = 200)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.AdminAuditLogs.AsNoTracking()
            .Include(a => a.AdminUser)
            .OrderByDescending(a => a.Id)
            .Take(take)
            .ToListAsync();
    }

    private static DateTime TruncateToMicroseconds(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static string ComputeHash(AdminAuditLog e)
    {
        var content = string.Join('|',
            e.AdminUserId?.ToString() ?? "",
            e.Action,
            e.IPAddress ?? "",
            e.ActionDate.ToUniversalTime().ToString("O"),
            e.DetailsJson ?? "",
            e.PreviousHash ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}
