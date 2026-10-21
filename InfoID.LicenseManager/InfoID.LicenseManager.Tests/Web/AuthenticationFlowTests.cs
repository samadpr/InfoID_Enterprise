using System.Net;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Application.Interfaces.Repositories;

namespace InfoID.LicenseManager.Tests.Web;

public class AuthenticationFlowTests : IDisposable
{
    private const string Email = "boss@infoid.test";
    private const string Password = "Correct-Horse-9";

    private readonly WebAppFactory _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Anonymous_visitors_are_sent_to_the_login_page_with_a_return_url()
    {
        _app.Admins.Seed(Email, Password);
        using var browser = _app.CreateBrowser();

        var response = await browser.GetAsync("/Audit");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location!.AbsolutePath.TrimEnd('/'));
        Assert.Contains("ReturnUrl=%2FAudit", response.Headers.Location.Query);
    }

    [Fact]
    public async Task Without_any_administrator_the_login_page_leads_to_first_time_setup()
    {
        using var browser = _app.CreateBrowser();

        var response = await browser.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Setup", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Setup_is_closed_once_an_administrator_exists()
    {
        _app.Admins.Seed(Email, Password);
        using var browser = _app.CreateBrowser();

        var response = await browser.GetAsync("/Account/Setup");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Valid_credentials_sign_in_and_open_the_dashboard()
    {
        _app.Admins.Seed(Email, Password, fullName: "Bea Boss");
        _app.Licenses.Counts = new LicenseStatusCounts(Total: 4242, Available: 1, Active: 2, Revoked: 3);
        using var browser = _app.CreateBrowser();

        await browser.SignInAsync(Email, Password);
        var dashboard = await browser.GetAsync("/");
        var html = await dashboard.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains("4,242", html);
        Assert.Contains("Bea Boss", html);
        Assert.Contains("Sign out", html);
        Assert.Contains("Admin.SignedIn", _app.Audit.Rows.Select(r => r.Action));
    }

    [Fact]
    public async Task Wrong_password_shows_a_generic_message_and_no_session()
    {
        _app.Admins.Seed(Email, Password);
        using var browser = _app.CreateBrowser();

        var token = await browser.GetTokenAsync("/Account/Login");
        var response = await browser.PostFormAsync("/Account/Login", token, ("Email", Email), ("Password", "nope"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Email or password is incorrect.", html);
        Assert.DoesNotContain("nope", html);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Posting_without_an_antiforgery_token_is_rejected()
    {
        _app.Admins.Seed(Email, Password);
        using var browser = _app.CreateBrowser();

        var response = await browser.PostAsync("/Account/Login", new FormUrlEncodedContent(
            [new("Email", Email), new("Password", Password)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_app.Audit.Rows);
    }

    [Fact]
    public async Task Return_url_is_followed_only_when_it_is_local()
    {
        _app.Admins.Seed(Email, Password);

        using (var local = _app.CreateBrowser())
        {
            var token = await local.GetTokenAsync("/Account/Login");
            var response = await local.PostFormAsync("/Account/Login", token, ("Email", Email), ("Password", Password), ("ReturnUrl", "/Audit"));
            Assert.Equal("/Audit", response.Headers.Location!.OriginalString);
        }

        using (var hostile = _app.CreateBrowser())
        {
            var token = await hostile.GetTokenAsync("/Account/Login");
            var response = await hostile.PostFormAsync("/Account/Login", token, ("Email", Email), ("Password", Password), ("ReturnUrl", "https://evil.example/phish"));
            Assert.DoesNotContain("evil.example", response.Headers.Location!.OriginalString);
        }
    }

    [Fact]
    public async Task Signing_out_ends_the_session_and_is_audited()
    {
        _app.Admins.Seed(Email, Password);
        using var browser = _app.CreateBrowser();
        await browser.SignInAsync(Email, Password);

        var token = await browser.GetTokenAsync("/Audit");
        var logout = await browser.PostFormAsync("/Account/Logout", token);

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/")).StatusCode);
        Assert.Contains("Admin.SignedOut", _app.Audit.Rows.Select(r => r.Action));
    }

    [Fact]
    public async Task A_disabled_account_loses_its_session_immediately()
    {
        var admin = _app.Admins.Seed(Email, Password);
        using var browser = _app.CreateBrowser();
        await browser.SignInAsync(Email, Password);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/")).StatusCode);

        admin.Cancelled = true;

        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task A_role_change_forces_a_fresh_sign_in()
    {
        var admin = _app.Admins.Seed(Email, Password, AdminRoles.SuperAdmin);
        using var browser = _app.CreateBrowser();
        await browser.SignInAsync(Email, Password);

        admin.Role = AdminRoles.SupportAgent;

        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task First_time_setup_creates_the_super_admin_and_signs_in()
    {
        using var browser = _app.CreateBrowser();

        var token = await browser.GetTokenAsync("/Account/Setup");
        var response = await browser.PostFormAsync("/Account/Setup", token,
            ("FullName", "Ada Admin"), ("Email", "ada@infoid.test"), ("Password", "Str0ng-Passphrase"), ("ConfirmPassword", "Str0ng-Passphrase"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var admin = Assert.Single(_app.Admins.All);
        Assert.Equal(AdminRoles.SuperAdmin, admin.Role);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Setup_form_cannot_choose_a_role()
    {
        using var browser = _app.CreateBrowser();

        var token = await browser.GetTokenAsync("/Account/Setup");
        await browser.PostFormAsync("/Account/Setup", token,
            ("FullName", "Ada Admin"), ("Email", "ada@infoid.test"), ("Password", "Str0ng-Passphrase"), ("ConfirmPassword", "Str0ng-Passphrase"),
            ("Role", AdminRoles.SupportAgent), ("PasswordHash", "x"), ("Cancelled", "true"));

        var admin = Assert.Single(_app.Admins.All);
        Assert.Equal(AdminRoles.SuperAdmin, admin.Role);
        Assert.False(admin.Cancelled);
        Assert.NotEqual("x", admin.PasswordHash);
    }

    [Fact]
    public async Task Setup_with_mismatched_passwords_creates_nothing()
    {
        using var browser = _app.CreateBrowser();

        var token = await browser.GetTokenAsync("/Account/Setup");
        var response = await browser.PostFormAsync("/Account/Setup", token,
            ("FullName", "Ada Admin"), ("Email", "ada@infoid.test"), ("Password", "Str0ng-Passphrase"), ("ConfirmPassword", "different"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The passwords do not match.", await response.Content.ReadAsStringAsync());
        Assert.Empty(_app.Admins.All);
    }
}
