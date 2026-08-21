using System.Linq.Expressions;
using InfoID.Domain.Common;
using InfoID.Domain.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of IRepository&lt;T&gt;. Every read filters out
/// soft-deleted rows (Cancelled == true) automatically -- callers never need
/// to remember to add that filter themselves.
/// </summary>
public class Repository<T> : IRepository<T> where T : BaseEntity
{
    private readonly InfoIdDbContext _context;
    private readonly DbSet<T> _set;

    public Repository(InfoIdDbContext context)
    {
        _context = context;
        _set = context.Set<T>();
    }

    private IQueryable<T> ActiveQuery() => _set.Where(e => !e.Cancelled);

    public async Task<T?> GetByIdAsync(long id, CancellationToken ct = default) =>
        await ActiveQuery().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) =>
        await ActiveQuery().ToListAsync(ct);

    public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await ActiveQuery().Where(predicate).ToListAsync(ct);

    public async Task<T?> SingleOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await ActiveQuery().SingleOrDefaultAsync(predicate, ct);

    public async Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await ActiveQuery().AnyAsync(predicate, ct);

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default) =>
        predicate is null ? await ActiveQuery().CountAsync(ct) : await ActiveQuery().CountAsync(predicate, ct);

    public async Task AddAsync(T entity, CancellationToken ct = default) =>
        await _set.AddAsync(entity, ct);

    public void Update(T entity) => _set.Update(entity);

    public void SoftDelete(T entity)
    {
        entity.Cancelled = true;
        _set.Update(entity);
    }

    public void HardDelete(T entity) => _set.Remove(entity);
}
