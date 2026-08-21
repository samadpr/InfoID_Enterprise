namespace InfoID.Domain.Common.Interfaces;

/// <summary>
/// Boundary of a single business transaction. Application services call
/// Repository&lt;T&gt;() to get typed repositories, make changes, then call
/// SaveChangesAsync once -- so multi-entity operations (e.g. "create an
/// Organization and its first AppUser") commit atomically or not at all.
/// </summary>
public interface IUnitOfWork
{
    IRepository<T> Repository<T>() where T : BaseEntity;

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
