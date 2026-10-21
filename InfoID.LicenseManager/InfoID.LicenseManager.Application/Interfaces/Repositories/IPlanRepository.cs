using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Interfaces.Repositories;

public interface IPlanRepository
{
    /// <summary>All non-cancelled plans of a product, in display order (untracked).</summary>
    Task<IReadOnlyList<Plan>> GetAllAsync(string productCode, CancellationToken cancellationToken = default);

    /// <summary>Tracked, so changes are saved through the unit of work.</summary>
    Task<Plan?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Active plans offered to customers (untracked).</summary>
    Task<IReadOnlyList<Plan>> GetActiveAsync(string productCode, CancellationToken cancellationToken = default);

    Task<Plan?> FindActiveAsync(long id, CancellationToken cancellationToken = default);

    void Add(Plan plan);

    // First-run defaults (the product row and its three editions).
    Task<bool> ProductExistsAsync(string productCode, CancellationToken cancellationToken = default);

    void AddProduct(Product product);

    Task<bool> AnyPlansAsync(string productCode, CancellationToken cancellationToken = default);

    void AddRange(IEnumerable<Plan> plans);
}
