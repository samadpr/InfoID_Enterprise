using System.Net;
using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Web;

public class SerialKeyPagesTests : IDisposable
{
    private const string Password = "Correct-Horse-9";
    private const string Machine = "CUSTOMER-PC-1";

    private readonly WebAppFactory _app = new();

    public void Dispose() => _app.Dispose();

    private async Task<HttpClient> SignedInAsync(string role)
    {
        var email = role.ToLowerInvariant() + "@infoid.test";
        _app.Admins.Seed(email, Password, role);
        var browser = _app.CreateBrowser();
        await browser.SignInAsync(email, Password);
        return browser;
    }

    private static string RequestCodeFor(LicenseCode license)
    {
        ActivationCode.TryParse(license.ActivationCode, out var code);
        return new SoftCode(code!, TestData.Fingerprint(Machine), TestData.Today).Encode();
    }

    [Fact]
    public async Task Decoding_a_request_shows_what_it_is_for_and_offers_the_generate_button()
    {
        var license = _app.Licenses.Add(TestData.NewLicense(LicenseTier.Enterprise, code: TestData.KnownCode));
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);

        var token = await browser.GetTokenAsync("/Activations/Generate");
        var response = await browser.PostFormAsync("/Activations/Decode", token, ("SoftCode", RequestCodeFor(license)));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(TestData.KnownCodeDisplay, html);
        Assert.Contains("Enterprise", html);
        Assert.Contains("Generate serial key", html);
        Assert.Empty(license.Activations);
    }

    [Fact]
    public async Task Generating_shows_a_key_that_validates_like_the_desktop_client_does()
    {
        var license = _app.Licenses.Add(TestData.NewLicense(LicenseTier.Professional, maxCards: 500));
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);

        var token = await browser.GetTokenAsync("/Activations/Generate");
        var response = await browser.PostFormAsync("/Activations/Issue", token, ("SoftCode", RequestCodeFor(license)));
        var html = await response.Content.ReadAsStringAsync();

        var key = Assert.Single(license.Activations).SerialKeyIssued!;
        Assert.Contains(key, html);
        ActivationCode.TryParse(license.ActivationCode, out var code);
        var validation = SerialKey.Validate(key, code!, TestData.Fingerprint(Machine), [TestSecretProvider.V1]);
        Assert.True(validation.IsValid, validation.Error);
        Assert.Equal(500, validation.Terms!.MaxCardsPerPeriod);
        Assert.Contains("SerialKey.Issued", _app.Audit.Rows.Select(r => r.Action));
    }

    [Fact]
    public async Task The_generate_page_is_not_cached_so_the_key_does_not_linger_in_the_browser()
    {
        var license = _app.Licenses.Add(TestData.NewLicense());
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);

        var token = await browser.GetTokenAsync("/Activations/Generate");
        var response = await browser.PostFormAsync("/Activations/Issue", token, ("SoftCode", RequestCodeFor(license)));

        Assert.Equal("no-store", response.Headers.GetValues("Cache-Control").Single());
    }

    [Fact]
    public async Task A_refused_request_explains_why_and_issues_nothing()
    {
        var license = _app.Licenses.Add(TestData.NewLicense());
        license.Revoke(TestData.NowUtc);
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);

        var token = await browser.GetTokenAsync("/Activations/Generate");
        var response = await browser.PostFormAsync("/Activations/Issue", token, ("SoftCode", RequestCodeFor(license)));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("This activation code has been revoked.", html);
        Assert.Empty(license.Activations);
    }

    [Fact]
    public async Task Garbage_instead_of_a_request_code_is_reported_on_the_form()
    {
        using var browser = await SignedInAsync(AdminRoles.SupportAgent);

        var token = await browser.GetTokenAsync("/Activations/Generate");
        var response = await browser.PostFormAsync("/Activations/Decode", token, ("SoftCode", "hello"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-error", html);
    }

    [Fact]
    public async Task Override_controls_are_shown_to_managers_only()
    {
        var license = _app.Licenses.Add(TestData.NewLicense());
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);
        using var support = await SignedInAsync(AdminRoles.SupportAgent);

        async Task<string> DecodeAsync(HttpClient browser)
        {
            var token = await browser.GetTokenAsync("/Activations/Generate");
            return await (await browser.PostFormAsync("/Activations/Decode", token, ("SoftCode", RequestCodeFor(license)))).Content.ReadAsStringAsync();
        }

        Assert.Contains("Override edition", await DecodeAsync(manager));
        Assert.DoesNotContain("Override edition", await DecodeAsync(support));
    }

    [Fact]
    public async Task A_support_agent_posting_override_fields_by_hand_is_refused_and_nothing_is_issued()
    {
        var license = _app.Licenses.Add(TestData.NewLicense(LicenseTier.Standard));
        using var support = await SignedInAsync(AdminRoles.SupportAgent);

        var token = await support.GetTokenAsync("/Activations/Generate");
        var response = await support.PostFormAsync("/Activations/Issue", token,
            ("SoftCode", RequestCodeFor(license)), ("OverrideTier", "true"), ("OverrideTierValue", "Enterprise"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(license.Activations);
    }

    [Fact]
    public async Task A_manager_can_override_the_edition_and_the_key_reflects_it()
    {
        var license = _app.Licenses.Add(TestData.NewLicense(LicenseTier.Standard));
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);

        var token = await manager.GetTokenAsync("/Activations/Generate");
        await manager.PostFormAsync("/Activations/Issue", token,
            ("SoftCode", RequestCodeFor(license)), ("OverrideTier", "true"), ("OverrideTierValue", "Enterprise"));

        Assert.Equal("Enterprise", Assert.Single(license.Activations).IssuedTier);
    }

    [Fact]
    public async Task Anonymous_visitors_cannot_use_the_generator()
    {
        using var browser = _app.CreateBrowser();
        _app.Admins.Seed("boss@infoid.test", Password);

        var response = await browser.GetAsync("/Activations/Generate");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location!.AbsolutePath);
    }

    // ---------------------------------------------------------------- simulator ----

    [Theory]
    [InlineData(AdminRoles.LicenseManager, HttpStatusCode.Forbidden)]
    [InlineData(AdminRoles.SupportAgent, HttpStatusCode.Forbidden)]
    [InlineData(AdminRoles.SuperAdmin, HttpStatusCode.OK)]
    public async Task The_simulator_is_for_super_admins_only(string role, HttpStatusCode expected)
    {
        using var browser = await SignedInAsync(role);

        Assert.Equal(expected, (await browser.GetAsync("/Simulator")).StatusCode);
    }

    [Fact]
    public async Task The_simulator_navigation_link_is_hidden_from_other_roles()
    {
        using var manager = await SignedInAsync(AdminRoles.LicenseManager);
        using var superAdmin = await SignedInAsync(AdminRoles.SuperAdmin);

        Assert.DoesNotContain("href=\"/Simulator\"", await manager.GetStringAsync("/"));
        Assert.Contains("href=\"/Simulator\"", await superAdmin.GetStringAsync("/"));
    }

    [Fact]
    public async Task The_simulator_makes_a_request_code_and_checks_a_key_end_to_end()
    {
        var license = _app.Licenses.Add(TestData.NewLicense(LicenseTier.Enterprise));
        var display = ActivationCodeFormat.Display(license.ActivationCode);
        using var browser = await SignedInAsync(AdminRoles.SuperAdmin);

        var token = await browser.GetTokenAsync("/Simulator");
        var request = await (await browser.PostFormAsync("/Simulator/RequestCode", token, ("ActivationCode", display), ("MachineName", "SIM-PC"))).Content.ReadAsStringAsync();
        Assert.Contains("Machine fingerprint", request);

        // The key the vendor side would hand out for that request.
        ActivationCode.TryParse(display, out var code);
        var key = SerialKey.Generate(license.ToTerms(), code!, TestData.Fingerprint("SIM-PC"), TestSecretProvider.V1);

        var verdict = await (await browser.PostFormAsync("/Simulator/Verify", token,
            ("ActivationCode", display), ("MachineName", "SIM-PC"), ("SerialKey", key))).Content.ReadAsStringAsync();
        Assert.Contains("InfoID would UNLOCK", verdict);
        Assert.Contains("Enterprise", verdict);
    }
}
