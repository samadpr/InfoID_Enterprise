using System.Linq.Expressions;
using InfoID.Domain.Common;

namespace InfoID.Domain.Common.Interfaces;

/// <summary>
/// Generic repository contract. Lives in Domain (not Infrastructure) so that
/// Application depends only on this interface -- never on EF Core directly.
/// InfoID.Infrastructure provides the EF Core implementation; the composition
/// root (InfoID.App's startup) wires the two together via DI.
///
/// All reads exclude soft-deleted rows (Cancelled == true) by default -- see
/// IncludeCancelled overloads where you genuinely need to see cancelled rows
/// (e.g. an "Undo" screen or an audit report).
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<T?> SingleOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);

    /// <summary>Queues an insert. Call IUnitOfWork.SaveChangesAsync to persist.</summary>
    Task AddAsync(T entity, CancellationToken ct = default);

    /// <summary>Marks an entity as modified. Call IUnitOfWork.SaveChangesAsync to persist.</summary>
    void Update(T entity);

    /// <summary>
    /// Soft-delete: sets Cancelled = true rather than removing the row, per
    /// InfoID's audit/history convention. Call IUnitOfWork.SaveChangesAsync
    /// to persist.
    /// </summary>
    void SoftDelete(T entity);

    /// <summary>
    /// Hard delete -- bypasses the soft-delete convention. Only use this for
    /// truly disposable rows (e.g. an ImportErrorLog cleanup job), never for
    /// primary business entities.
    /// </summary>
    void HardDelete(T entity);
}
