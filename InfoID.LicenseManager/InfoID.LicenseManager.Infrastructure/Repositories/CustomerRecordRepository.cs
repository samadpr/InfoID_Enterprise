using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Infrastructure.Repositories;

internal sealed class CustomerRecordRepository : ICustomerRecordRepository
{
    private readonly LicenseManagerDbContext _db;

    public CustomerRecordRepository(LicenseManagerDbContext db) => _db = db;

    public Task<CustomerRecord?> FindMatchingAsync(string organizationName, string? normalizedEmail, CancellationToken cancellationToken = default) =>
        normalizedEmail is not null
            ? _db.CustomerRecords.FirstOrDefaultAsync(c => !c.Cancelled && c.ContactEmail == normalizedEmail, cancellationToken)
            : _db.CustomerRecords.FirstOrDefaultAsync(c => !c.Cancelled && c.OrganizationName == organizationName, cancellationToken);

    public void Add(CustomerRecord customer) => _db.CustomerRecords.Add(customer);
}
