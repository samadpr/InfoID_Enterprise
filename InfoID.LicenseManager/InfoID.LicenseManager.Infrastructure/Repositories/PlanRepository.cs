using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Infrastructure.Repositories;

internal sealed class PlanRepository : IPlanRepository
{
    private readonly LicenseManagerDbContext _db;

    public PlanRepository(LicenseManagerDbContext db) => _db = db;

    public async Task<IReadOnlyList<Plan>> GetAllAsync(string productCode, CancellationToken cancellationToken = default) =>
        await _db.Plans.AsNoTracking()
            .Where(p => p.ProductCode == productCode && !p.Cancelled)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

    public Task<Plan?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _db.Plans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public void Add(Plan plan) => _db.Plans.Add(plan);

    public Task<bool> ProductExistsAsync(string productCode, CancellationToken cancellationToken = default) =>
        _db.Products.AnyAsync(p => p.Code == productCode, cancellationToken);

    public void AddProduct(Product product) => _db.Products.Add(product);

    public Task<bool> AnyPlansAsync(string productCode, CancellationToken cancellationToken = default) =>
        _db.Plans.AnyAsync(p => p.ProductCode == productCode, cancellationToken);

    public void AddRange(IEnumerable<Plan> plans) => _db.Plans.AddRange(plans);

    public async Task<IReadOnlyList<Plan>> GetActiveAsync(string productCode, CancellationToken cancellationToken = default) =>
        await _db.Plans.AsNoTracking()
            .Where(p => p.ProductCode == productCode && p.IsActive && !p.Cancelled)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

    public Task<Plan?> FindActiveAsync(long id, CancellationToken cancellationToken = default) =>
        _db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.IsActive && !p.Cancelled, cancellationToken);
}
