using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Customers;
using InfoID.LicenseManager.Application.DTOs.Plans;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Unit.Application;

public class CustomerAdminServiceTests
{
    // ---------------------------------------------------------------- search and details ----

    [Fact]
    public async Task Filters_separate_leads_verified_unverified_and_disabled_accounts()
    {
        var h = new ServiceHarness();
        var lead = h.Accounts.Seed("lead@c.test");
        var customer = h.Accounts.Seed("customer@c.test");
        h.Accounts.Seed("unverified@c.test", verified: false);
        h.Accounts.Seed("off@c.test", disabled: true);
        h.Requests.Seed(h.Plans.Seed(), accountId: customer.Id);

        async Task<IReadOnlyList<string>> Emails(CustomerFilter filter) =>
            (await h.CustomerService.SearchAsync(new CustomerSearchDto(filter))).Select(r => r.Email).Order().ToList();

        Assert.Equal(4, (await Emails(CustomerFilter.All)).Count);
        Assert.Equal([lead.Email], await Emails(CustomerFilter.Leads));
        Assert.Equal(["customer@c.test", "lead@c.test"], await Emails(CustomerFilter.Verified));
        Assert.Equal(["unverified@c.test"], await Emails(CustomerFilter.NotVerified));
        Assert.Equal(["off@c.test"], await Emails(CustomerFilter.Disabled));
    }

    [Fact]
    public async Task Status_text_follows_the_account_state_and_text_search_matches_several_fields()
    {
        var h = new ServiceHarness();
        h.Accounts.Seed("lead@c.test", fullName: "Lena Lead", company: "Alpha Inc");
        h.Accounts.Seed("off@c.test", disabled: true);

        var rows = await h.CustomerService.SearchAsync(new CustomerSearchDto());

        Assert.Equal(CustomerStatus.Lead, rows.Single(r => r.Email == "lead@c.test").Status);
        Assert.Equal(CustomerStatus.Disabled, rows.Single(r => r.Email == "off@c.test").Status);
        Assert.Single(await h.CustomerService.SearchAsync(new CustomerSearchDto(Text: "alpha")));
        Assert.Single(await h.CustomerService.SearchAsync(new CustomerSearchDto(Text: "LENA")));
        Assert.Empty(await h.CustomerService.SearchAsync(new CustomerSearchDto(Text: "nobody")));
    }

    [Fact]
    public async Task Details_list_the_accounts_licenses_and_requests()
    {
        var h = new ServiceHarness(AdminRoles.SupportAgent);
        var account = h.Accounts.Seed("cathy@c.test");
        var license = h.Licenses.Add(TestData.NewLicense(code: TestData.KnownCode));
        license.CustomerAccountId = account.Id;
        var request = h.Requests.Seed(h.Plans.Seed("Pro"), accountId: account.Id);
        request.LicenseCode = license;

        var details = await h.CustomerService.GetDetailsAsync(account.Id);

        Assert.Equal(TestData.KnownCodeDisplay, Assert.Single(details.Licenses).ActivationCode);
        Assert.Equal("Pro", Assert.Single(details.Requests).PlanName);
        Assert.Equal(1, details.Customer.LicenseCount);
        Assert.Equal(1, details.Customer.RequestCount);
        await Assert.ThrowsAsync<NotFoundException>(() => h.CustomerService.GetDetailsAsync(999));
    }

    [Fact]
    public async Task Strangers_cannot_look_at_customers()
    {
        var h = new ServiceHarness("Intruder");

        await Assert.ThrowsAsync<ForbiddenException>(() => h.CustomerService.SearchAsync(new CustomerSearchDto()));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.CustomerService.GetDetailsAsync(1));
    }

    // ---------------------------------------------------------------- disable / enable ----

    [Fact]
    public async Task Disabling_needs_a_reason_signs_the_customer_out_everywhere_and_is_audited()
    {
        var h = new ServiceHarness();
        var account = h.Accounts.Seed("cathy@c.test");
        h.Accounts.Sessions.Add(new CustomerSession { CustomerAccountId = account.Id, TokenHash = "A" });
        h.Accounts.Sessions.Add(new CustomerSession { CustomerAccountId = account.Id, TokenHash = "B" });
        h.Accounts.Sessions.Add(new CustomerSession { CustomerAccountId = 99, TokenHash = "C" });

        var noReason = await h.CustomerService.SetDisabledAsync(new SetCustomerDisabledRequestDto(account.Id, true, "  "));
        var result = await h.CustomerService.SetDisabledAsync(new SetCustomerDisabledRequestDto(account.Id, true, " Abuse "));

        Assert.False(noReason.Succeeded);
        Assert.True(result.Succeeded);
        Assert.True(account.IsDisabled);
        Assert.All(h.Accounts.Sessions.Where(s => s.CustomerAccountId == account.Id), s => Assert.NotNull(s.RevokedUtc));
        Assert.Null(h.Accounts.Sessions.Single(s => s.TokenHash == "C").RevokedUtc);
        var entry = Assert.Single(h.Audit.Rows);
        Assert.Equal("Customer.Disabled", entry.Action);
        Assert.Contains("Abuse", entry.DetailsJson);
    }

    [Fact]
    public async Task Enabling_restores_the_account_and_repeating_either_action_is_refused()
    {
        var h = new ServiceHarness();
        var account = h.Accounts.Seed("cathy@c.test");

        var enableEnabled = await h.CustomerService.SetDisabledAsync(new SetCustomerDisabledRequestDto(account.Id, false, null));
        await h.CustomerService.SetDisabledAsync(new SetCustomerDisabledRequestDto(account.Id, true, "x"));
        var disableAgain = await h.CustomerService.SetDisabledAsync(new SetCustomerDisabledRequestDto(account.Id, true, "x"));
        var enable = await h.CustomerService.SetDisabledAsync(new SetCustomerDisabledRequestDto(account.Id, false, null));

        Assert.Equal("This account is already enabled.", enableEnabled.Errors[0]);
        Assert.Equal("This account is already disabled.", disableAgain.Errors[0]);
        Assert.True(enable.Succeeded);
        Assert.False(account.IsDisabled);
        Assert.Equal(["Customer.Disabled", "Customer.Enabled"], h.AuditedActions);
    }

    [Fact]
    public async Task Support_agents_cannot_disable_or_enable_and_unknown_accounts_are_not_found()
    {
        var h = new ServiceHarness(AdminRoles.SupportAgent);
        var account = h.Accounts.Seed("cathy@c.test");

        await Assert.ThrowsAsync<ForbiddenException>(() => h.CustomerService.SetDisabledAsync(new SetCustomerDisabledRequestDto(account.Id, true, "x")));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.CustomerService.SetDisabledAsync(new SetCustomerDisabledRequestDto(account.Id, false, null)));
        Assert.False(account.IsDisabled);

        var manager = new ServiceHarness();
        await Assert.ThrowsAsync<NotFoundException>(() => manager.CustomerService.SetDisabledAsync(new SetCustomerDisabledRequestDto(404, true, "x")));
    }

    // ---------------------------------------------------------------- resend verification ----

    [Fact]
    public async Task Resending_sends_a_fresh_code_and_support_agents_may_do_it()
    {
        var h = new ServiceHarness(AdminRoles.SupportAgent);
        var account = h.Accounts.Seed("cathy@c.test", verified: false);

        var result = await h.CustomerService.ResendVerificationAsync(account.Id);

        Assert.True(result.Succeeded);
        var mail = Assert.Single(h.Email.Sent);
        Assert.Equal("cathy@c.test", mail.To);
        Assert.Matches("code is: [0-9]{6}", mail.Body);
        Assert.Equal(["Customer.VerificationResent"], h.AuditedActions);
    }

    [Fact]
    public async Task Resending_is_refused_for_verified_or_disabled_accounts_and_throttled_within_a_minute()
    {
        var h = new ServiceHarness();
        var verified = h.Accounts.Seed("v@c.test");
        var disabled = h.Accounts.Seed("d@c.test", verified: false, disabled: true);
        var pending = h.Accounts.Seed("p@c.test", verified: false);

        var v = await h.CustomerService.ResendVerificationAsync(verified.Id);
        var d = await h.CustomerService.ResendVerificationAsync(disabled.Id);
        var first = await h.CustomerService.ResendVerificationAsync(pending.Id);
        var tooSoon = await h.CustomerService.ResendVerificationAsync(pending.Id);
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        var later = await h.CustomerService.ResendVerificationAsync(pending.Id);

        Assert.Equal("This account's e-mail is already verified.", v.Errors[0]);
        Assert.Equal("This account is disabled. Enable it first.", d.Errors[0]);
        Assert.True(first.Succeeded);
        Assert.StartsWith("A code was sent less than a minute ago.", tooSoon.Errors[0]);
        Assert.True(later.Succeeded);
        Assert.Equal(2, h.Email.Sent.Count);
    }

    [Fact]
    public async Task A_mail_server_that_is_down_is_reported_to_staff_and_audited()
    {
        var h = new ServiceHarness();
        var account = h.Accounts.Seed("cathy@c.test", verified: false);
        h.Email.Failure = new InvalidOperationException("smtp down");

        var result = await h.CustomerService.ResendVerificationAsync(account.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("could not be sent", result.Errors[0]);
        Assert.Contains("Customer.EmailFailed", h.AuditedActions);
        Assert.DoesNotContain("Customer.VerificationResent", h.AuditedActions);
    }

    [Fact]
    public async Task Resending_to_an_unknown_account_is_not_found()
    {
        var h = new ServiceHarness();

        await Assert.ThrowsAsync<NotFoundException>(() => h.CustomerService.ResendVerificationAsync(404));
    }
}

public class PlanServiceTests
{
    private static PlanInputDto Input(string name = "Pro", int? months = 12, int machines = 2, int? cards = 500, bool active = true) =>
        new(name, LicenseTier.Professional, "Schools", "A\nB", months, machines, cards, CardLimitPeriod.Monthly, "Contact sales", 2, active);

    [Fact]
    public async Task Managers_create_plans_and_the_change_is_audited()
    {
        var h = new ServiceHarness();

        var result = await h.PlanService.CreateAsync(Input("  Pro  "));

        Assert.True(result.Succeeded);
        var plan = Assert.Single(h.Plans.Plans);
        Assert.Equal("Pro", plan.Name);
        Assert.Equal(ProductCodes.InfoID, plan.ProductCode);
        Assert.Equal("Professional", plan.Tier);
        Assert.Equal(plan.Id, result.Value!.Id);
        Assert.Equal(["Plan.Created"], h.AuditedActions);
    }

    [Theory]
    [InlineData(AdminRoles.SupportAgent)]
    [InlineData("Intruder")]
    public async Task Others_cannot_create_or_edit_plans(string role)
    {
        var h = new ServiceHarness(role);
        var plan = h.Plans.Seed();

        await Assert.ThrowsAsync<ForbiddenException>(() => h.PlanService.CreateAsync(Input()));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.PlanService.UpdateAsync(new UpdatePlanRequestDto(plan.Id, Input("Hacked"))));

        Assert.Equal("Standard", plan.Name);
        Assert.Single(h.Plans.Plans);
        Assert.Empty(h.Audit.Rows);
    }

    [Theory]
    [InlineData("", 12, 1, null, "Enter a plan name.")]
    [InlineData("P", 0, 1, null, "Duration must be 1-120 months, or empty for perpetual.")]
    [InlineData("P", 12, 0, null, "Machines must be between 1 and 100.")]
    [InlineData("P", 12, 1, 150, "The card limit must be a multiple of 100 between 100 and 102,300, or empty for unlimited.")]
    public async Task Invalid_plans_come_back_as_a_message_and_store_nothing(string name, int months, int machines, int? cards, string expected)
    {
        var h = new ServiceHarness();

        var result = await h.PlanService.CreateAsync(Input(name, months, machines, cards));

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Errors[0]);
        Assert.Empty(h.Plans.Plans);
        Assert.Equal(0, h.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Updating_changes_the_plan_and_an_invalid_update_changes_nothing()
    {
        var h = new ServiceHarness();
        var plan = h.Plans.Seed("Old");

        var ok = await h.PlanService.UpdateAsync(new UpdatePlanRequestDto(plan.Id, Input("New", months: null, cards: null)));
        var bad = await h.PlanService.UpdateAsync(new UpdatePlanRequestDto(plan.Id, Input("Broken", machines: 0)));

        Assert.True(ok.Succeeded);
        Assert.Equal("New", plan.Name);
        Assert.Null(plan.DurationMonths);
        Assert.False(bad.Succeeded);
        Assert.Equal("New", plan.Name);
        Assert.Equal(["Plan.Updated"], h.AuditedActions);
    }

    [Fact]
    public async Task Unknown_or_cancelled_plans_are_not_found()
    {
        var h = new ServiceHarness();
        var cancelled = h.Plans.Seed();
        cancelled.Cancelled = true;

        await Assert.ThrowsAsync<NotFoundException>(() => h.PlanService.GetAsync(999));
        await Assert.ThrowsAsync<NotFoundException>(() => h.PlanService.GetAsync(cancelled.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => h.PlanService.UpdateAsync(new UpdatePlanRequestDto(999, Input())));
    }

    [Fact]
    public async Task The_staff_list_shows_every_plan_in_order_including_inactive_ones()
    {
        var h = new ServiceHarness(AdminRoles.SupportAgent);
        h.Plans.Seed("B", order: 2);
        h.Plans.Seed("Hidden", active: false, order: 3);
        h.Plans.Seed("A", order: 1);

        var plans = await h.PlanService.GetAllAsync();

        Assert.Equal(["A", "B", "Hidden"], plans.Select(p => p.Name));
        await Assert.ThrowsAsync<ForbiddenException>(() => new ServiceHarness("Intruder").PlanService.GetAllAsync());
    }

    [Fact]
    public async Task First_run_creates_the_product_and_the_three_editions_once()
    {
        var h = new ServiceHarness(role: "Anonymous");

        await h.PlanService.EnsureDefaultsAsync();
        await h.PlanService.EnsureDefaultsAsync();

        Assert.Equal("INFOID", Assert.Single(h.Plans.Products).Code);
        Assert.Equal(["Standard", "Professional", "Enterprise"], h.Plans.Plans.Select(p => p.Name));
        Assert.Equal([1, 2, 3], h.Plans.Plans.Select(p => p.SortOrder));
        Assert.All(h.Plans.Plans, p => Assert.Equal(12, p.DurationMonths));
        Assert.Equal("Setup", h.UnitOfWork.Actor);
    }

    [Fact]
    public async Task First_run_never_overwrites_plans_staff_already_changed()
    {
        var h = new ServiceHarness(role: "Anonymous");
        h.Plans.Seed("My own plan");

        await h.PlanService.EnsureDefaultsAsync();

        Assert.Equal("My own plan", Assert.Single(h.Plans.Plans).Name);
        Assert.Single(h.Plans.Products);
    }
}
