using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Infrastructure.Repositories;

internal sealed class BlacklistRepository : IBlacklistRepository
{
    public const string FingerprintEntry = "Fingerprint";
    public const string ActivationCodeEntry = "ActivationCode";

    private readonly LicenseManagerDbContext _db;

    public BlacklistRepository(LicenseManagerDbContext db) => _db = db;

    public Task<bool> IsBlacklistedAsync(string fingerprintHex, string normalizedActivationCode, CancellationToken cancellationToken = default) =>
        _db.BlacklistEntries.AsNoTracking().AnyAsync(b => !b.Cancelled &&
            ((b.EntryType == FingerprintEntry && b.Value == fingerprintHex) ||
             (b.EntryType == ActivationCodeEntry && b.Value == normalizedActivationCode)), cancellationToken);
}
