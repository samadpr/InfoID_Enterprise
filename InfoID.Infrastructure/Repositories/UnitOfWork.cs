using System.Collections.Concurrent;
using InfoID.Domain.Common;
using InfoID.Domain.Common.Interfaces;

namespace InfoID.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly InfoIdDbContext _context;
    private readonly ConcurrentDictionary<Type, object> _repositories = new();

    public UnitOfWork(InfoIdDbContext context)
    {
        _context = context;
    }

    public IRepository<T> Repository<T>() where T : BaseEntity
    {
        return (IRepository<T>)_repositories.GetOrAdd(
            typeof(T),
            _ => new Repository<T>(_context));
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        _context.SaveChangesAsync(ct);
}
