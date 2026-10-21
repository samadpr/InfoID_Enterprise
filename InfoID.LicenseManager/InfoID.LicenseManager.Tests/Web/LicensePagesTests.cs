using System.Net;
using InfoID.Licensing.Core;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Web;

public class LicensePagesTests : IDisposable
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

    private static (string, string)[] CreateForm(params (string, string)[] extra) =>
        new (string, string)[]
        {
            ("Quantity", "1"), ("Tier", "Professional"), ("Type", "Subscription"), ("ExpiryDate", TestData.Today.AddYears(1).AddDays(30).ToString("yyyy-MM-dd")),
            ("UnlimitedCards", "true"), ("MaxCardsPerPeriod", "1000"), ("LimitPeriod", "Monthly"), ("MaxActivations", "2"),
            ("OrganizationName", "Acme AS"), ("ContactEmail", "sales@acme.test"),
        }.Where(f => extra.All(e => e.Item1 != f.Item1)).Concat(extra).ToArray(); // extra fields replace or add

    // ---------------------------------------------------------------- list ----

    [Fact]
    public async Task The_list_shows_codes_and_the_search_reaches_the_service()
    {
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);
        _app.Licenses.Add(TestData.NewLicense(code: TestData.KnownCode));

        var html = await browser.GetStringAsync($"/Licenses?text={TestData.KnownCodeDisplay[..9]}&status=Available");

        Assert.Contains(TestData.KnownCodeDisplay, html);
        Assert.Equal(TestData.KnownCode[..8], _app.Licenses.LastSearch!.CodeFragment);
        Assert.Equal("Available", _app.Licenses.LastSearch.Status);
    }

    [Fact]
    public async Task Support_agents_see_no_create_button_but_managers_do()
    {
        using var support = await SignedInAsync(AdminRoles.SupportAgent);
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);

        Assert.DoesNotContain("Create codes", await support.GetStringAsync("/Licenses"));
        Assert.Contains("Create codes", await manager.GetStringAsync("/Licenses"));
    }

    [Fact]
    public async Task Typed_text_is_html_encoded_in_the_search_box()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);

        var html = await browser.GetStringAsync("/Licenses?text=%22%3E%3Cscript%3Ealert(1)%3C%2Fscript%3E");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    // ---------------------------------------------------------------- create ----

    [Fact]
    public async Task Support_agents_cannot_open_or_post_the_create_form()
    {
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);

        var form = await browser.GetAsync("/Licenses/Create");
        var token = await browser.GetTokenAsync("/Licenses");
        var post = await browser.PostFormAsync("/Licenses/Create", token, CreateForm());

        Assert.Equal(HttpStatusCode.Forbidden, form.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Empty(_app.Licenses.Codes);
    }

    [Fact]
    public async Task A_manager_creates_a_single_code_and_lands_on_its_page()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);

        var token = await browser.GetTokenAsync("/Licenses/Create");
        var response = await browser.PostFormAsync("/Licenses/Create", token, CreateForm());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var code = Assert.Single(_app.Licenses.Codes);
        Assert.Equal($"/Licenses/Details/{code.Id}", response.Headers.Location!.OriginalString);
        var details = await browser.GetStringAsync(response.Headers.Location.OriginalString);
        Assert.Contains("Created 1 activation code.", details);
        Assert.Contains("Acme AS", details);
        Assert.Contains("Professional", details);
    }

    [Fact]
    public async Task A_batch_redirects_to_a_list_of_that_batch_with_copyable_codes()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);

        var token = await browser.GetTokenAsync("/Licenses/Create");
        var response = await browser.PostFormAsync("/Licenses/Create", token, CreateForm(("Quantity", "4")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("batchId=", response.Headers.Location!.OriginalString);
        var html = await browser.GetStringAsync(response.Headers.Location.OriginalString);
        Assert.Equal(4, _app.Licenses.Codes.Count);
        Assert.All(_app.Licenses.Codes, c => Assert.Contains(c.ActivationCode[..4], html));
        Assert.Contains("Codes of this batch", html);
    }

    [Fact]
    public async Task Fields_the_form_does_not_offer_cannot_be_posted_to_set_privileged_values()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);

        var token = await browser.GetTokenAsync("/Licenses/Create");
        await browser.PostFormAsync("/Licenses/Create", token, CreateForm(
            ("ProductCode", "OTHERPRODUCT"), ("CustomerAccountId", "42"), ("Status", "Active"), ("CreatedByAdminUserId", "999"),
            ("IsUsed", "true"), ("Id", "999"), ("BatchId", Guid.NewGuid().ToString()), ("Cancelled", "true")));

        var code = Assert.Single(_app.Licenses.Codes);
        Assert.Equal(ProductCodes.InfoID, code.ProductCode);
        Assert.Null(code.CustomerAccountId);
        Assert.Equal(LicenseCodeStatus.Available, code.Status);
        Assert.Equal(_app.Admins.All[0].Id, code.CreatedByAdminUserId); // the signed-in staff member, not the posted value
        Assert.False(code.IsUsed);
        Assert.False(code.Cancelled);
        Assert.Null(code.BatchId);
    }

    [Fact]
    public async Task An_invalid_form_is_shown_again_with_the_messages_and_nothing_is_created()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);

        var token = await browser.GetTokenAsync("/Licenses/Create");
        var response = await browser.PostFormAsync("/Licenses/Create", token, CreateForm(("Quantity", "0"), ("ContactEmail", "nope")));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Quantity must be between 1 and 1000.", html);
        Assert.Empty(_app.Licenses.Codes);
    }

    [Fact]
    public async Task Create_without_an_antiforgery_token_is_rejected()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);

        var response = await browser.PostAsync("/Licenses/Create", new FormUrlEncodedContent(CreateForm().Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_app.Licenses.Codes);
    }

    // ---------------------------------------------------------------- details & actions ----

    [Fact]
    public async Task Details_of_an_unknown_code_show_a_friendly_404()
    {
        using var browser = await SignedInAsync(AdminRoles.LicenseManager);

        var response = await browser.GetAsync("/Licenses/Details/424242");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Action_forms_are_offered_to_managers_only()
    {
        var code = _app.Licenses.Add(TestData.NewLicense());
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);
        using var support = await SignedInAsync(AdminRoles.SupportAgent);

        var managerHtml = await manager.GetStringAsync($"/Licenses/Details/{code.Id}");
        var supportHtml = await support.GetStringAsync($"/Licenses/Details/{code.Id}");

        Assert.Contains("Revoke license", managerHtml);
        Assert.DoesNotContain("Revoke license", supportHtml);
        Assert.DoesNotContain("Extend", supportHtml.Replace("Extend it before", ""));
    }

    [Fact]
    public async Task A_support_agent_posting_an_action_by_hand_is_refused_and_nothing_changes()
    {
        var code = _app.Licenses.Add(TestData.NewLicense());
        using var support = await SignedInAsync(AdminRoles.SupportAgent);
        var token = await support.GetTokenAsync($"/Licenses/Details/{code.Id}");

        var revoke = await support.PostFormAsync($"/Licenses/Revoke/{code.Id}", token, ("Revoke.Reason", "because"));
        var extend = await support.PostFormAsync($"/Licenses/Extend/{code.Id}", token, ("Extend.NewExpiryDate", TestData.Today.AddYears(9).ToString("yyyy-MM-dd")));
        var free = await support.PostFormAsync($"/Licenses/FreeSlots/{code.Id}", token, ("FreeSlots.Reason", "because"));
        var offline = await support.PostFormAsync($"/Licenses/SetOffline/{code.Id}", token, ("allow", "true"));

        Assert.All(new[] { revoke, extend, free, offline }, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Equal(LicenseCodeStatus.Available, code.Status);
        Assert.Equal(TestData.Today.AddYears(1), code.ExpiryDate);
        Assert.False(code.AllowOfflineActivation);
    }

    [Fact]
    public async Task A_manager_revokes_with_a_reason_and_sees_the_result()
    {
        var code = _app.Licenses.Add(TestData.NewLicense());
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);
        var token = await manager.GetTokenAsync($"/Licenses/Details/{code.Id}");

        var response = await manager.PostFormAsync($"/Licenses/Revoke/{code.Id}", token, ("Revoke.Reason", "Chargeback"));
        var html = await manager.GetStringAsync(response.Headers.Location!.OriginalString);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(LicenseCodeStatus.Revoked, code.Status);
        Assert.Contains("The license was revoked.", html);
        Assert.Contains("LicenseCode.Revoked", _app.Audit.Rows.Select(r => r.Action));
    }

    [Fact]
    public async Task Revoking_without_a_reason_changes_nothing()
    {
        var code = _app.Licenses.Add(TestData.NewLicense());
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);
        var token = await manager.GetTokenAsync($"/Licenses/Details/{code.Id}");

        var response = await manager.PostFormAsync($"/Licenses/Revoke/{code.Id}", token, ("Revoke.Reason", ""));
        var html = await manager.GetStringAsync(response.Headers.Location!.OriginalString);

        Assert.Equal(LicenseCodeStatus.Available, code.Status);
        Assert.Contains("Enter a reason for revoking.", html);
    }

    [Fact]
    public async Task Another_license_cannot_be_reached_by_putting_its_id_in_the_form()
    {
        var mine = _app.Licenses.Add(TestData.NewLicense());
        var other = _app.Licenses.Add(TestData.NewLicense());
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);
        var token = await manager.GetTokenAsync($"/Licenses/Details/{mine.Id}");

        // The id comes from the route only; a body field called LicenseId / id is ignored.
        await manager.PostFormAsync($"/Licenses/Revoke/{mine.Id}", token, ("Revoke.Reason", "x"), ("LicenseId", other.Id.ToString()), ("id", other.Id.ToString()));

        Assert.Equal(LicenseCodeStatus.Revoked, mine.Status);
        Assert.Equal(LicenseCodeStatus.Available, other.Status);
    }

    [Fact]
    public async Task Extending_and_freeing_slots_work_end_to_end()
    {
        var code = _app.Licenses.Add(TestData.NewLicense(expiry: TestData.Today.AddMonths(1)));
        code.RegisterActivation(TestData.ActiveMachine("AA"), TestData.NowUtc);
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);
        var token = await manager.GetTokenAsync($"/Licenses/Details/{code.Id}");

        var newExpiry = TestData.Today.AddYears(3);
        await manager.PostFormAsync($"/Licenses/Extend/{code.Id}", token, ("Extend.NewExpiryDate", newExpiry.ToString("yyyy-MM-dd")));
        await manager.PostFormAsync($"/Licenses/FreeSlots/{code.Id}", token, ("FreeSlots.Reason", "New PC"));

        Assert.Equal(newExpiry, code.ExpiryDate);
        Assert.Empty(code.ActiveActivations);
        Assert.Equal(LicenseCodeStatus.Available, code.Status);
    }

    [Fact]
    public async Task Action_failures_are_shown_to_the_user_not_as_error_pages()
    {
        var code = _app.Licenses.Add(TestData.NewLicense(type: LicenseType.Perpetual));
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);
        var token = await manager.GetTokenAsync($"/Licenses/Details/{code.Id}");

        var response = await manager.PostFormAsync($"/Licenses/Extend/{code.Id}", token, ("Extend.NewExpiryDate", TestData.Today.AddYears(3).ToString("yyyy-MM-dd")));
        var html = await manager.GetStringAsync(response.Headers.Location!.OriginalString);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Perpetual licenses have no expiry date.", html);
    }

    [Fact]
    public async Task Actions_need_post_and_cannot_be_triggered_by_following_a_link()
    {
        var code = _app.Licenses.Add(TestData.NewLicense());
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);

        var response = await manager.GetAsync($"/Licenses/Revoke/{code.Id}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(LicenseCodeStatus.Available, code.Status);
    }
}
