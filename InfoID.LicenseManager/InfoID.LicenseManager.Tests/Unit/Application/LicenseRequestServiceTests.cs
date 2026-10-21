using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Requests;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Unit.Application;

public class LicenseRequestServiceTests
{
    private static readonly DateOnly Today = TestData.Today;

    private static ApproveLicenseRequestDto Approve(long id, LicenseTier tier = LicenseTier.Professional, LicenseType type = LicenseType.Subscription,
        DateOnly? expiry = null, int? cards = null, int machines = 2, bool offline = false, string? notes = null) =>
        new(id, tier, type, type == LicenseType.Perpetual ? null : expiry ?? Today.AddYears(1), cards, CardLimitPeriod.Monthly, machines, offline, notes);

    private static (ServiceHarness H, Plan Plan, LicenseRequest Request) Arrange(string role = AdminRoles.LicenseManager, long? accountId = null)
    {
        var h = new ServiceHarness(role);
        var plan = h.Plans.Seed("Pro", LicenseTier.Professional, months: 12, machines: 2, maxCards: 500);
        var request = h.Requests.Seed(plan, accountId: accountId);
        return (h, plan, request);
    }

    // ---------------------------------------------------------------- authorization ----

    [Theory]
    [InlineData(AdminRoles.SupportAgent)]
    [InlineData("Intruder")]
    public async Task Staff_without_license_rights_cannot_process_requests(string role)
    {
        var (h, _, request) = Arrange(role);

        await Assert.ThrowsAsync<ForbiddenException>(() => h.RequestService.MarkInProgressAsync(request.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.RequestService.ApproveAsync(Approve(request.Id)));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.RequestService.RejectAsync(new RejectLicenseRequestDto(request.Id, "no")));

        Assert.Equal(LicenseRequestStatus.New, request.Status);
        Assert.Empty(h.Licenses.Codes);
        Assert.Empty(h.Email.Sent);
        Assert.Empty(h.Audit.Rows);
    }

    [Fact]
    public async Task Support_agents_may_look_but_strangers_may_not()
    {
        var (h, _, request) = Arrange(AdminRoles.SupportAgent);

        Assert.Single(await h.RequestService.SearchAsync(new LicenseRequestSearchDto()));
        Assert.Equal(request.Id, (await h.RequestService.GetAsync(request.Id)).Id);
        Assert.Equal(1, await h.RequestService.CountNewAsync());

        var stranger = new ServiceHarness("Intruder");
        await Assert.ThrowsAsync<ForbiddenException>(() => stranger.RequestService.SearchAsync(new LicenseRequestSearchDto()));
    }

    // ---------------------------------------------------------------- search ----

    [Fact]
    public async Task The_default_filter_shows_only_open_requests_and_other_filters_work()
    {
        var (h, plan, newRequest) = Arrange();
        var inProgress = h.Requests.Seed(plan, status: LicenseRequestStatus.InProgress, email: "b@x.test");
        var approved = h.Requests.Seed(plan, status: LicenseRequestStatus.Approved, email: "c@x.test");
        var rejected = h.Requests.Seed(plan, status: LicenseRequestStatus.Rejected, email: "d@x.test");

        Assert.Equal(new[] { newRequest.Id, inProgress.Id }.Order(), (await h.RequestService.SearchAsync(new LicenseRequestSearchDto())).Select(r => r.Id).Order());
        Assert.Equal(4, (await h.RequestService.SearchAsync(new LicenseRequestSearchDto(RequestStatusFilter.All))).Count);
        Assert.Equal([approved.Id], (await h.RequestService.SearchAsync(new LicenseRequestSearchDto(LicenseRequestStatus.Approved))).Select(r => r.Id));
        Assert.Equal([rejected.Id], (await h.RequestService.SearchAsync(new LicenseRequestSearchDto("Rejected"))).Select(r => r.Id));
        Assert.Empty(await h.RequestService.SearchAsync(new LicenseRequestSearchDto("Bogus")));
    }

    [Fact]
    public async Task Search_text_matches_name_company_e_mail_and_the_result_carries_plan_and_code_details()
    {
        var (h, plan, request) = Arrange();
        var approved = h.Requests.Seed(plan, email: "other@x.test", fullName: "Olga Other", company: "Other Ltd", status: LicenseRequestStatus.Approved);
        approved.LicenseCode = h.Licenses.Add(TestData.NewLicense(code: TestData.KnownCode));
        approved.HandledByAdminUserId = h.Admins.Seed("handler@infoid.test", "x", fullName: "Hanna Handler").Id;

        var byCompany = await h.RequestService.SearchAsync(new LicenseRequestSearchDto(RequestStatusFilter.All, "other ltd"));
        var byEmail = await h.RequestService.SearchAsync(new LicenseRequestSearchDto(RequestStatusFilter.All, "buyer@acme"));

        var dto = Assert.Single(byCompany);
        Assert.Equal(TestData.KnownCodeDisplay, dto.ActivationCode);
        Assert.Equal("Hanna Handler", dto.HandledBy);
        Assert.Equal("Pro", dto.PlanName);
        Assert.Equal(500, dto.PlanMaxCards);
        Assert.Equal(request.Id, Assert.Single(byEmail).Id);
    }

    [Fact]
    public async Task An_unknown_request_is_not_found()
    {
        var (h, _, _) = Arrange();

        await Assert.ThrowsAsync<NotFoundException>(() => h.RequestService.GetAsync(999));
    }

    // ---------------------------------------------------------------- mark in progress ----

    [Fact]
    public async Task Marking_in_progress_records_the_handler_and_is_audited()
    {
        var (h, _, request) = Arrange();

        var result = await h.RequestService.MarkInProgressAsync(request.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(LicenseRequestStatus.InProgress, request.Status);
        Assert.Equal(7, request.HandledByAdminUserId);
        Assert.Equal(["Request.InProgress"], h.AuditedActions);
        Assert.Equal(1, h.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_decided_request_cannot_be_marked_in_progress_and_an_unknown_one_rolls_back()
    {
        var (h, plan, _) = Arrange();
        var done = h.Requests.Seed(plan, status: LicenseRequestStatus.Approved);

        var refused = await h.RequestService.MarkInProgressAsync(done.Id);

        Assert.Equal("This request is already approved.", refused.Errors[0]);
        await Assert.ThrowsAsync<NotFoundException>(() => h.RequestService.MarkInProgressAsync(404));
        Assert.Equal(1, h.UnitOfWork.Rollbacks);
    }

    // ---------------------------------------------------------------- approve ----

    [Fact]
    public async Task Approving_creates_the_code_with_the_confirmed_terms_and_links_everything_in_one_transaction()
    {
        var (h, _, request) = Arrange();
        var customer = h.Accounts.Seed("cathy@customer.test");
        request.CustomerAccountId = customer.Id;
        request.Phone = "123";

        var result = await h.RequestService.ApproveAsync(Approve(request.Id, LicenseTier.Enterprise, expiry: Today.AddMonths(18), cards: 2000, machines: 3, offline: true, notes: " VIP "));

        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        var code = Assert.Single(h.Licenses.Codes);
        Assert.Equal(TestData.Today.AddMonths(18), code.ExpiryDate);
        Assert.Equal("Enterprise", code.Tier);
        Assert.Equal(2000, code.MaxCardsPerPeriod);
        Assert.Equal(3, code.MaxActivations);
        Assert.True(code.AllowOfflineActivation);
        Assert.Equal(request.ProductCode, code.ProductCode);
        Assert.Equal(customer.Id, code.CustomerAccountId);
        Assert.Equal(7, code.CreatedByAdminUserId);
        Assert.Equal($"From request #{request.Id}. VIP", code.Notes);
        Assert.Equal("Acme AS", code.CustomerRecord!.OrganizationName);
        Assert.Equal("buyer@acme.test", code.CustomerRecord.ContactEmail);
        Assert.Equal(LicenseRequestStatus.Approved, request.Status);
        Assert.Same(code, request.LicenseCode);
        Assert.Equal(7, request.HandledByAdminUserId);
        Assert.Same(code.CustomerRecord, customer.CustomerRecord);
        Assert.Equal(1, h.UnitOfWork.Commits);
        Assert.Equal([request.Id], h.Requests.Requests.Where(r => r.Status == LicenseRequestStatus.Approved).Select(r => r.Id));
    }

    [Fact]
    public async Task Approving_e_mails_the_customer_the_code_and_audits_both_events_without_secrets()
    {
        var (h, _, request) = Arrange(accountId: null);

        var result = await h.RequestService.ApproveAsync(Approve(request.Id, expiry: Today.AddYears(1), machines: 2));

        var display = ActivationCodeFormat.Display(h.Licenses.Codes[0].ActivationCode);
        Assert.Equal(display, result.Value!.ActivationCode);
        Assert.True(result.Value.EmailSent);
        var mail = Assert.Single(h.Email.Sent);
        Assert.Equal("buyer@acme.test", mail.To);
        Assert.Equal("Your activation code", mail.Subject);
        Assert.Contains(display, mail.Body);
        Assert.Contains("Edition: Professional", mail.Body);
        Assert.Contains("Computers: 2", mail.Body);
        Assert.Equal(["LicenseCode.Created", "Request.Approved"], h.AuditedActions);
        Assert.Contains(display, h.Audit.Rows[1].DetailsJson);
    }

    [Fact]
    public async Task A_perpetual_plan_is_approved_without_an_expiry()
    {
        var (h, _, request) = Arrange();

        var result = await h.RequestService.ApproveAsync(Approve(request.Id, type: LicenseType.Perpetual));

        Assert.True(result.Succeeded);
        Assert.Null(h.Licenses.Codes[0].ExpiryDate);
        Assert.Contains("Valid: perpetual", h.Email.Sent[0].Body);
    }

    [Fact]
    public async Task Invalid_terms_are_refused_before_anything_is_created()
    {
        var (h, _, request) = Arrange();

        var result = await h.RequestService.ApproveAsync(Approve(request.Id, cards: 150, machines: 500));
        var pastExpiry = await h.RequestService.ApproveAsync(Approve(request.Id, expiry: Today.AddDays(-1)));

        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("between 100 and 102,300", string.Join(" | ", result.Errors));
        Assert.Equal("The expiry date must be in the future.", Assert.Single(pastExpiry.Errors));
        Assert.Empty(h.Licenses.Codes);
        Assert.Equal(LicenseRequestStatus.New, request.Status);
        Assert.Empty(h.Email.Sent);
        Assert.Equal(0, h.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_request_can_only_be_approved_once()
    {
        var (h, _, request) = Arrange();

        var first = await h.RequestService.ApproveAsync(Approve(request.Id));
        var second = await h.RequestService.ApproveAsync(Approve(request.Id));

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal("This request is already approved.", second.Errors[0]);
        Assert.Single(h.Licenses.Codes);
        Assert.Single(h.Email.Sent);
    }

    [Fact]
    public async Task A_rejected_request_cannot_be_approved_and_an_unknown_one_is_not_found()
    {
        var (h, plan, _) = Arrange();
        var rejected = h.Requests.Seed(plan, status: LicenseRequestStatus.Rejected);

        var result = await h.RequestService.ApproveAsync(Approve(rejected.Id));

        Assert.Equal("This request is already rejected.", result.Errors[0]);
        Assert.Empty(h.Licenses.Codes);
        await Assert.ThrowsAsync<NotFoundException>(() => h.RequestService.ApproveAsync(Approve(9999)));
    }

    [Fact]
    public async Task A_failure_while_storing_the_code_leaves_the_request_untouched_and_sends_nothing()
    {
        var (h, _, request) = Arrange();
        h.Licenses.FailOnAdd = new InvalidOperationException("database went away");

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.RequestService.ApproveAsync(Approve(request.Id)));

        Assert.Equal(1, h.UnitOfWork.Rollbacks);
        Assert.Equal(LicenseRequestStatus.New, request.Status);
        Assert.Null(request.LicenseCode);
        Assert.Empty(h.Email.Sent);
        Assert.Empty(h.Audit.Rows);
    }

    [Fact]
    public async Task A_mail_server_that_is_down_does_not_undo_the_approval_but_staff_are_told()
    {
        var (h, _, request) = Arrange();
        h.Email.Failure = new InvalidOperationException("smtp down");

        var result = await h.RequestService.ApproveAsync(Approve(request.Id));

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.EmailSent);
        Assert.Equal(LicenseRequestStatus.Approved, request.Status);
        Assert.Contains("Email.Failed", h.AuditedActions);
    }

    [Fact]
    public async Task The_approval_locks_the_request_row_before_reading_it()
    {
        var (h, _, request) = Arrange();

        await h.RequestService.ApproveAsync(Approve(request.Id));

        // The fake repository throws when a row lock is requested outside a transaction, so reaching here proves it was inside one.
        Assert.Equal(1, h.UnitOfWork.Commits);
    }

    // ---------------------------------------------------------------- reject ----

    [Fact]
    public async Task Rejecting_stores_the_trimmed_reason_notifies_the_customer_and_is_audited()
    {
        var (h, _, request) = Arrange();

        var result = await h.RequestService.RejectAsync(new RejectLicenseRequestDto(request.Id, "  Payment not received  "));

        Assert.True(result.Succeeded);
        Assert.Equal(LicenseRequestStatus.Rejected, request.Status);
        Assert.Equal("Payment not received", request.StatusReason);
        var mail = Assert.Single(h.Email.Sent);
        Assert.Equal("About your license request", mail.Subject);
        Assert.Contains("Reason: Payment not received", mail.Body);
        Assert.Equal(["Request.Rejected"], h.AuditedActions);
        Assert.Empty(h.Licenses.Codes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejecting_needs_a_reason_the_customer_can_read(string reason)
    {
        var (h, _, request) = Arrange();

        var result = await h.RequestService.RejectAsync(new RejectLicenseRequestDto(request.Id, reason));

        Assert.False(result.Succeeded);
        Assert.Equal(LicenseRequestStatus.New, request.Status);
        Assert.Empty(h.Email.Sent);
    }

    [Fact]
    public async Task A_decided_request_cannot_be_rejected_and_a_reason_that_is_too_long_is_refused()
    {
        var (h, plan, request) = Arrange();
        var approved = h.Requests.Seed(plan, status: LicenseRequestStatus.Approved);

        var again = await h.RequestService.RejectAsync(new RejectLicenseRequestDto(approved.Id, "x"));
        var tooLong = await h.RequestService.RejectAsync(new RejectLicenseRequestDto(request.Id, new string('x', 301)));

        Assert.Equal("This request is already approved.", again.Errors[0]);
        Assert.False(tooLong.Succeeded);
        Assert.Equal(LicenseRequestStatus.New, request.Status);
    }
}
