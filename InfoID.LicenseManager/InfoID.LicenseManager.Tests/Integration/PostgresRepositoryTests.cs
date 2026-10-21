using InfoID.LicenseManager.Application.Services;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure.Persistence;
using InfoID.LicenseManager.Infrastructure.Repositories;
using InfoID.LicenseManager.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using LegacyAdminContext = InfoID.Licensing.Server.Services.AdminContext;
using LegacyAuditService = InfoID.Licensing.Server.Services.AuditService;
using LegacyContext = InfoID.Licensing.Server.Data.LicenseDbContext;

namespace InfoID.LicenseManager.Tests.Integration;

/// <summary>Repository behaviour against a real PostgreSQL (advisory locks, atomic first-run, SQL
/// translation of the dashboard queries, and audit-chain compatibility with the legacy server).
/// Skipped, with a reason, when Docker is not running.</summary>
[Collection(PostgresCollection.Name)]
public class PostgresRepositoryTests
{
    private readonly PostgresFixture _postgres;

    public PostgresRepositoryTests(PostgresFixture postgres) => _postgres = postgres;

    private static AuditService NewAuditService(string connectionString, TimeProvider? clock = null)
    {
        var user = new FakeCurrentUser();
        var db = PostgresFixture.NewContext(connectionString, user);
        var repository = new AuditLogRepository(
            new DbContextOptionsBuilder<LicenseManagerDbContext>().UseNpgsql(connectionString).Options, user, db);
        return new AuditService(repository, user, clock ?? TimeProvider.System);
    }

    [SkippableFact]
    public async Task Concurrent_audit_writers_still_produce_one_unbroken_chain()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("audit_concurrency");

        await Task.WhenAll(Enumerable.Range(0, 25).Select(i => Task.Run(() =>
            NewAuditService(cs).LogAsync("Concurrent.Write", new { i }))));

        await using var db = PostgresFixture.NewContext(cs);
        Assert.Equal(25, await db.AdminAuditLogs.CountAsync());
        Assert.Null(await NewAuditService(cs).VerifyChainAsync());
    }

    [SkippableFact]
    public async Task A_chain_written_by_the_legacy_server_verifies_and_can_be_extended_in_both_directions()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("audit_legacy_compat");
        var legacyFactory = new LegacyFactory(cs);
        var legacy = new LegacyAuditService(legacyFactory, new LegacyAdminContext { ClientAddress = "legacy-pc" });

        await legacy.LogAsync("Legacy.First", new { A = 1 });
        await legacy.LogAsync("Legacy.Second");

        var current = NewAuditService(cs);
        Assert.Null(await current.VerifyChainAsync());

        await current.LogAsync("Web.Third", new { B = 2 });

        Assert.Null(await current.VerifyChainAsync());
        Assert.Null(await legacy.VerifyChainAsync());
    }

    [SkippableFact]
    public async Task First_run_setup_is_atomic_when_many_requests_race()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("first_admin_race");

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            await using var db = PostgresFixture.NewContext(cs);
            var repository = new AdminUserRepository(db);
            return await repository.TryAddFirstAdminAsync(new AdminUser
            {
                FullName = "Admin " + i,
                Email = $"admin{i}@infoid.test",
                PasswordHash = "x",
                Role = AdminRoles.SuperAdmin,
            });
        })));

        Assert.Equal(1, results.Count(r => r));
        await using var check = PostgresFixture.NewContext(cs);
        Assert.Equal(1, await check.AdminUsers.CountAsync());
    }

    [SkippableFact]
    public async Task Dashboard_queries_count_only_live_rows_in_the_right_windows()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("dashboard_counts");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var seed = PostgresFixture.NewContext(cs))
        {
            LicenseCode Code(string code, string status, DateOnly? expiry, bool cancelled = false) => new()
            {
                ActivationCode = code, Status = status, ExpiryDate = expiry, Cancelled = cancelled, LicenseType = expiry is null ? "Perpetual" : "Subscription",
            };

            var expiring = Code("AAAA", LicenseCodeStatus.Active, today.AddDays(10));
            seed.LicenseCodes.AddRange(
                expiring,
                Code("BBBB", LicenseCodeStatus.Available, today.AddDays(45)),
                Code("CCCC", LicenseCodeStatus.Available, today.AddDays(80)),
                Code("DDDD", LicenseCodeStatus.Revoked, today.AddDays(5)),            // revoked: never "expiring"
                Code("EEEE", LicenseCodeStatus.Available, today.AddDays(-1)),         // already expired
                Code("FFFF", LicenseCodeStatus.Available, null),                      // perpetual
                Code("GGGG", LicenseCodeStatus.Available, today.AddDays(5), cancelled: true));
            await seed.SaveChangesAsync();

            seed.ActivationRecords.AddRange(
                new ActivationRecord { LicenseCodeId = expiring.Id, MachineFingerprint = "F1", Status = ActivationStatus.Active, ActivationDate = DateTime.UtcNow.AddDays(-2) },
                new ActivationRecord { LicenseCodeId = expiring.Id, MachineFingerprint = "F2", Status = ActivationStatus.Deactivated, ActivationDate = DateTime.UtcNow.AddDays(-40) });
            await seed.SaveChangesAsync();
        }

        await using var db = PostgresFixture.NewContext(cs);
        var licenses = new LicenseRepository(db);
        var activations = new ActivationRepository(db);

        var counts = await licenses.CountByStatusAsync();
        Assert.Equal(6, counts.Total);       // the cancelled one is excluded
        Assert.Equal(4, counts.Available);
        Assert.Equal(1, counts.Active);
        Assert.Equal(1, counts.Revoked);
        Assert.Equal(1, await licenses.CountExpiringAsync(today, 30));
        Assert.Equal(2, await licenses.CountExpiringAsync(today, 60));
        Assert.Equal(3, await licenses.CountExpiringAsync(today, 90));
        Assert.Equal(1, await activations.CountActiveAsync());
        Assert.Equal(1, await activations.CountActivatedSinceAsync(DateTime.UtcNow.AddDays(-30)));
    }

    private sealed class LegacyFactory : IDbContextFactory<LegacyContext>
    {
        private readonly string _connectionString;

        public LegacyFactory(string connectionString) => _connectionString = connectionString;

        public LegacyContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<LegacyContext>().UseNpgsql(_connectionString).Options);
    }
}
