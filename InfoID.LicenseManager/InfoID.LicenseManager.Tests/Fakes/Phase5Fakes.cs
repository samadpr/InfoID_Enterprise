using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.DTOs.Trial;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Security;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Tests.Fakes;

/// <summary>Signs with a real ECDSA key (generated once) so tests can verify tokens exactly as InfoID.Desktop does.</summary>
public sealed class FakeLicenseTokenSigner : ILicenseTokenSigner
{
    private static readonly (string PrivateKeyPem, string PublicKeyPem) KeyPair = SignedLicense.CreateKeyPair();

    public static string PublicKeyPem => KeyPair.PublicKeyPem;

    public bool Configured { get; set; } = true;

    public bool IsConfigured => Configured;

    public string Sign(OnlineLicense license) => SignedLicense.Sign(license, KeyPair.PrivateKeyPem);
}

/// <summary>Trial storage with the same atomic rules as the SQL: the limit check and the hold are one step under a lock.</summary>
public sealed class InMemoryTrialRepository : ITrialRepository
{
    private readonly object _gate = new();
    private long _nextUsageId = 1;

    public List<TrialUsage> Usages { get; } = new();
    public List<TrialReservation> Reservations { get; } = new();

    public Task<long> EnsureUsageAsync(long accountId, string productCode, int defaultLimit, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var usage = Usages.FirstOrDefault(u => u.CustomerAccountId == accountId && u.ProductCode == productCode);
            if (usage is null)
            {
                usage = new TrialUsage { Id = _nextUsageId++, CustomerAccountId = accountId, ProductCode = productCode, CardLimit = defaultLimit };
                Usages.Add(usage);
            }

            return Task.FromResult(usage.Id);
        }
    }

    public Task<int> ExpireStaleAsync(long usageId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var stale = Reservations.Where(r => r.TrialUsageId == usageId && r.Status == TrialReservationStatus.Reserved && r.ExpiresUtc < utcNow).ToList();
            foreach (var r in stale) Close(r, TrialReservationStatus.Expired, utcNow);
            return Task.FromResult(stale.Count);
        }
    }

    public Task<TrialStatusDto> ReadStatusAsync(long usageId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var u = Usages.First(x => x.Id == usageId);
            return Task.FromResult(new TrialStatusDto(u.CardLimit, u.CardsUsed, u.CardsReserved));
        }
    }

    public Task<bool> TryReserveAsync(long usageId, long accountId, int cards, Guid reservationKey, DateTime expiresUtc, DateTime utcNow,
        string? designName, string? machineFingerprint, string? appVersion, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var u = Usages.First(x => x.Id == usageId);
            if (u.CardsUsed + u.CardsReserved + cards > u.CardLimit) return Task.FromResult(false);

            u.CardsReserved += cards;
            Reservations.Add(new TrialReservation
            {
                Id = Reservations.Count + 1, ReservationKey = reservationKey, TrialUsageId = usageId, CustomerAccountId = accountId, Cards = cards,
                Status = TrialReservationStatus.Reserved, ExpiresUtc = expiresUtc, DesignName = designName, MachineFingerprint = machineFingerprint, AppVersion = appVersion,
            });
            return Task.FromResult(true);
        }
    }

    public Task<long?> CommitAsync(long accountId, Guid reservationKey, int cardsPrinted, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var r = Reservations.FirstOrDefault(x => x.ReservationKey == reservationKey && x.CustomerAccountId == accountId);
            if (r is null) return Task.FromResult<long?>(null);

            var printed = Math.Clamp(cardsPrinted, 0, r.Cards);
            var u = Usages.First(x => x.Id == r.TrialUsageId);
            if (r.Status == TrialReservationStatus.Reserved)
            {
                u.CardsReserved -= r.Cards;
                u.CardsUsed += printed;
            }
            else if (r.Status is TrialReservationStatus.Expired or TrialReservationStatus.Released)
            {
                u.CardsUsed += printed; // credits were already given back, but the cards were printed
            }
            else
            {
                return Task.FromResult<long?>(r.TrialUsageId); // already committed: counts once
            }

            r.Status = TrialReservationStatus.Committed;
            r.CardsPrinted = printed;
            return Task.FromResult<long?>(r.TrialUsageId);
        }
    }

    public Task<long?> ReleaseAsync(long accountId, Guid reservationKey, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var r = Reservations.FirstOrDefault(x => x.ReservationKey == reservationKey && x.CustomerAccountId == accountId);
            if (r is null) return Task.FromResult<long?>(null);
            if (r.Status == TrialReservationStatus.Reserved) Close(r, TrialReservationStatus.Released, utcNow);
            return Task.FromResult<long?>(r.TrialUsageId);
        }
    }

    private void Close(TrialReservation r, string status, DateTime utcNow)
    {
        Usages.First(u => u.Id == r.TrialUsageId).CardsReserved -= r.Cards;
        r.Status = status;
        r.CompletedUtc = utcNow;
    }
}
