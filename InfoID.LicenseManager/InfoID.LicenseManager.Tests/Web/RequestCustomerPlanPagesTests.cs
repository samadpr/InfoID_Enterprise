using System.Net;
using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Web;

/// <summary>Staff pages for license requests, customer accounts and plans, driven through the real pipeline.</summary>
public class RequestCustomerPlanPagesTests : IDisposable
{
    private const string Password = "Correct-Horse-9";

    private readonly WebAppFactory _app = new();

    public void Dispose() => _app.Dispose();

    private async Task<HttpClient> SignedInAsync(string role)
    {
        var email = role.ToLowerInvariant() + "@infoid.test";
        _app.Admins.Seed(email, Password, role, fullName: role + " User");
        var browser = _app.CreateBrowser();
        await browser.SignInAsync(email, Password);
        return browser;
    }

    private static async Task<string> FollowAsync(HttpClient browser, HttpResponseMessage redirect)
    {
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        return await browser.GetStringAsync(redirect.Headers.Location!);
    }

    private (LicenseRequest Request, Plan Plan) SeedRequest(long? accountId = null)
    {
        var plan = _app.Plans.Seed("Professional", LicenseTier.Professional, machines: 2);
        return (_app.Requests.Seed(plan, accountId: accountId), plan);
    }

    private static (string, string)[] ApproveForm(params (string, string)[] extra) =>
        new (string, string)[]
        {
            ("Approve.Tier", "Professional"), ("Approve.Type", "Subscription"),
            ("Approve.ExpiryDate", TestData.Today.AddYears(1).ToString("yyyy-MM-dd")),
            ("Approve.UnlimitedCards", "true"), ("Approve.MaxCardsPerPeriod", "1000"), ("Approve.LimitPeriod", "Monthly"),
            ("Approve.MaxActivations", "2"),
        }.Where(f => extra.All(e => e.Item1 != f.Item1)).Concat(extra).ToArray();

    // ================================================================ requests ====

    [Fact]
    public async Task Anonymous_visitors_are_sent_to_the_login_page_for_every_staff_page()
    {
        using var browser = _app.CreateBrowser();

        foreach (var url in new[] { "/Requests", "/Requests/Details/1", "/Customers", "/Customers/Details/1", "/Plans", "/Plans/Create", "/Plans/Edit/1" })
        {
            var response = await browser.GetAsync(url);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Account/Login", response.Headers.Location!.AbsolutePath);
        }
    }

    [Fact]
    public async Task The_inbox_shows_open_requests_by_default_and_can_filter_by_status_and_text()
    {
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);
        var plan = _app.Plans.Seed();
        _app.Requests.Seed(plan, "open@acme.test", "Olga Open");
        _app.Requests.Seed(plan, "done@acme.test", "Dan Done", status: LicenseRequestStatus.Approved);

        var open = await browser.GetStringAsync("/Requests");
        var all = await browser.GetStringAsync("/Requests?status=All");
        var text = await browser.GetStringAsync("/Requests?status=All&text=dan");

        Assert.Contains("open@acme.test", open);
        Assert.DoesNotContain("done@acme.test", open);
        Assert.Contains("done@acme.test", all);
        Assert.Contains("done@acme.test", text);
        Assert.DoesNotContain("open@acme.test", text);
    }

    [Fact]
    public async Task Request_details_are_html_encoded_and_unknown_requests_are_404()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var plan = _app.Plans.Seed();
        var request = _app.Requests.Seed(plan, fullName: "<script>alert(1)</script>");

        var html = await browser.GetStringAsync($"/Requests/Details/{request.Id}");
        var missing = await browser.GetAsync("/Requests/Details/999");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Support_agents_see_a_request_but_no_approve_or_reject_forms()
    {
        using var support = await SignedInAsync(AdminRoles.SupportAgent);
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);
        var (request, _) = SeedRequest();

        var supportHtml = await support.GetStringAsync($"/Requests/Details/{request.Id}");
        var managerHtml = await manager.GetStringAsync($"/Requests/Details/{request.Id}");

        Assert.DoesNotContain("Approve and send code", supportHtml);
        Assert.DoesNotContain("Reject request", supportHtml);
        Assert.Contains("Approve and send code", managerHtml);
        Assert.Contains("Reject request", managerHtml);
        Assert.Contains("Mark as in progress", managerHtml);
    }

    [Theory]
    [InlineData("MarkInProgress")]
    [InlineData("Approve")]
    [InlineData("Reject")]
    public async Task Support_agents_cannot_process_requests(string action)
    {
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);
        var (request, _) = SeedRequest();
        var token = await browser.GetTokenAsync("/Requests");

        var response = await browser.PostFormAsync($"/Requests/{action}/{request.Id}", token,
            [.. ApproveForm(), ("Reject.Reason", "no")]);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(LicenseRequestStatus.New, request.Status);
        Assert.Empty(_app.Licenses.Codes);
        Assert.Empty(_app.Email.Sent);
    }

    [Fact]
    public async Task Processing_a_request_needs_the_anti_forgery_token_and_a_POST()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var (request, _) = SeedRequest();

        var noToken = await browser.PostAsync($"/Requests/Approve/{request.Id}", new FormUrlEncodedContent(ApproveForm().Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2))));
        var get = await browser.GetAsync($"/Requests/Approve/{request.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        Assert.Equal(LicenseRequestStatus.New, request.Status);
        Assert.Empty(_app.Licenses.Codes);
    }

    [Fact]
    public async Task Marking_in_progress_updates_the_request_and_shows_a_message()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var (request, _) = SeedRequest();
        var token = await browser.GetTokenAsync($"/Requests/Details/{request.Id}");

        var html = await FollowAsync(browser, await browser.PostFormAsync($"/Requests/MarkInProgress/{request.Id}", token));

        Assert.Contains("The request is marked as in progress.", html);
        Assert.Equal(LicenseRequestStatus.InProgress, request.Status);
    }

    [Fact]
    public async Task Approving_creates_the_code_emails_the_customer_and_shows_it_on_the_request()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var (request, _) = SeedRequest();
        var token = await browser.GetTokenAsync($"/Requests/Details/{request.Id}");

        var html = await FollowAsync(browser, await browser.PostFormAsync($"/Requests/Approve/{request.Id}", token, ApproveForm(("Approve.Notes", "VIP"))));

        Assert.Contains("Approved. The activation code was created and e-mailed to the customer.", html);
        var code = Assert.Single(_app.Licenses.Codes);
        Assert.Equal(2, code.MaxActivations);
        Assert.Equal(LicenseRequestStatus.Approved, request.Status);
        Assert.Equal("buyer@acme.test", Assert.Single(_app.Email.Sent).To);
        Assert.Contains(ActivationCodeFormat.Display(code.ActivationCode), html);
    }

    [Fact]
    public async Task A_failing_mail_server_still_creates_the_code_and_tells_staff_to_pass_it_on()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var (request, _) = SeedRequest();
        _app.Email.Failure = new InvalidOperationException("smtp down");
        var token = await browser.GetTokenAsync($"/Requests/Details/{request.Id}");

        var html = await FollowAsync(browser, await browser.PostFormAsync($"/Requests/Approve/{request.Id}", token, ApproveForm()));

        Assert.Contains("the e-mail could not be sent", html);
        Assert.Single(_app.Licenses.Codes);
        Assert.Equal(LicenseRequestStatus.Approved, request.Status);
    }

    [Fact]
    public async Task Invalid_approval_terms_create_nothing_and_show_the_first_problem()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var (request, _) = SeedRequest();
        var token = await browser.GetTokenAsync($"/Requests/Details/{request.Id}");

        var outOfRange = await FollowAsync(browser, await browser.PostFormAsync($"/Requests/Approve/{request.Id}", token, ApproveForm(("Approve.MaxActivations", "500"))));
        var pastExpiry = await FollowAsync(browser, await browser.PostFormAsync($"/Requests/Approve/{request.Id}", token,
            ApproveForm(("Approve.ExpiryDate", TestData.Today.AddDays(-3).ToString("yyyy-MM-dd")))));

        Assert.Contains("Machines must be between 1 and 100.", outOfRange);
        Assert.Contains("The expiry date must be in the future.", pastExpiry);
        Assert.Empty(_app.Licenses.Codes);
        Assert.Equal(LicenseRequestStatus.New, request.Status);
        Assert.Empty(_app.Email.Sent);
    }

    [Fact]
    public async Task The_request_id_comes_from_the_route_never_from_a_posted_field()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var (target, plan) = SeedRequest();
        var other = _app.Requests.Seed(plan, "other@acme.test");
        var token = await browser.GetTokenAsync($"/Requests/Details/{target.Id}");

        await browser.PostFormAsync($"/Requests/Approve/{target.Id}", token, [.. ApproveForm(), ("id", other.Id.ToString()), ("Approve.Id", other.Id.ToString()), ("RequestId", other.Id.ToString())]);

        Assert.Equal(LicenseRequestStatus.Approved, target.Status);
        Assert.Equal(LicenseRequestStatus.New, other.Status);
    }

    [Fact]
    public async Task Posted_fields_that_are_not_part_of_the_form_cannot_change_the_request()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var (request, _) = SeedRequest();
        var token = await browser.GetTokenAsync($"/Requests/Details/{request.Id}");

        await browser.PostFormAsync($"/Requests/Approve/{request.Id}", token, [.. ApproveForm(), ("Email", "evil@x.test"), ("Approve.Email", "evil@x.test"), ("Status", "Rejected")]);

        Assert.Equal("buyer@acme.test", request.Email);
        Assert.Equal("buyer@acme.test", Assert.Single(_app.Email.Sent).To);
        Assert.Equal(LicenseRequestStatus.Approved, request.Status);
    }

    [Fact]
    public async Task Rejecting_needs_a_reason_and_then_emails_the_customer()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var (request, _) = SeedRequest();
        var token = await browser.GetTokenAsync($"/Requests/Details/{request.Id}");

        var empty = await FollowAsync(browser, await browser.PostFormAsync($"/Requests/Reject/{request.Id}", token, ("Reject.Reason", "")));
        var ok = await FollowAsync(browser, await browser.PostFormAsync($"/Requests/Reject/{request.Id}", token, ("Reject.Reason", "Payment not received")));

        Assert.Contains("Enter a reason (the customer sees it).", empty);
        Assert.Contains("The request was rejected.", ok);
        Assert.Equal(LicenseRequestStatus.Rejected, request.Status);
        Assert.Contains("Payment not received", Assert.Single(_app.Email.Sent).Body);
    }

    [Fact]
    public async Task A_decided_request_cannot_be_approved_again_from_the_page()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var (request, _) = SeedRequest();
        var token = await browser.GetTokenAsync($"/Requests/Details/{request.Id}");
        await browser.PostFormAsync($"/Requests/Reject/{request.Id}", token, ("Reject.Reason", "No"));

        var html = await FollowAsync(browser, await browser.PostFormAsync($"/Requests/Approve/{request.Id}", token, ApproveForm()));

        Assert.Contains("alert error", html);
        Assert.Empty(_app.Licenses.Codes);
        Assert.Equal(LicenseRequestStatus.Rejected, request.Status);
    }

    // ================================================================ customers ====

    [Fact]
    public async Task The_customer_list_filters_and_encodes_what_it_prints()
    {
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);
        _app.Accounts.Seed("lead@c.test", fullName: "<b>Bold</b>");
        _app.Accounts.Seed("off@c.test", disabled: true);

        var all = await browser.GetStringAsync("/Customers");
        var disabled = await browser.GetStringAsync("/Customers?filter=Disabled");
        var search = await browser.GetStringAsync("/Customers?text=%22%3E%3Cscript%3E");

        Assert.Contains("lead@c.test", all);
        Assert.DoesNotContain("<b>Bold</b>", all);
        Assert.Contains("off@c.test", disabled);
        Assert.DoesNotContain("lead@c.test", disabled);
        Assert.DoesNotContain("<script>", search);
    }

    [Fact]
    public async Task Customer_details_show_the_account_and_unknown_ids_are_404()
    {
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);
        var account = _app.Accounts.Seed("cathy@c.test", fullName: "Cathy Customer", company: "Acme");

        var html = await browser.GetStringAsync($"/Customers/Details/{account.Id}");
        var missing = await browser.GetAsync("/Customers/Details/999");

        Assert.Contains("cathy@c.test", html);
        Assert.Contains("Acme", html);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Disable_and_enable_buttons_appear_only_for_managers_and_match_the_account_state()
    {
        using var support = await SignedInAsync(AdminRoles.SupportAgent);
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);
        var active = _app.Accounts.Seed("a@c.test");
        var disabled = _app.Accounts.Seed("d@c.test", disabled: true);

        Assert.DoesNotContain("Disable account", await support.GetStringAsync($"/Customers/Details/{active.Id}"));
        Assert.Contains("Disable account", await manager.GetStringAsync($"/Customers/Details/{active.Id}"));
        Assert.DoesNotContain("Enable account", await manager.GetStringAsync($"/Customers/Details/{active.Id}"));
        Assert.Contains("Enable account", await manager.GetStringAsync($"/Customers/Details/{disabled.Id}"));
        Assert.DoesNotContain("Enable account", await support.GetStringAsync($"/Customers/Details/{disabled.Id}"));
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("Enable")]
    public async Task Support_agents_cannot_disable_or_enable_accounts(string action)
    {
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);
        var account = _app.Accounts.Seed("cathy@c.test");
        var token = await browser.GetTokenAsync("/Customers");

        var response = await browser.PostFormAsync($"/Customers/{action}/{account.Id}", token, ("Disable.Reason", "x"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(account.IsDisabled);
    }

    [Fact]
    public async Task Disabling_needs_a_reason_signs_the_customer_out_and_enabling_restores_the_account()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var account = _app.Accounts.Seed("cathy@c.test");
        _app.Accounts.Sessions.Add(new CustomerSession { CustomerAccountId = account.Id, TokenHash = "A" });
        var token = await browser.GetTokenAsync($"/Customers/Details/{account.Id}");

        var noReason = await FollowAsync(browser, await browser.PostFormAsync($"/Customers/Disable/{account.Id}", token, ("Disable.Reason", "")));
        var disabled = await FollowAsync(browser, await browser.PostFormAsync($"/Customers/Disable/{account.Id}", token, ("Disable.Reason", "Abuse")));
        var enabled = await FollowAsync(browser, await browser.PostFormAsync($"/Customers/Enable/{account.Id}", token));

        Assert.Contains("alert error", noReason);
        Assert.Contains("The account is disabled and signed out everywhere.", disabled);
        Assert.Contains("The account is enabled again.", enabled);
        Assert.False(account.IsDisabled);
        Assert.NotNull(_app.Accounts.Sessions.Single().RevokedUtc);
    }

    [Fact]
    public async Task The_customer_id_comes_from_the_route_never_from_a_posted_field()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var target = _app.Accounts.Seed("target@c.test");
        var other = _app.Accounts.Seed("other@c.test");
        var token = await browser.GetTokenAsync("/Customers");

        await browser.PostFormAsync($"/Customers/Disable/{target.Id}", token, ("Disable.Reason", "x"), ("id", other.Id.ToString()), ("Id", other.Id.ToString()));

        Assert.True(target.IsDisabled);
        Assert.False(other.IsDisabled);
    }

    [Fact]
    public async Task Support_agents_may_resend_a_verification_code_and_a_second_try_is_throttled()
    {
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);
        var account = _app.Accounts.Seed("cathy@c.test", verified: false);
        var token = await browser.GetTokenAsync($"/Customers/Details/{account.Id}");

        var first = await FollowAsync(browser, await browser.PostFormAsync($"/Customers/ResendVerification/{account.Id}", token));
        var second = await FollowAsync(browser, await browser.PostFormAsync($"/Customers/ResendVerification/{account.Id}", token));

        Assert.Contains("A new verification code was sent to the customer.", first);
        Assert.Contains("A code was sent less than a minute ago.", second);
        Assert.Single(_app.Email.Sent);
    }

    [Fact]
    public async Task Customer_actions_refuse_GET_requests_and_missing_tokens()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var account = _app.Accounts.Seed("cathy@c.test");

        var get = await browser.GetAsync($"/Customers/Disable/{account.Id}");
        var noToken = await browser.PostAsync($"/Customers/Disable/{account.Id}", new FormUrlEncodedContent([new("Disable.Reason", "x")]));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.False(account.IsDisabled);
    }

    // ================================================================ plans ====

    private static (string, string)[] PlanForm(params (string, string)[] extra) =>
        new (string, string)[]
        {
            ("Name", "Team"), ("Tier", "Professional"), ("Tagline", "For teams"), ("FeaturesText", "A\r\nB"), ("DurationMonths", "12"),
            ("DefaultMachines", "3"), ("UnlimitedCards", "true"), ("MaxCardsPerPeriod", "1000"), ("LimitPeriod", "Monthly"),
            ("PriceText", "Contact sales"), ("SortOrder", "4"), ("IsActive", "true"),
        }.Where(f => extra.All(e => e.Item1 != f.Item1)).Concat(extra).ToArray();

    [Fact]
    public async Task The_plan_list_shows_every_plan_including_inactive_ones_and_hides_edit_from_support()
    {
        using var support = await SignedInAsync(AdminRoles.SupportAgent);
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);
        _app.Plans.Seed("Visible");
        _app.Plans.Seed("Hidden", active: false, order: 2);

        var supportHtml = await support.GetStringAsync("/Plans");
        var managerHtml = await manager.GetStringAsync("/Plans");

        Assert.Contains("Visible", supportHtml);
        Assert.Contains("Hidden", supportHtml);
        Assert.DoesNotContain("/Plans/Edit/", supportHtml);
        Assert.Contains("/Plans/Edit/", managerHtml);
    }

    [Fact]
    public async Task Support_agents_cannot_open_or_post_the_plan_forms()
    {
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);
        var plan = _app.Plans.Seed("Standard");
        var token = await browser.GetTokenAsync("/Plans");

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/Plans/Create")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync($"/Plans/Edit/{plan.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostFormAsync("/Plans/Create", token, PlanForm())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostFormAsync($"/Plans/Edit/{plan.Id}", token, PlanForm(("Name", "Hacked")))).StatusCode);

        Assert.Equal("Standard", plan.Name);
        Assert.Single(_app.Plans.Plans);
    }

    [Fact]
    public async Task A_manager_creates_a_plan_and_sees_it_in_the_list()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var token = await browser.GetTokenAsync("/Plans/Create");

        var html = await FollowAsync(browser, await browser.PostFormAsync("/Plans/Create", token, PlanForm()));

        Assert.Contains("Plan &quot;Team&quot; created.", html);
        var plan = Assert.Single(_app.Plans.Plans);
        Assert.Equal("Team", plan.Name);
        Assert.Equal(3, plan.DefaultMachines);
        Assert.Equal(4, plan.SortOrder);
        Assert.Contains("Team", html);
    }

    [Theory]
    [InlineData("Name", "", "Enter a plan name.")]
    [InlineData("DefaultMachines", "0", "Machines must be between 1 and 100.")]
    [InlineData("DurationMonths", "500", "Duration must be 1-120 months.")]
    public async Task An_invalid_plan_is_shown_again_with_the_problem_and_nothing_is_saved(string field, string value, string expected)
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var token = await browser.GetTokenAsync("/Plans/Create");

        var response = await browser.PostFormAsync("/Plans/Create", token, PlanForm((field, value)));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, html);
        Assert.Empty(_app.Plans.Plans);
    }

    [Fact]
    public async Task Editing_loads_the_plan_and_saves_the_change()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var plan = _app.Plans.Seed("Old name");

        var form = await browser.GetStringAsync($"/Plans/Edit/{plan.Id}");
        var token = await browser.GetTokenAsync($"/Plans/Edit/{plan.Id}");
        var html = await FollowAsync(browser, await browser.PostFormAsync($"/Plans/Edit/{plan.Id}", token, PlanForm(("Name", "New name"))));

        Assert.Contains("Old name", form);
        Assert.Contains("Plan &quot;New name&quot; saved.", html);
        Assert.Equal("New name", plan.Name);
    }

    [Fact]
    public async Task The_plan_id_comes_from_the_route_and_unknown_plans_are_404()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var target = _app.Plans.Seed("Target");
        var other = _app.Plans.Seed("Other", order: 2);
        var token = await browser.GetTokenAsync($"/Plans/Edit/{target.Id}");

        await browser.PostFormAsync($"/Plans/Edit/{target.Id}", token, PlanForm(("Name", "Changed"), ("id", other.Id.ToString()), ("Id", other.Id.ToString())));
        var missingGet = await browser.GetAsync("/Plans/Edit/999");
        var missingPost = await browser.PostFormAsync("/Plans/Edit/999", token, PlanForm());

        Assert.Equal("Changed", target.Name);
        Assert.Equal("Other", other.Name);
        Assert.Equal(HttpStatusCode.NotFound, missingGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingPost.StatusCode);
    }

    [Fact]
    public async Task Plan_forms_require_the_anti_forgery_token()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);

        var response = await browser.PostAsync("/Plans/Create",
            new FormUrlEncodedContent(PlanForm().Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_app.Plans.Plans);
    }

    [Fact]
    public async Task Plan_text_is_html_encoded_on_the_list_and_the_edit_form()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);
        var plan = _app.Plans.Seed("<script>alert(1)</script>");
        plan.Tagline = "\"><img src=x onerror=alert(1)>";

        var list = await browser.GetStringAsync("/Plans");
        var edit = await browser.GetStringAsync($"/Plans/Edit/{plan.Id}");

        Assert.DoesNotContain("<script>alert(1)</script>", list + edit);
        Assert.DoesNotContain("<img src=x", list + edit);
    }
}
