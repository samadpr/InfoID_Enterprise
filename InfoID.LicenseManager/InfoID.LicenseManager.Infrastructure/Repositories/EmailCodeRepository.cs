using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Infrastructure.Repositories;

internal sealed class EmailCodeRepository : IEmailCodeRepository
{
    private readonly LicenseManagerDbContext _db;

    public EmailCodeRepository(LicenseManagerDbContext db) => _db = db;

    public async Task<IReadOnlyList<EmailCode>> GetUnusedAsync(long accountId, string purpose, CancellationToken cancellationToken = default) =>
        await _db.EmailCodes
            .Where(c => c.CustomerAccountId == accountId && c.Purpose == purpose && c.UsedUtc == null)
            .ToListAsync(cancellationToken);

    public void Add(EmailCode code) => _db.EmailCodes.Add(code);

    public Task<EmailCode?> FindLatestUnusedAsync(long accountId, string purpose, CancellationToken cancellationToken = default) =>
        _db.EmailCodes
            .Where(c => c.CustomerAccountId == accountId && c.Purpose == purpose && c.UsedUtc == null)
            .OrderByDescending(c => c.CreatedDate)
            .FirstOrDefaultAsync(cancellationToken);
}
