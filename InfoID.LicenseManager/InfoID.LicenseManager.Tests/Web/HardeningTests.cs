using System.Net;

namespace InfoID.LicenseManager.Tests.Web;

/// <summary>Deployment-facing security settings: response headers, reverse-proxy trust and the rate limiter's view of the caller.</summary>
public class HardeningTests
{
    private static Task<HttpResponseMessage> PostLoginAsync(HttpClient browser, string? forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login") { Content = new FormUrlEncodedContent([new("Email", "x"), new("Password", "y")]) };
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return browser.SendAsync(request);
    }

    [Fact]
    public async Task Pages_opt_out_of_camera_microphone_location_and_cross_origin_use()
    {
        using var app = new WebAppFactory();
        using var browser = app.CreateBrowser();

        var response = await browser.GetAsync("/Account/Login");

        Assert.Equal("camera=(), microphone=(), geolocation=(), payment=(), usb=()", response.Headers.GetValues("Permissions-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Forwarded_headers_are_ignored_unless_the_proxy_is_configured()
    {
        using var app = new WebAppFactory();
        using var browser = app.CreateBrowser();
        var statuses = new List<HttpStatusCode>();

        // A client that invents a different address per request must still hit one limit.
        for (var i = 0; i < 12; i++) statuses.Add((await PostLoginAsync(browser, $"198.51.100.{i + 1}")).StatusCode);

        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task A_configured_proxy_gives_every_real_client_its_own_limit()
    {
        using var app = new WebAppFactory { RemoteAddress = IPAddress.Parse("10.0.0.5") };
        app.Settings["Proxy:KnownProxies:0"] = "10.0.0.5";
        using var browser = app.CreateBrowser();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 12; i++) statuses.Add((await PostLoginAsync(browser, $"198.51.100.{i + 1}")).StatusCode);

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task A_configured_proxy_still_throttles_one_client_that_keeps_trying()
    {
        using var app = new WebAppFactory { RemoteAddress = IPAddress.Parse("10.0.0.5") };
        app.Settings["Proxy:KnownProxies:0"] = "10.0.0.5";
        using var browser = app.CreateBrowser();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 12; i++) statuses.Add((await PostLoginAsync(browser, "198.51.100.9")).StatusCode);

        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task Forwarded_headers_from_a_machine_that_is_not_the_configured_proxy_are_ignored()
    {
        using var app = new WebAppFactory { RemoteAddress = IPAddress.Parse("192.0.2.66") };
        app.Settings["Proxy:KnownProxies:0"] = "10.0.0.5";
        using var browser = app.CreateBrowser();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 12; i++) statuses.Add((await PostLoginAsync(browser, $"198.51.100.{i + 1}")).StatusCode);

        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}
