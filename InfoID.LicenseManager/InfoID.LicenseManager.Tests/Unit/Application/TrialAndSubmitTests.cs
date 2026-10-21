using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.DTOs.Accounts;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Unit.Application;

public class TrialServiceTests
{
    [Fact]
    public async Task A_new_account_starts_with_the_configured_allowance()
    {
        var h = new ServiceHarness(role: "Anonymous");
        h.TrialOptions.CardLimit = 25;

        var status = await h.TrialService.GetStatusAsync(1);

        Assert.Equal(new(25, 0, 0), status);
        Assert.Equal(25, status.Remaining);
    }

    [Fact]
    public async Task Reserve_then_commit_counts_only_the_cards_printed_and_frees_the_rest()
    {
        var h = new ServiceHarness(role: "Anonymous");

        var reserved = await h.TrialService.ReserveAsync(1, 6, "FP", "1.0", "Badge");
        Assert.True(reserved.Succeeded);
        Assert.Equal(4, reserved.Status.Remaining);
        Assert.Equal(6, reserved.Status.Reserved);

        var committed = await h.TrialService.CommitAsync(1, reserved.ReservationKey!.Value, 5);

        Assert.True(committed.Succeeded);
        Assert.Equal(new(10, 5, 0), committed.Status);
        Assert.Equal("Badge", h.Trials.Reservations[0].DesignName);
    }

    [Fact]
    public async Task Committing_twice_counts_once_and_cannot_exceed_what_was_reserved()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var key = (await h.TrialService.ReserveAsync(1, 3, null, null, null)).ReservationKey!.Value;

        await h.TrialService.CommitAsync(1, key, 99);
        var again = await h.TrialService.CommitAsync(1, key, 99);

        Assert.Equal(new(10, 3, 0), again.Status); // 99 was clamped to the 3 reserved, and the repeat changed nothing
    }

    [Fact]
    public async Task Releasing_gives_all_credits_back_and_is_idempotent()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var key = (await h.TrialService.ReserveAsync(1, 4, null, null, null)).ReservationKey!.Value;

        var first = await h.TrialService.ReleaseAsync(1, key);
        var second = await h.TrialService.ReleaseAsync(1, key);

        Assert.Equal(new(10, 0, 0), first.Status);
        Assert.Equal(new(10, 0, 0), second.Status);
    }

    [Fact]
    public async Task The_allowance_cannot_be_exceeded_and_the_message_says_how_many_are_left()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await h.TrialService.CommitAsync(1, (await h.TrialService.ReserveAsync(1, 8, null, null, null)).ReservationKey!.Value, 8);

        var tooMany = await h.TrialService.ReserveAsync(1, 5, null, null, null);
        await h.TrialService.CommitAsync(1, (await h.TrialService.ReserveAsync(1, 2, null, null, null)).ReservationKey!.Value, 2);
        var none = await h.TrialService.ReserveAsync(1, 1, null, null, null);

        Assert.False(tooMany.Succeeded);
        Assert.Equal("Only 2 trial cards left. Print fewer cards, or activate InfoID.", tooMany.Error);
        Assert.False(none.Succeeded);
        Assert.Equal("You've used all 10 trial cards. Activate InfoID to keep printing.", none.Error);
        Assert.Null(none.ReservationKey);
    }

    [Theory]
    [InlineData(0, "Nothing to print.")]
    [InlineData(-3, "Nothing to print.")]
    [InlineData(11, "A trial print job can have at most 10 cards.")]
    public async Task A_print_job_must_be_between_one_card_and_the_per_job_maximum(int cards, string expected)
    {
        var h = new ServiceHarness(role: "Anonymous");

        var result = await h.TrialService.ReserveAsync(1, cards, null, null, null);

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Error);
        Assert.Empty(h.Trials.Reservations);
    }

    [Fact]
    public async Task Many_simultaneous_reservations_can_never_exceed_the_limit()
    {
        var h = new ServiceHarness(role: "Anonymous");

        var results = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => Task.Run(() => h.TrialService.ReserveAsync(1, 1, null, null, null))));

        Assert.Equal(10, results.Count(r => r.Succeeded));
        Assert.Equal(new(10, 0, 10), await h.TrialService.GetStatusAsync(1));
    }

    [Fact]
    public async Task A_reservation_the_app_never_reports_on_expires_and_gives_its_credits_back()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await h.TrialService.ReserveAsync(1, 7, null, null, null);

        h.Clock.Advance(TimeSpan.FromMinutes(31));
        var status = await h.TrialService.GetStatusAsync(1);

        Assert.Equal(new(10, 0, 0), status);
        Assert.Equal(TrialReservationStatus.Expired, h.Trials.Reservations[0].Status);
    }

    [Fact]
    public async Task Cards_printed_after_the_reservation_expired_still_count()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var key = (await h.TrialService.ReserveAsync(1, 4, null, null, null)).ReservationKey!.Value;
        h.Clock.Advance(TimeSpan.FromMinutes(31));
        await h.TrialService.GetStatusAsync(1);

        var late = await h.TrialService.CommitAsync(1, key, 4);

        Assert.Equal(new(10, 4, 0), late.Status);
    }

    [Fact]
    public async Task One_account_cannot_commit_or_release_another_accounts_reservation()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var key = (await h.TrialService.ReserveAsync(1, 3, null, null, null)).ReservationKey!.Value;

        var commit = await h.TrialService.CommitAsync(2, key, 3);
        var release = await h.TrialService.ReleaseAsync(2, key);
        var unknown = await h.TrialService.CommitAsync(1, Guid.NewGuid(), 1);

        Assert.All(new[] { commit, release, unknown }, r =>
        {
            Assert.False(r.Succeeded);
            Assert.Equal("That print job isn't known to the server.", r.Error);
        });
        Assert.Equal(new(10, 0, 3), await h.TrialService.GetStatusAsync(1));
    }

    [Fact]
    public async Task Each_account_has_its_own_allowance()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await h.TrialService.ReserveAsync(1, 10, null, null, null);

        var other = await h.TrialService.ReserveAsync(2, 10, null, null, null);

        Assert.True(other.Succeeded);
    }
}

public class SubmitRequestTests
{
    private static SubmitLicenseRequestDto Submit(long planId, string email = "Buyer@Acme.TEST", int machines = 2, string name = "Bea Buyer") =>
        new(planId, machines, name, "Acme AS", email, "123", "Norway", "Oslo", "Please call", "FP", "1.0");

    [Fact]
    public async Task A_request_without_an_account_stores_the_details_notifies_the_customer_and_is_audited()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var plan = h.Plans.Seed("Pro", LicenseTier.Professional);

        var result = await h.RequestService.SubmitAsync(Submit(plan.Id), null);

        Assert.True(result.Succeeded);
        var request = Assert.Single(h.Requests.Requests);
        Assert.Equal(result.RequestId, request.Id);
        Assert.Equal("buyer@acme.test", request.Email);
        Assert.Equal(LicenseRequestStatus.New, request.Status);
        Assert.Null(request.CustomerAccountId);
        Assert.Equal(plan.Id, request.PlanId);
        Assert.Equal(2, request.Machines);
        Assert.Equal("CustomerRequest", h.UnitOfWork.Actor);
        var mail = Assert.Single(h.Email.Sent);
        Assert.Equal("buyer@acme.test", mail.To);
        Assert.Contains("Pro", mail.Subject);
        Assert.Contains("Request.Submitted", h.AuditedActions);
    }

    [Fact]
    public async Task A_signed_in_account_always_uses_its_own_verified_e_mail_and_is_linked()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var plan = h.Plans.Seed();
        var account = h.Accounts.Seed("real@customer.test");
        var session = new AccountSessionDto(account.Id, new AccountProfileDto(account.FullName, null, account.Email, null, null));

        await h.RequestService.SubmitAsync(Submit(plan.Id, email: "someone-else@evil.test"), session);

        var request = Assert.Single(h.Requests.Requests);
        Assert.Equal("real@customer.test", request.Email);
        Assert.Equal(account.Id, request.CustomerAccountId);
    }

    [Fact]
    public async Task The_sales_team_is_notified_only_when_configured()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var plan = h.Plans.Seed();
        h.SalesOptions.Email = "sales@infoid.test";

        await h.RequestService.SubmitAsync(Submit(plan.Id), null);

        Assert.Equal(["buyer@acme.test", "sales@infoid.test"], h.Email.Sent.Select(m => m.To));
    }

    [Theory]
    [InlineData("", "a@b.com", 1, "Enter your name.")]
    [InlineData("N", "nope", 1, "Enter a valid email address.")]
    [InlineData("N", "a@b.com", 0, "Number of computers must be between 1 and 100.")]
    [InlineData("N", "a@b.com", 101, "Number of computers must be between 1 and 100.")]
    public async Task Invalid_requests_are_refused_and_store_nothing(string name, string email, int machines, string expected)
    {
        var h = new ServiceHarness(role: "Anonymous");
        var plan = h.Plans.Seed();

        var result = await h.RequestService.SubmitAsync(Submit(plan.Id, email, machines, name), null);

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Message);
        Assert.Empty(h.Requests.Requests);
    }

    [Fact]
    public async Task An_inactive_or_unknown_plan_is_refused()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var inactive = h.Plans.Seed(active: false);

        var off = await h.RequestService.SubmitAsync(Submit(inactive.Id), null);
        var unknown = await h.RequestService.SubmitAsync(Submit(999), null);

        Assert.Equal("That plan is no longer available. Please choose another.", off.Message);
        Assert.Equal(off.Message, unknown.Message);
    }

    [Fact]
    public async Task At_most_five_open_requests_per_e_mail_are_accepted()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var plan = h.Plans.Seed();

        var results = new List<SubmittedRequestDto>();
        for (var i = 0; i < 6; i++) results.Add(await h.RequestService.SubmitAsync(Submit(plan.Id), null));

        Assert.Equal(5, results.Count(r => r.Succeeded));
        Assert.Equal("You already have several requests in progress. We'll contact you soon.", results[5].Message);
        // A decided request stops counting.
        h.Requests.Requests[0].Status = LicenseRequestStatus.Rejected;
        Assert.True((await h.RequestService.SubmitAsync(Submit(plan.Id), null)).Succeeded);
    }

    [Fact]
    public async Task Overlong_text_is_cut_to_the_column_sizes_instead_of_failing_in_the_database()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var plan = h.Plans.Seed();

        await h.RequestService.SubmitAsync(Submit(plan.Id, name: new string('x', 500)) with { Message = new string('m', 5000), Phone = new string('1', 200) }, null);

        var request = Assert.Single(h.Requests.Requests);
        Assert.Equal(200, request.FullName.Length);
        Assert.Equal(2000, request.Message!.Length);
        Assert.Equal(50, request.Phone!.Length);
    }

    [Fact]
    public async Task A_mail_server_that_is_down_does_not_fail_the_submission()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var plan = h.Plans.Seed();
        h.Email.Failure = new InvalidOperationException("smtp down");

        var result = await h.RequestService.SubmitAsync(Submit(plan.Id), null);

        Assert.True(result.Succeeded);
        Assert.Contains("Email.Failed", h.AuditedActions);
    }

    [Fact]
    public async Task The_public_plan_list_contains_only_active_plans_in_order()
    {
        var h = new ServiceHarness(role: "Anonymous");
        h.Plans.Seed("Second", order: 2);
        h.Plans.Seed("Hidden", active: false, order: 1);
        h.Plans.Seed("First", order: 0);

        var plans = await h.PlanService.GetActivePlansAsync(ProductCodes.InfoID);

        Assert.Equal(["First", "Second"], plans.Select(p => p.Name));
    }
}
