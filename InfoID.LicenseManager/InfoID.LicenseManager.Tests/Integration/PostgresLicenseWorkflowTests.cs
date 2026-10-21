using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.DTOs.Activations;
using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Application.Services;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure.Persistence;
using InfoID.LicenseManager.Infrastructure.Repositories;
using InfoID.LicenseManager.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfoID.LicenseManager.Tests.Integration;

/// <summary>License workflows against a real PostgreSQL: row locking, atomic activation limits, SQL search and
/// the multi-step actions. Each "request" builds its own DbContext and services, like a web request does.
/// Skipped, with a reason, when Docker is not running.</summary>
[Collection(PostgresCollection.Name)]
public class PostgresLicenseWorkflowTests
{
    private static readonly TestClock Clock = new(new DateTimeOffset(DateTime.UtcNow));

    private readonly PostgresFixture _postgres;

    public PostgresLicenseWorkflowTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>One request's worth of services over its own context, acting as a License Manager.</summary>
    private sealed class Request
    {
        public Request(string connectionString)
        {
            var user = new FakeCurrentUser { IsAuthenticated = true, AdminUserId = null, Email = "manager@infoid.test", Role = AdminRoles.LicenseManager };
            Db = PostgresFixture.NewContext(connectionString, user);
            var unitOfWork = new UnitOfWork(Db);
            var options = new DbContextOptionsBuilder<LicenseManagerDbContext>().UseNpgsql(connectionString).Options;
            var audit = new AuditService(new AuditLogRepository(options, user, Db), user, Clock);
            var licenses = new LicenseRepository(Db);
            Licenses = new LicenseService(licenses, new LicenseCodeFactory(licenses, new CustomerRecordRepository(Db), Clock), new ActivationRepository(Db), unitOfWork, audit, user, Clock, NullLogger<LicenseService>.Instance);
            Activation = new ActivationService(licenses, new BlacklistRepository(Db), unitOfWork, new TestSecretProvider(), audit, user, Clock, NullLogger<ActivationService>.Instance);
        }

        public LicenseManagerDbContext Db { get; }
        public LicenseService Licenses { get; }
        public ActivationService Activation { get; }
    }

    private static CreateLicenseRequestDto NewLicense(int quantity = 1, int machines = 1, string? organization = null) => new(
        quantity, LicenseTier.Professional, LicenseType.Subscription, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), null,
        CardLimitPeriod.Monthly, machines, false, organization, null, null, null, null, null);

    private static string RequestCode(string displayCode, string machine)
    {
        ActivationCode.TryParse(displayCode, out var code);
        return new SoftCode(code!, TestData.Fingerprint(machine), DateOnly.FromDateTime(DateTime.UtcNow)).Encode();
    }

    [SkippableFact]
    public async Task Parallel_requests_for_the_last_machine_slot_let_exactly_one_through()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("slot_race_single");
        var code = (await new Request(cs).Licenses.CreateAsync(NewLicense(machines: 1))).Value!.Codes[0];

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() =>
            new Request(cs).Activation.IssueSerialKeyAsync(new IssueSerialKeyRequestDto(RequestCode(code, "PC-" + i), null, null)))));

        Assert.Equal(1, results.Count(r => r.Succeeded));
        await using var check = PostgresFixture.NewContext(cs);
        Assert.Equal(1, await check.ActivationRecords.CountAsync(a => a.Status == ActivationStatus.Active));
    }

    [SkippableFact]
    public async Task Parallel_requests_never_exceed_the_machine_limit_of_a_multi_machine_code()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("slot_race_multi");
        var code = (await new Request(cs).Licenses.CreateAsync(NewLicense(machines: 5))).Value!.Codes[0];

        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(i => Task.Run(() =>
            new Request(cs).Activation.IssueSerialKeyAsync(new IssueSerialKeyRequestDto(RequestCode(code, "PC-" + i), null, null)))));

        Assert.Equal(5, results.Count(r => r.Succeeded));
        await using var check = PostgresFixture.NewContext(cs);
        Assert.Equal(5, await check.ActivationRecords.CountAsync(a => a.Status == ActivationStatus.Active));
    }

    [SkippableFact]
    public async Task Duplicate_requests_from_the_same_machine_leave_one_active_record()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("same_machine_race");
        var code = (await new Request(cs).Licenses.CreateAsync(NewLicense(machines: 1))).Value!.Codes[0];
        var request = RequestCode(code, "PC-A");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
            new Request(cs).Activation.IssueSerialKeyAsync(new IssueSerialKeyRequestDto(request, null, null)))));

        Assert.All(results, r => Assert.True(r.Succeeded, string.Join(" ", r.Errors)));
        Assert.Single(results.Select(r => r.Value!.SerialKey).Distinct());
        await using var check = PostgresFixture.NewContext(cs);
        Assert.Equal(1, await check.ActivationRecords.CountAsync(a => a.Status == ActivationStatus.Active));
        Assert.Equal(10, await check.ActivationRecords.CountAsync());
    }

    [SkippableFact]
    public async Task A_revoke_racing_with_activations_leaves_no_active_machine_behind()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("revoke_race");
        var created = (await new Request(cs).Licenses.CreateAsync(NewLicense(machines: 20))).Value!;

        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
            (await new Request(cs).Activation.IssueSerialKeyAsync(new IssueSerialKeyRequestDto(RequestCode(created.Codes[0], "PC-" + i), null, null))).Succeeded)).ToList();
        tasks.Add(Task.Run(async () =>
            (await new Request(cs).Licenses.RevokeAsync(new RevokeLicenseRequestDto(created.FirstLicenseId, "test"))).Succeeded));
        await Task.WhenAll(tasks);

        await using var check = PostgresFixture.NewContext(cs);
        var license = await check.LicenseCodes.Include(c => c.Activations).SingleAsync();
        Assert.Equal(LicenseCodeStatus.Revoked, license.Status);
        Assert.DoesNotContain(license.Activations, a => a.Status == ActivationStatus.Active);
    }

    [SkippableFact]
    public async Task A_batch_is_stored_with_unique_codes_one_customer_and_an_audit_entry()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("batch_create");

        var created = (await new Request(cs).Licenses.CreateAsync(NewLicense(quantity: 300, organization: "Acme AS"))).Value!;

        await using var check = PostgresFixture.NewContext(cs);
        Assert.Equal(300, await check.LicenseCodes.CountAsync());
        Assert.Equal(300, await check.LicenseCodes.Select(c => c.ActivationCode).Distinct().CountAsync());
        Assert.Equal(1, await check.CustomerRecords.CountAsync());
        Assert.Equal(created.BatchId, await check.LicenseCodes.Select(c => c.BatchId).Distinct().SingleAsync());
        Assert.Equal("LicenseCode.Created", (await check.AdminAuditLogs.SingleAsync()).Action);
    }

    [SkippableFact]
    public async Task Search_finds_by_code_customer_and_machine_and_treats_wildcards_literally()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("search_sql");
        var request = new Request(cs);
        var plain = (await request.Licenses.CreateAsync(NewLicense(organization: "Acme AS"))).Value!;
        await request.Licenses.CreateAsync(NewLicense(organization: "100% Pure_Co"));
        await request.Activation.IssueSerialKeyAsync(new IssueSerialKeyRequestDto(RequestCode(plain.Codes[0], "FINDME-PC"), null, null));
        var machineHex = TestData.Fingerprint("FINDME-PC").Hex;

        Assert.Single(await new Request(cs).Licenses.SearchAsync(new LicenseSearchDto(plain.Codes[0])));
        Assert.Single(await new Request(cs).Licenses.SearchAsync(new LicenseSearchDto("acme")));
        Assert.Single(await new Request(cs).Licenses.SearchAsync(new LicenseSearchDto(machineHex[..10])));
        Assert.Single(await new Request(cs).Licenses.SearchAsync(new LicenseSearchDto("100% Pure_Co")));
        Assert.Empty(await new Request(cs).Licenses.SearchAsync(new LicenseSearchDto("%%%")));      // would match everything as a wildcard
        Assert.Empty(await new Request(cs).Licenses.SearchAsync(new LicenseSearchDto("Pure_Xo")));  // '_' is not "any character"
        Assert.Single(await new Request(cs).Licenses.SearchAsync(new LicenseSearchDto(Status: "Active")));
    }

    [SkippableFact]
    public async Task Freeing_slots_stores_transfer_history_and_reopens_the_code()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("free_slots_sql");
        var created = (await new Request(cs).Licenses.CreateAsync(NewLicense(machines: 2))).Value!;
        await new Request(cs).Activation.IssueSerialKeyAsync(new IssueSerialKeyRequestDto(RequestCode(created.Codes[0], "PC-A"), null, null));
        await new Request(cs).Activation.IssueSerialKeyAsync(new IssueSerialKeyRequestDto(RequestCode(created.Codes[0], "PC-B"), null, null));

        var freed = await new Request(cs).Licenses.FreeMachineSlotsAsync(new FreeMachineSlotsRequestDto(created.FirstLicenseId, "Moved"));

        Assert.Equal(2, freed.Value);
        await using var check = PostgresFixture.NewContext(cs);
        Assert.Equal(2, await check.TransferHistories.CountAsync(t => t.Reason == "Moved" && t.OldActivationRecordId != null));
        Assert.Equal(LicenseCodeStatus.Available, (await check.LicenseCodes.SingleAsync()).Status);
    }
}
