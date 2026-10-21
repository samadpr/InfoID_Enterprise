using InfoID.LicenseManager.Application.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Infrastructure.Persistence;

/// <summary>The request-scoped DbContext acts as the unit of work shared by all repositories.</summary>
internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly LicenseManagerDbContext _db;

    public UnitOfWork(LicenseManagerDbContext db) => _db = db;

    public void UseActor(string actor) => _db.CurrentUser = actor;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        // Already inside a transaction (a service calling another): join it instead of nesting.
        if (_db.Database.CurrentTransaction is not null)
            return await work(cancellationToken);

        // Disposing the transaction without committing (an exception escaped) rolls everything back and releases row locks.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var result = await work(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
