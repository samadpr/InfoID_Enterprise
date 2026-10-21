using System.Net;
using System.Security.Claims;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;
using InfoID.LicenseManager.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.LicenseManager.Tests.Web;

public class SecurityAndErrorTests
{
    [Fact]
    public async Task Pages_carry_defensive_headers_and_are_not_cacheable()
    {
        using var app = new WebAppFactory();
        app.Admins.Seed("boss@infoid.test", "Correct-Horse-9");
        using var browser = app.CreateBrowser();

        var response = await browser.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-store", response.Headers.GetValues("Cache-Control").Single());
    }

    [Fact]
    public async Task The_login_page_ships_no_inline_script_or_style()
    {
        using var app = new WebAppFactory();
        app.Admins.Seed("boss@infoid.test", "Correct-Horse-9");
        using var browser = app.CreateBrowser();

        var html = await browser.GetStringAsync("/Account/Login");

        // Scripts may only be loaded from our own files (the content-security policy forbids inline ones); styles are in site.css.
        var inline = System.Text.RegularExpressions.Regex.Matches(html, @"<script[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(m => m.Value).Where(tag => !tag.Contains(" src=")).ToList();
        Assert.True(inline.Count == 0, "Inline script tags: " + string.Join(" | ", inline));
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" onclick=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_session_cookie_is_http_only_and_same_site_strict()
    {
        using var app = new WebAppFactory();
        app.Admins.Seed("boss@infoid.test", "Correct-Horse-9");
        using var browser = app.CreateBrowser();

        var signIn = await browser.SignInAsync("boss@infoid.test", "Correct-Horse-9");

        var cookie = signIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("InfoID.Admin=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Anonymous_probing_of_unknown_pages_is_sent_to_login_rather_than_revealing_routes()
    {
        using var app = new WebAppFactory();
        app.Admins.Seed("boss@infoid.test", "Correct-Horse-9");
        using var browser = app.CreateBrowser();

        var response = await browser.GetAsync("/no/such/page");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Unknown_pages_show_a_friendly_404_without_internals()
    {
        using var app = new WebAppFactory();
        app.Admins.Seed("boss@infoid.test", "Correct-Horse-9");
        using var browser = app.CreateBrowser();
        await browser.SignInAsync("boss@infoid.test", "Correct-Horse-9");

        var response = await browser.GetAsync("/no/such/page");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", html);
    }

    [Fact]
    public async Task Unhandled_exceptions_show_a_generic_page_with_a_trace_id_and_no_details()
    {
        using var app = new WebAppFactory { EnvironmentName = "Production" };
        app.AdminRepository = new ThrowingAdminRepository(app.Admins) { Armed = true };
        using var browser = app.CreateBrowser();

        var response = await browser.GetAsync("/Account/Login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Something went wrong", html);
        Assert.Contains("Reference:", html);
        Assert.DoesNotContain("boom-secret-detail", html);
        Assert.DoesNotContain("ThrowingAdminRepository", html);
        Assert.DoesNotContain("   at ", html);
    }

    [Theory]
    [InlineData(AdminRoles.SuperAdmin, true)]
    [InlineData(AdminRoles.LicenseManager, true)]
    [InlineData(AdminRoles.SupportAgent, false)]
    public async Task Only_license_managers_and_super_admins_satisfy_the_manage_licenses_policy(string role, bool allowed)
    {
        using var app = new WebAppFactory();
        var authorization = app.Services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(PrincipalWithRole(role), null, AuthPolicies.ManageLicenses);

        Assert.Equal(allowed, result.Succeeded);
    }

    [Theory]
    [InlineData(AdminRoles.SuperAdmin, true)]
    [InlineData(AdminRoles.LicenseManager, false)]
    [InlineData(AdminRoles.SupportAgent, false)]
    public async Task Only_super_admins_satisfy_the_super_admin_policy(string role, bool allowed)
    {
        using var app = new WebAppFactory();
        var authorization = app.Services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(PrincipalWithRole(role), null, AuthPolicies.SuperAdmin);

        Assert.Equal(allowed, result.Succeeded);
    }

    [Fact]
    public async Task A_made_up_role_satisfies_no_policy()
    {
        using var app = new WebAppFactory();
        var authorization = app.Services.GetRequiredService<IAuthorizationService>();

        Assert.False((await authorization.AuthorizeAsync(PrincipalWithRole("Intruder"), null, AuthPolicies.ManageLicenses)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(PrincipalWithRole("Intruder"), null, AuthPolicies.SuperAdmin)).Succeeded);
    }

    private static ClaimsPrincipal PrincipalWithRole(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));

    /// <summary>Behaves like the real store until armed, then fails every call like a lost database connection.</summary>
    private sealed class ThrowingAdminRepository : IAdminUserRepository
    {
        private readonly IAdminUserRepository _inner;

        public ThrowingAdminRepository(IAdminUserRepository inner) => _inner = inner;

        public bool Armed { get; set; }

        private void FailIfArmed()
        {
            if (Armed) throw new InvalidOperationException("boom-secret-detail");
        }

        public Task<bool> AnyActiveAsync(CancellationToken cancellationToken = default)
        {
            FailIfArmed();
            return _inner.AnyActiveAsync(cancellationToken);
        }

        public Task<AdminUser?> FindActiveByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        {
            FailIfArmed();
            return _inner.FindActiveByEmailAsync(normalizedEmail, cancellationToken);
        }

        public Task<AdminUser?> FindActiveByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            FailIfArmed();
            return _inner.FindActiveByIdAsync(id, cancellationToken);
        }

        public Task<bool> TryAddFirstAdminAsync(AdminUser admin, CancellationToken cancellationToken = default)
        {
            FailIfArmed();
            return _inner.TryAddFirstAdminAsync(admin, cancellationToken);
        }
    }

    [Fact]
    public async Task A_failure_while_handling_a_post_is_still_a_generic_500_not_a_csrf_error()
    {
        using var app = new WebAppFactory { EnvironmentName = "Production" };
        app.Admins.Seed("boss@infoid.test", "Correct-Horse-9");
        var database = new ThrowingAdminRepository(app.Admins);
        app.AdminRepository = database;
        using var browser = app.CreateBrowser();
        var token = await browser.GetTokenAsync("/Account/Login");

        database.Armed = true;
        var response = await browser.PostFormAsync("/Account/Login", token, ("Email", "boss@infoid.test"), ("Password", "Correct-Horse-9"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Something went wrong", html);
        Assert.DoesNotContain("boom-secret-detail", html);
    }
}

public class LoginRateLimitTests
{
    [Fact]
    public async Task Repeated_login_posts_from_one_address_are_throttled()
    {
        using var app = new WebAppFactory();
        app.Admins.Seed("boss@infoid.test", "Correct-Horse-9");
        using var browser = app.CreateBrowser();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 12; i++)
        {
            var response = await browser.PostAsync("/Account/Login", new FormUrlEncodedContent([new("Email", "x"), new("Password", "y")]));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(10, statuses.Count(s => s == HttpStatusCode.BadRequest)); // reached MVC, rejected for the missing token
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
        Assert.Empty(app.Audit.Rows);
    }
}
