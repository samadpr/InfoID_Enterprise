using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Unit.Application;

public class LicenseServiceTests
{
    private static readonly DateOnly Today = TestData.Today;

    private static CreateLicenseRequestDto Request(
        int quantity = 1, LicenseType type = LicenseType.Subscription, DateOnly? expiry = null, int? cards = null,
        int machines = 1, string? organization = null, string? email = null, bool offline = false) => new(
        quantity, LicenseTier.Professional, type, type == LicenseType.Perpetual ? null : expiry ?? Today.AddYears(1), cards,
        CardLimitPeriod.Monthly, machines, offline, organization, null, "Norway", email, null, null);

    // ---------------------------------------------------------------- authorization ----

    [Theory]
    [InlineData(AdminRoles.SupportAgent)]
    [InlineData("Intruder")]
    public async Task Staff_without_license_rights_cannot_change_anything(string role)
    {
        var h = new ServiceHarness(role);
        var code = h.Licenses.Add(TestData.NewLicense());

        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.CreateAsync(Request()));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.RevokeAsync(new RevokeLicenseRequestDto(code.Id, "x")));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.ExtendAsync(new ExtendLicenseRequestDto(code.Id, Today.AddYears(2))));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.FreeMachineSlotsAsync(new FreeMachineSlotsRequestDto(code.Id, "x")));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.SetOfflineActivationAsync(new SetOfflineActivationRequestDto(code.Id, true)));

        Assert.Single(h.Licenses.Codes);
        Assert.Equal(LicenseCodeStatus.Available, code.Status);
        Assert.Empty(h.Audit.Rows);
        Assert.Equal(0, h.UnitOfWork.Saves);
    }

    [Theory]
    [InlineData(AdminRoles.SuperAdmin)]
    [InlineData(AdminRoles.LicenseManager)]
    public async Task License_managers_and_super_admins_may_create(string role)
    {
        var h = new ServiceHarness(role);

        var result = await h.Service.CreateAsync(Request());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Every_role_may_look_at_codes()
    {
        var h = new ServiceHarness(AdminRoles.SupportAgent);
        var code = h.Licenses.Add(TestData.NewLicense());

        Assert.Single(await h.Service.SearchAsync(new LicenseSearchDto()));
        Assert.Equal(code.Id, (await h.Service.GetDetailsAsync(code.Id)).Id);
    }

    // ---------------------------------------------------------------- create ----

    [Fact]
    public async Task A_single_code_is_created_available_with_server_set_fields_and_audited()
    {
        var h = new ServiceHarness();

        var result = await h.Service.CreateAsync(Request(organization: "Acme AS", email: " Sales@Acme.TEST ", cards: 1000, machines: 2, offline: true));

        Assert.True(result.Succeeded);
        var created = result.Value!;
        Assert.Null(created.BatchId);
        var code = Assert.Single(h.Licenses.Codes);
        Assert.Equal(code.Id, created.FirstLicenseId);
        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", Assert.Single(created.Codes));
        Assert.Equal(LicenseCodeStatus.Available, code.Status);
        Assert.Equal(ProductCodes.InfoID, code.ProductCode);
        Assert.Null(code.CustomerAccountId);
        Assert.Equal(7, code.CreatedByAdminUserId);
        Assert.Equal(2, code.MaxActivations);
        Assert.True(code.AllowOfflineActivation);
        Assert.Equal(1000, code.MaxCardsPerPeriod);
        Assert.Equal(1, h.UnitOfWork.Saves);
        Assert.Equal(["LicenseCode.Created"], h.AuditedActions);
    }

    [Fact]
    public async Task Created_codes_are_valid_activation_codes_in_normalised_storage_form()
    {
        var h = new ServiceHarness();

        var result = await h.Service.CreateAsync(Request(quantity: 5));

        foreach (var display in result.Value!.Codes)
        {
            Assert.True(ActivationCode.TryParse(display, out var parsed), display + " does not parse");
            Assert.Contains(h.Licenses.Codes, c => c.ActivationCode == parsed!.Value);
        }
    }

    [Fact]
    public async Task A_batch_shares_one_batch_id_and_every_code_is_unique()
    {
        var h = new ServiceHarness();

        var result = await h.Service.CreateAsync(Request(quantity: 50));

        var created = result.Value!;
        Assert.NotNull(created.BatchId);
        Assert.Equal(50, created.Codes.Count);
        Assert.Equal(50, created.Codes.Distinct().Count());
        Assert.All(h.Licenses.Codes, c => Assert.Equal(created.BatchId, c.BatchId));
    }

    [Fact]
    public async Task The_largest_allowed_batch_is_created_in_one_save()
    {
        var h = new ServiceHarness();

        var result = await h.Service.CreateAsync(Request(quantity: 1000));

        Assert.Equal(1000, result.Value!.Codes.Count);
        Assert.Equal(1, h.UnitOfWork.Saves);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    [InlineData(-5)]
    public async Task Batch_size_is_limited(int quantity)
    {
        var h = new ServiceHarness();

        var result = await h.Service.CreateAsync(Request(quantity: quantity));

        Assert.False(result.Succeeded);
        Assert.Contains("Quantity must be between 1 and 1000.", result.Errors);
        Assert.Empty(h.Licenses.Codes);
        Assert.Equal(0, h.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Invalid_terms_are_reported_together_and_nothing_is_stored()
    {
        var h = new ServiceHarness();

        var result = await h.Service.CreateAsync(Request(quantity: 0, machines: 500, expiry: Today.AddDays(-1), cards: 150));

        Assert.False(result.Succeeded);
        Assert.True(result.Errors.Count >= 3, string.Join(" | ", result.Errors));
        Assert.Empty(h.Licenses.Codes);
        Assert.Empty(h.Audit.Rows);
    }

    [Fact]
    public async Task An_expiry_date_in_the_past_is_refused()
    {
        var h = new ServiceHarness();

        var result = await h.Service.CreateAsync(Request(expiry: Today));

        Assert.Contains("The expiry date must be in the future.", result.Errors);
    }

    [Fact]
    public async Task Overlong_text_and_bad_contact_email_are_refused_before_the_database_would_truncate_or_fail()
    {
        var h = new ServiceHarness();

        var result = await h.Service.CreateAsync(Request(organization: new string('x', 201), email: "not-an-email"));

        Assert.Contains("Organization name must be at most 200 characters.", result.Errors);
        Assert.Contains("Enter a valid contact email address, or leave it empty.", result.Errors);
    }

    [Fact]
    public async Task Customers_are_found_by_email_or_name_and_created_otherwise()
    {
        var h = new ServiceHarness();

        await h.Service.CreateAsync(Request(organization: "Acme AS", email: "sales@acme.test"));
        await h.Service.CreateAsync(Request(organization: "Acme Norway AS", email: "SALES@acme.test"));   // same e-mail: same customer
        await h.Service.CreateAsync(Request(organization: "Other Ltd", email: "info@other.test"));
        await h.Service.CreateAsync(Request(organization: "Name Only Inc"));
        await h.Service.CreateAsync(Request(organization: "Name Only Inc"));                               // same name, no e-mail: same customer
        await h.Service.CreateAsync(Request());                                                           // no customer at all

        Assert.Equal(3, h.Customers.Customers.Count);
        Assert.Equal(new[] { "sales@acme.test", "info@other.test", null }, h.Customers.Customers.Select(c => c.ContactEmail));
        Assert.Equal(1, h.Licenses.Codes.Count(c => c.CustomerRecord is null));
    }

    [Fact]
    public async Task A_code_that_clashes_with_an_existing_one_is_never_reused()
    {
        var h = new ServiceHarness();
        var existing = h.Licenses.Add(TestData.NewLicense());

        var result = await h.Service.CreateAsync(Request(quantity: 200));

        Assert.Equal(200, result.Value!.Codes.Count);
        Assert.Equal(201, h.Licenses.Codes.Select(c => c.ActivationCode).Distinct().Count());
        Assert.Contains(existing, h.Licenses.Codes);
    }

    [Fact]
    public async Task The_creation_audit_entry_summarises_the_batch_without_listing_codes()
    {
        var h = new ServiceHarness();

        var result = await h.Service.CreateAsync(Request(quantity: 3, organization: "Acme AS"));

        var entry = Assert.Single(h.Audit.Rows);
        Assert.Contains("\"Count\":3", entry.DetailsJson);
        Assert.Contains("Acme AS", entry.DetailsJson);
        Assert.All(result.Value!.Codes, display => Assert.DoesNotContain(display.Replace("-", ""), entry.DetailsJson));
        Assert.Equal(7, entry.AdminUserId);
    }

    // ---------------------------------------------------------------- search / details ----

    [Fact]
    public async Task Search_normalises_the_typed_code_and_clamps_the_page_size()
    {
        var h = new ServiceHarness();

        await h.Service.SearchAsync(new LicenseSearchDto("  7k3m-qx9d  ", "Active", 99999));

        var criteria = h.Licenses.LastSearch!;
        Assert.Equal("7k3m-qx9d", criteria.Text);
        Assert.Equal("7K3MQX9D", criteria.CodeFragment);
        Assert.Equal("Active", criteria.Status);
        Assert.Equal(1000, criteria.Take);
    }

    [Fact]
    public async Task An_unknown_status_filter_returns_nothing_instead_of_everything()
    {
        var h = new ServiceHarness();
        h.Licenses.Add(TestData.NewLicense());

        Assert.Empty(await h.Service.SearchAsync(new LicenseSearchDto(Status: "Bogus")));
        Assert.Null(h.Licenses.LastSearch);
    }

    [Fact]
    public async Task Search_returns_codes_in_display_form_newest_first()
    {
        var h = new ServiceHarness();
        var older = h.Licenses.Add(TestData.NewLicense());
        var newer = h.Licenses.Add(TestData.NewLicense());

        var rows = await h.Service.SearchAsync(new LicenseSearchDto());

        Assert.Equal([newer.Id, older.Id], rows.Select(r => r.Id));
        Assert.Equal(ActivationCodeFormat.Display(newer.ActivationCode), rows[0].ActivationCode);
    }

    [Fact]
    public async Task Search_can_be_limited_to_one_batch()
    {
        var h = new ServiceHarness();
        var batch = (await h.Service.CreateAsync(Request(quantity: 3))).Value!.BatchId;
        await h.Service.CreateAsync(Request());

        var rows = await h.Service.SearchAsync(new LicenseSearchDto(BatchId: batch));

        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task Details_include_machines_and_the_display_code()
    {
        var h = new ServiceHarness();
        var code = h.Licenses.Add(TestData.NewLicense(code: TestData.KnownCode, maxActivations: 2));
        code.RegisterActivation(TestData.ActiveMachine("AA1"), TestData.NowUtc);

        var details = await h.Service.GetDetailsAsync(code.Id);

        Assert.Equal(TestData.KnownCodeDisplay, details.ActivationCode);
        Assert.Equal(1, details.ActiveMachines);
        Assert.Equal("AA1", Assert.Single(details.Activations).MachineFingerprint);
    }

    [Fact]
    public async Task Details_of_an_unknown_or_cancelled_code_are_not_found()
    {
        var h = new ServiceHarness();
        var cancelled = h.Licenses.Add(TestData.NewLicense());
        cancelled.Cancelled = true;

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.GetDetailsAsync(9999));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.GetDetailsAsync(cancelled.Id));
    }

    // ---------------------------------------------------------------- revoke ----

    [Fact]
    public async Task Revoking_ends_the_license_in_one_locked_transaction_and_is_audited_with_the_reason()
    {
        var h = new ServiceHarness();
        var code = h.Licenses.Add(TestData.NewLicense());
        code.RegisterActivation(TestData.ActiveMachine("AA"), TestData.NowUtc);

        var result = await h.Service.RevokeAsync(new RevokeLicenseRequestDto(code.Id, "  Chargeback  "));

        Assert.True(result.Succeeded);
        Assert.Equal(LicenseCodeStatus.Revoked, code.Status);
        Assert.Empty(code.ActiveActivations);
        Assert.Equal([code.Id], h.Licenses.LockedIds);
        Assert.Equal(1, h.UnitOfWork.Commits);
        var entry = Assert.Single(h.Audit.Rows);
        Assert.Equal("LicenseCode.Revoked", entry.Action);
        Assert.Contains("Chargeback", entry.DetailsJson);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Revoking_needs_a_reason(string reason)
    {
        var h = new ServiceHarness();
        var code = h.Licenses.Add(TestData.NewLicense());

        var result = await h.Service.RevokeAsync(new RevokeLicenseRequestDto(code.Id, reason));

        Assert.False(result.Succeeded);
        Assert.Equal(LicenseCodeStatus.Available, code.Status);
        Assert.Empty(h.Audit.Rows);
    }

    [Fact]
    public async Task Revoking_twice_is_refused_and_keeps_the_original_revoke_date()
    {
        var h = new ServiceHarness();
        var code = h.Licenses.Add(TestData.NewLicense());
        await h.Service.RevokeAsync(new RevokeLicenseRequestDto(code.Id, "first"));
        var firstDate = code.RevokedDate;
        h.Clock.Advance(TimeSpan.FromDays(3));

        var again = await h.Service.RevokeAsync(new RevokeLicenseRequestDto(code.Id, "second"));

        Assert.False(again.Succeeded);
        Assert.Equal("This license is already revoked.", again.Errors[0]);
        Assert.Equal(firstDate, code.RevokedDate);
        Assert.Single(h.Audit.Rows);
    }

    [Fact]
    public async Task Revoking_an_unknown_code_is_not_found_and_rolls_back()
    {
        var h = new ServiceHarness();

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.RevokeAsync(new RevokeLicenseRequestDto(12345, "x")));

        Assert.Equal(1, h.UnitOfWork.Rollbacks);
        Assert.Empty(h.Audit.Rows);
    }

    // ---------------------------------------------------------------- extend ----

    [Fact]
    public async Task Extending_changes_the_expiry_and_audits_old_and_new_dates()
    {
        var h = new ServiceHarness();
        var code = h.Licenses.Add(TestData.NewLicense(expiry: Today.AddMonths(1)));

        var result = await h.Service.ExtendAsync(new ExtendLicenseRequestDto(code.Id, Today.AddYears(1)));

        Assert.True(result.Succeeded);
        Assert.Equal(Today.AddYears(1), code.ExpiryDate);
        var entry = Assert.Single(h.Audit.Rows);
        Assert.Equal("LicenseCode.Extended", entry.Action);
        Assert.Contains(Today.AddMonths(1).ToString("yyyy-MM-dd"), entry.DetailsJson);
        Assert.Contains(Today.AddYears(1).ToString("yyyy-MM-dd"), entry.DetailsJson);
    }

    [Fact]
    public async Task Extending_to_a_past_date_or_a_perpetual_license_is_a_failed_result_not_an_exception()
    {
        var h = new ServiceHarness();
        var subscription = h.Licenses.Add(TestData.NewLicense());
        var perpetual = h.Licenses.Add(TestData.NewLicense(type: LicenseType.Perpetual));

        var past = await h.Service.ExtendAsync(new ExtendLicenseRequestDto(subscription.Id, Today.AddDays(-1)));
        var forever = await h.Service.ExtendAsync(new ExtendLicenseRequestDto(perpetual.Id, Today.AddYears(1)));

        Assert.Equal("The new expiry date must be in the future.", past.Errors[0]);
        Assert.Equal("Perpetual licenses have no expiry date.", forever.Errors[0]);
        Assert.Empty(h.Audit.Rows);
        Assert.Equal(Today.AddYears(1), subscription.ExpiryDate);
    }

    // ---------------------------------------------------------------- slots / offline ----

    [Fact]
    public async Task Freeing_slots_records_transfers_makes_the_code_available_and_reports_the_count()
    {
        var h = new ServiceHarness();
        var code = h.Licenses.Add(TestData.NewLicense(maxActivations: 2));
        code.RegisterActivation(TestData.ActiveMachine("AA"), TestData.NowUtc);
        code.RegisterActivation(TestData.ActiveMachine("BB"), TestData.NowUtc);

        var result = await h.Service.FreeMachineSlotsAsync(new FreeMachineSlotsRequestDto(code.Id, "Old PC died"));

        Assert.Equal(2, result.Value);
        Assert.Equal(2, h.Activations.Transfers.Count);
        Assert.All(h.Activations.Transfers, t => Assert.Equal("Old PC died", t.Reason));
        Assert.Equal(LicenseCodeStatus.Available, code.Status);
        Assert.Contains("\"Count\":2", Assert.Single(h.Audit.Rows).DetailsJson);
    }

    [Fact]
    public async Task Freeing_slots_needs_a_reason()
    {
        var h = new ServiceHarness();
        var code = h.Licenses.Add(TestData.NewLicense());
        code.RegisterActivation(TestData.ActiveMachine("AA"), TestData.NowUtc);

        var result = await h.Service.FreeMachineSlotsAsync(new FreeMachineSlotsRequestDto(code.Id, " "));

        Assert.False(result.Succeeded);
        Assert.Single(code.ActiveActivations);
    }

    [Theory]
    [InlineData(true, "LicenseCode.OfflineAllowed")]
    [InlineData(false, "LicenseCode.OfflineDisallowed")]
    public async Task Offline_activation_can_be_switched_and_is_audited(bool allow, string expectedAudit)
    {
        var h = new ServiceHarness();
        var code = h.Licenses.Add(TestData.NewLicense(allowOffline: !allow));

        var result = await h.Service.SetOfflineActivationAsync(new SetOfflineActivationRequestDto(code.Id, allow));

        Assert.True(result.Succeeded);
        Assert.Equal(allow, code.AllowOfflineActivation);
        Assert.Equal([expectedAudit], h.AuditedActions);
    }
}
