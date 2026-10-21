using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Infrastructure.Repositories;

internal sealed class AdminUserRepository : IAdminUserRepository
{
    /// <summary>PostgreSQL advisory-lock key serialising first-run setup across all processes.</summary>
    private const long FirstAdminLockKey = 734000102;

    private readonly LicenseManagerDbContext _db;

    public AdminUserRepository(LicenseManagerDbContext db) => _db = db;

    public Task<bool> AnyActiveAsync(CancellationToken cancellationToken = default) =>
        _db.AdminUsers.AnyAsync(u => !u.Cancelled, cancellationToken);

    public Task<AdminUser?> FindActiveByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        _db.AdminUsers.FirstOrDefaultAsync(u => u.Email == normalizedEmail && !u.Cancelled, cancellationToken);

    public Task<AdminUser?> FindActiveByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _db.AdminUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && !u.Cancelled, cancellationToken);

    public async Task<bool> TryAddFirstAdminAsync(AdminUser admin, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({FirstAdminLockKey})", cancellationToken);

        if (await _db.AdminUsers.AnyAsync(u => !u.Cancelled, cancellationToken))
            return false;

        _db.AdminUsers.Add(admin);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
