using System.Net;

namespace InfoID.LicenseManager.Tests.Web;

public class AuditPageTests : IDisposable
{
    private const string Email = "boss@infoid.test";
    private const string Password = "Correct-Horse-9";

    private readonly WebAppFactory _app = new();

    public void Dispose() => _app.Dispose();

    private async Task<HttpClient> SignedInBrowserAsync()
    {
        _app.Admins.Seed(Email, Password, fullName: "Bea Boss");
        var browser = _app.CreateBrowser();
        await browser.SignInAsync(Email, Password);
        return browser;
    }

    [Fact]
    public async Task The_audit_page_lists_entries_newest_first_with_html_encoded_details()
    {
        using var browser = await SignedInBrowserAsync();
        _app.Audit.Rows.Last().DetailsJson = "<script>alert(1)</script>";

        var html = await browser.GetStringAsync("/Audit");

        Assert.Contains("Admin.SignedIn", html);
        Assert.Contains("Bea Boss", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task Verifying_an_untouched_log_reports_it_intact()
    {
        using var browser = await SignedInBrowserAsync();

        var token = await browser.GetTokenAsync("/Audit");
        var verify = await browser.PostFormAsync("/Audit/Verify", token);
        var html = await browser.GetStringAsync(verify.Headers.Location!.OriginalString);

        Assert.Equal(HttpStatusCode.Redirect, verify.StatusCode);
        Assert.Contains("The audit log is intact", html);
    }

    [Fact]
    public async Task Verifying_a_tampered_log_names_the_broken_entry()
    {
        using var browser = await SignedInBrowserAsync();
        _app.Audit.Rows[0].Action = "Rewritten.By.Attacker";

        var token = await browser.GetTokenAsync("/Audit");
        var verify = await browser.PostFormAsync("/Audit/Verify", token);
        var html = await browser.GetStringAsync(verify.Headers.Location!.OriginalString);

        Assert.Contains("tampered with", html);
        Assert.Contains("entry #1", html);
    }

    [Fact]
    public async Task Verify_cannot_be_triggered_by_a_get_request()
    {
        using var browser = await SignedInBrowserAsync();

        var response = await browser.GetAsync("/Audit/Verify");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Single(_app.Audit.Rows); // only the sign-in; nothing was verified or logged
    }
}
