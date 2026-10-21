using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.DTOs.Accounts;
using InfoID.LicenseManager.Application.DTOs.Customers;
using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Application.DTOs.Requests;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;
using Microsoft.EntityFrameworkCore;

namespace InfoID.LicenseManager.Tests.Integration;

/// <summary>The client protocol, requests, customers and plans against a real PostgreSQL: atomic limits, locks,
/// transactions and the SQL of every query. Skipped, with a reason, when no database server is available.</summary>
[Collection(PostgresCollection.Name)]
public class PostgresClientWorkflowTests
{
    private readonly PostgresFixture _postgres;

    public PostgresClientWorkflowTests(PostgresFixture postgres) => _postgres = postgres;

    private static string Hex(string machine) => TestData.Fingerprint(machine).Hex;

    private static CreateLicenseRequestDto NewLicense(int machines = 1, bool offline = false) => new(
        1, LicenseTier.Professional, LicenseType.Subscription, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), null,
        CardLimitPeriod.Monthly, machines, offline, "Acme AS", null, null, "sales@acme.test", null, null);

    private static async Task<string> CreateCodeAsync(string cs, int machines = 1, bool offline = false)
    {
        using var staff = new RealServices(cs);
        return (await staff.Licenses.CreateAsync(NewLicense(machines, offline))).Value!.Codes[0];
    }

    // ---------------------------------------------------------------- online activation ----

    [SkippableFact]
    public async Task Parallel_online_activations_for_the_last_slot_let_exactly_one_through()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("online_slot_race");
        var code = await CreateCodeAsync(cs, machines: 1);

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(async () =>
        {
            using var request = new RealServices(cs, "Anonymous");
            return await request.Online.ActivateAsync(code, Hex("PC-" + i), "1.0");
        })));

        Assert.Equal(1, results.Count(r => r.Succeeded));
        await using var check = PostgresFixture.NewContext(cs);
        Assert.Equal(1, await check.ActivationRecords.CountAsync(a => a.Status == ActivationStatus.Active));
    }

    [SkippableFact]
    public async Task Online_activation_check_in_and_deactivation_work_end_to_end_with_real_rows()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("online_flow");
        var code = await CreateCodeAsync(cs, machines: 1);

        using (var request = new RealServices(cs, "Anonymous"))
        {
            var activated = await request.Online.ActivateAsync(code, Hex("PC-A"), "1.0");
            Assert.True(activated.Succeeded, activated.Error);
            var license = SignedLicense.Verify(activated.License, FakeLicenseTokenSigner.PublicKeyPem, out var error);
            Assert.True(license is not null, error);
            Assert.Equal("Acme AS", license!.OrganizationName);
            Assert.True(license.LicenseId > 0); // the real activation id, as the desktop expects
        }

        using (var request = new RealServices(cs, "Anonymous"))
        {
            var checkIn = await request.Online.CheckInAsync(code, Hex("PC-A"), 5, "1.1");
            Assert.Equal("Active", checkIn.Status);
        }

        using (var request = new RealServices(cs, "Anonymous"))
        {
            Assert.True((await request.Online.DeactivateAsync(code, Hex("PC-A"))).Succeeded);
            Assert.Equal("Deactivated", (await request.Online.CheckInAsync(code, Hex("PC-A"), null, null)).Status);
        }

        await using var check = PostgresFixture.NewContext(cs);
        var record = await check.ActivationRecords.SingleAsync();
        Assert.Equal(ActivationStatus.Deactivated, record.Status);
        Assert.Equal(5, record.CardsIssuedReported);
        Assert.Equal("1.1", record.AppVersion);
        Assert.Equal("Deactivated from InfoID", (await check.TransferHistories.SingleAsync()).Reason);
        Assert.Equal(LicenseCodeStatus.Available, (await check.LicenseCodes.SingleAsync()).Status);
        Assert.Equal("LicenseApi", record.ModifiedBy);
    }

    [SkippableFact]
    public async Task The_phone_page_issues_a_key_only_for_offline_enabled_licenses_on_a_real_database()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("phone_flow");
        var offline = await CreateCodeAsync(cs, offline: true);
        var onlineOnly = await CreateCodeAsync(cs, offline: false);
        string RequestFor(string display) => new SoftCode(ParseCode(display), TestData.Fingerprint("PC"), DateOnly.FromDateTime(DateTime.UtcNow)).Encode();

        using var request = new RealServices(cs, "Anonymous");
        var ok = await request.Activation.IssueForPhoneAsync(RequestFor(offline));
        var refused = await request.Activation.IssueForPhoneAsync(RequestFor(onlineOnly));

        Assert.True(ok.Succeeded, string.Join(" ", ok.Errors));
        Assert.False(refused.Succeeded);
    }

    private static ActivationCode ParseCode(string display)
    {
        ActivationCode.TryParse(display, out var code);
        return code!;
    }

    // ---------------------------------------------------------------- trial ----

    [SkippableFact]
    public async Task Many_simultaneous_trial_reservations_can_never_exceed_the_limit_in_the_database()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("trial_race");
        long accountId;
        using (var signup = new RealServices(cs, "Anonymous"))
        {
            await signup.Accounts.SignUpAsync(new SignUpInputDto("Tina Trial", null, "tina@trial.test", null, null, "Passw0rd-ok", true));
            accountId = (await signup.Db.CustomerAccounts.SingleAsync()).Id;
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(async () =>
        {
            using var request = new RealServices(cs, "Anonymous");
            return await request.Trials.ReserveAsync(accountId, 1, null, null, null);
        })));

        Assert.Equal(10, results.Count(r => r.Succeeded));
        await using var check = PostgresFixture.NewContext(cs);
        var usage = await check.TrialUsages.SingleAsync(); // 40 simultaneous first requests still made one row
        Assert.Equal(10, usage.CardLimit);
        Assert.Equal(10, usage.CardsReserved);
        Assert.Equal(0, usage.CardsUsed);
        Assert.Equal(10, await check.TrialReservations.CountAsync());
    }

    [SkippableFact]
    public async Task Trial_commit_release_and_expiry_keep_the_counts_exact()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("trial_flow");
        long accountId;
        using (var signup = new RealServices(cs, "Anonymous"))
        {
            await signup.Accounts.SignUpAsync(new SignUpInputDto("Tina Trial", null, "tina@trial.test", null, null, "Passw0rd-ok", true));
            accountId = (await signup.Db.CustomerAccounts.SingleAsync()).Id;
        }

        using var request = new RealServices(cs, "Anonymous");
        var a = (await request.Trials.ReserveAsync(accountId, 4, "FP", "1.0", "Badge")).ReservationKey!.Value;
        var b = (await request.Trials.ReserveAsync(accountId, 3, null, null, null)).ReservationKey!.Value;
        var committed = await request.Trials.CommitAsync(accountId, a, 99);   // clamped to the 4 reserved
        var again = await request.Trials.CommitAsync(accountId, a, 99);       // idempotent
        var released = await request.Trials.ReleaseAsync(accountId, b);

        Assert.Equal(new(10, 4, 3), committed.Status);
        Assert.Equal(new(10, 4, 3), again.Status);
        Assert.Equal(new(10, 4, 0), released.Status);
        Assert.False((await request.Trials.CommitAsync(accountId + 1, a, 1)).Succeeded); // not this account's job
    }

    // ---------------------------------------------------------------- accounts ----

    [SkippableFact]
    public async Task Sign_up_verify_login_and_session_work_with_real_rows_and_stored_hashes_only()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("accounts_flow");
        var mail = new FakeEmailSender();
        string token;
        using (var signup = new RealServices(cs, "Anonymous", email: mail))
        {
            await signup.Accounts.SignUpAsync(new SignUpInputDto("Cathy Customer", "Customer Inc", "Cathy@Customer.test", "1", "Norway", "Passw0rd-ok", true));
            var body = mail.Sent.Single().Body;
            var code = body.Substring(body.IndexOf("code is: ", StringComparison.Ordinal) + 9, 6);
            token = (await signup.Accounts.VerifyEmailAsync("cathy@customer.test", code, "1.0")).Token!;
        }

        using (var later = new RealServices(cs, "Anonymous"))
        {
            var session = await later.Accounts.GetSessionAsync(token);
            Assert.Equal("cathy@customer.test", session!.Profile.Email);
            Assert.True((await later.Accounts.LogInAsync("cathy@customer.test", "Passw0rd-ok", null)).Succeeded);
            Assert.False((await later.Accounts.LogInAsync("cathy@customer.test", "wrong-password1", null)).Succeeded);
        }

        await using var check = PostgresFixture.NewContext(cs);
        var account = await check.CustomerAccounts.SingleAsync();
        Assert.StartsWith("PBKDF2$", account.PasswordHash);
        Assert.True(account.EmailVerified);
        Assert.DoesNotContain(token, (await check.CustomerSessions.FirstAsync()).TokenHash);
        Assert.Equal(1, await check.EmailCodes.CountAsync(c => c.UsedUtc != null));
    }

    // ---------------------------------------------------------------- requests (F6) ----

    [SkippableFact]
    public async Task Two_staff_approving_the_same_request_at_once_create_exactly_one_license()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("approve_race");
        long requestId;
        using (var seed = new RealServices(cs, "Anonymous"))
        {
            await seed.Plans.EnsureDefaultsAsync();
            var plan = await seed.Db.Plans.FirstAsync();
            var submitted = await seed.Requests.SubmitAsync(new SubmitLicenseRequestDto(plan.Id, 2, "Bea Buyer", "Acme AS", "buyer@acme.test", null, "Norway", null, null, null, null), null);
            requestId = submitted.RequestId!.Value;
        }

        var terms = new ApproveLicenseRequestDto(requestId, LicenseTier.Standard, LicenseType.Subscription, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1),
            null, CardLimitPeriod.Monthly, 2, false, null);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
        {
            using var staff = new RealServices(cs);
            return await staff.Requests.ApproveAsync(terms);
        })));

        Assert.Equal(1, results.Count(r => r.Succeeded));
        await using var check = PostgresFixture.NewContext(cs);
        Assert.Equal(1, await check.LicenseCodes.CountAsync());
        var request = await check.LicenseRequests.SingleAsync();
        Assert.Equal(LicenseRequestStatus.Approved, request.Status);
        Assert.NotNull(request.LicenseCodeId);
        Assert.Equal(request.ProductCode, (await check.LicenseCodes.SingleAsync()).ProductCode);
    }

    [SkippableFact]
    public async Task Approval_links_the_code_to_the_customer_account_and_search_finds_the_request()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("approve_link");
        var mail = new FakeEmailSender();
        long accountId;
        using (var customer = new RealServices(cs, "Anonymous", email: mail))
        {
            await customer.Plans.EnsureDefaultsAsync();
            await customer.Accounts.SignUpAsync(new SignUpInputDto("Cathy Customer", null, "cathy@customer.test", null, null, "Passw0rd-ok", true));
            var body = mail.Sent.Last().Body;
            var token = (await customer.Accounts.VerifyEmailAsync("cathy@customer.test", body.Substring(body.IndexOf("code is: ", StringComparison.Ordinal) + 9, 6), null)).Token;
            var session = await customer.Accounts.GetSessionAsync(token);
            accountId = session!.AccountId;
            var plan = await customer.Db.Plans.FirstAsync();
            await customer.Requests.SubmitAsync(new SubmitLicenseRequestDto(plan.Id, 1, "Cathy Customer", null, "ignored@x.test", null, null, null, null, null, null), session);
        }

        using (var staff = new RealServices(cs, email: mail))
        {
            var open = await staff.Requests.SearchAsync(new LicenseRequestSearchDto(RequestStatusFilter.Open, "cathy"));
            var request = Assert.Single(open);
            var result = await staff.Requests.ApproveAsync(new ApproveLicenseRequestDto(request.Id, LicenseTier.Standard, LicenseType.Subscription,
                DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), null, CardLimitPeriod.Monthly, 1, false, null));
            Assert.True(result.Succeeded, string.Join(" ", result.Errors));
            Assert.True(result.Value!.EmailSent);
        }

        await using var check = PostgresFixture.NewContext(cs);
        var license = await check.LicenseCodes.SingleAsync();
        Assert.Equal(accountId, license.CustomerAccountId);
        Assert.NotNull((await check.CustomerAccounts.SingleAsync()).CustomerRecordId);
        Assert.Contains("Your activation code", mail.Sent.Last().Subject);
        using var customerAgain = new RealServices(cs, "Anonymous");
        Assert.Single(await customerAgain.Accounts.GetLicensesAsync(accountId));
        Assert.Equal("Approved", (await customerAgain.Accounts.GetRequestsAsync(accountId)).Single().Status);
    }

    // ---------------------------------------------------------------- customers (F7) and plans (F8) ----

    [SkippableFact]
    public async Task Customer_filters_disable_enable_and_session_revocation_work_in_sql()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("customers_sql");
        var mail = new FakeEmailSender();
        long leadId;
        using (var customer = new RealServices(cs, "Anonymous", email: mail))
        {
            await customer.Plans.EnsureDefaultsAsync();
            foreach (var email in new[] { "lead@c.test", "unverified@c.test" })
                await customer.Accounts.SignUpAsync(new SignUpInputDto("Name " + email, null, email, null, null, "Passw0rd-ok", true));
            var leadMail = mail.Sent.First(m => m.To == "lead@c.test").Body;
            await customer.Accounts.VerifyEmailAsync("lead@c.test", leadMail.Substring(leadMail.IndexOf("code is: ", StringComparison.Ordinal) + 9, 6), null);
            leadId = (await customer.Db.CustomerAccounts.SingleAsync(a => a.Email == "lead@c.test")).Id;
        }

        using var staff = new RealServices(cs);
        IReadOnlyList<string> Emails(IEnumerable<CustomerListItemDto> rows) => rows.Select(r => r.Email).Order().ToList();

        Assert.Equal(["lead@c.test", "unverified@c.test"], Emails(await staff.CustomerAdmin.SearchAsync(new CustomerSearchDto())));
        Assert.Equal(["lead@c.test"], Emails(await staff.CustomerAdmin.SearchAsync(new CustomerSearchDto(CustomerFilter.Leads))));
        Assert.Equal(["unverified@c.test"], Emails(await staff.CustomerAdmin.SearchAsync(new CustomerSearchDto(CustomerFilter.NotVerified))));
        Assert.Equal(["lead@c.test"], Emails(await staff.CustomerAdmin.SearchAsync(new CustomerSearchDto(Text: "LEAD@"))));
        Assert.Empty(await staff.CustomerAdmin.SearchAsync(new CustomerSearchDto(Text: "%%%")));

        Assert.True((await staff.CustomerAdmin.SetDisabledAsync(new SetCustomerDisabledRequestDto(leadId, true, "Abuse"))).Succeeded);
        Assert.Equal(["lead@c.test"], Emails(await staff.CustomerAdmin.SearchAsync(new CustomerSearchDto(CustomerFilter.Disabled))));
        await using (var check = PostgresFixture.NewContext(cs))
            Assert.All(await check.CustomerSessions.Where(s => s.CustomerAccountId == leadId).ToListAsync(), s => Assert.NotNull(s.RevokedUtc));
        Assert.True((await staff.CustomerAdmin.SetDisabledAsync(new SetCustomerDisabledRequestDto(leadId, false, null))).Succeeded);
    }

    [SkippableFact]
    public async Task Default_plans_are_created_once_even_when_started_twice_and_plan_edits_persist()
    {
        var cs = await _postgres.CreateMigratedDatabaseAsync("plans_sql");
        using (var first = new RealServices(cs, "Anonymous"))
        {
            await first.Plans.EnsureDefaultsAsync();
            await first.Plans.EnsureDefaultsAsync();
        }

        using var staff = new RealServices(cs);
        var plans = await staff.Plans.GetAllAsync();
        Assert.Equal(["Standard", "Professional", "Enterprise"], plans.Select(p => p.Name));

        var edited = await staff.Plans.UpdateAsync(new InfoID.LicenseManager.Application.DTOs.Plans.UpdatePlanRequestDto(plans[0].Id,
            new InfoID.LicenseManager.Application.DTOs.Plans.PlanInputDto("Starter", LicenseTier.Standard, "Tag", "A\nB", null, 3, 500, CardLimitPeriod.Yearly, "From 10", 5, false)));
        Assert.True(edited.Succeeded);

        using var anonymous = new RealServices(cs, "Anonymous");
        var active = await anonymous.Plans.GetActivePlansAsync(ProductCodes.InfoID);
        Assert.Equal(["Professional", "Enterprise"], active.Select(p => p.Name)); // "Starter" is now inactive
        await using var check = PostgresFixture.NewContext(cs);
        var starter = await check.Plans.SingleAsync(p => p.Name == "Starter");
        Assert.Null(starter.DurationMonths);
        Assert.Equal(500, starter.MaxCardsPerPeriod);
        Assert.Equal(1, await check.Products.CountAsync());
    }
}
