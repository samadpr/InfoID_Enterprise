using System.Net;
using System.Text.RegularExpressions;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Security;
using InfoID.LicenseManager.Tests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InfoID.LicenseManager.Tests.Web;

/// <summary>The real web app (routing, filters, cookies, views, headers) with the database-backed
/// repositories swapped for in-memory fakes, so page behaviour is tested without PostgreSQL.</summary>
public sealed class WebAppFactory : WebApplicationFactory<Program>
{
    public WebAppFactory()
    {
        Admins = new InMemoryAdminUserRepository();
        Audit = new InMemoryAuditLogRepository(Admins);
        AdminRepository = Admins;
        Licenses = new InMemoryLicenseRepository(UnitOfWork);
        Requests = new InMemoryLicenseRequestRepository(UnitOfWork) { Admins = Admins };
        Accounts = new InMemoryCustomerAccountRepository(Licenses, Requests);
        EmailCodes = new InMemoryEmailCodeRepository(Clock);
    }

    public InMemoryAdminUserRepository Admins { get; }
    public InMemoryAuditLogRepository Audit { get; }
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public InMemoryLicenseRepository Licenses { get; }
    public InMemoryCustomerRecordRepository Customers { get; } = new();
    public InMemoryBlacklistRepository Blacklist { get; } = new();
    public TestSecretProvider Secrets { get; set; } = new();
    public InMemoryActivationRepository Activations { get; } = new();
    public InMemoryPlanRepository Plans { get; } = new();
    public InMemoryLicenseRequestRepository Requests { get; }
    public InMemoryCustomerAccountRepository Accounts { get; }
    public InMemoryEmailCodeRepository EmailCodes { get; }
    public InMemoryTrialRepository Trials { get; } = new();
    public FakeEmailSender Email { get; } = new();
    public FakeLicenseTokenSigner Signer { get; } = new();

    /// <summary>Replaces the license signer (a test may swap in one that fails).</summary>
    public InfoID.LicenseManager.Application.Interfaces.Security.ILicenseTokenSigner? SignerOverride { get; set; }

    /// <summary>Everything the app logged, formatted, so tests can prove secrets never reach the log.</summary>
    public CapturedLogs Logs { get; } = new();
    public TestClock Clock { get; } = new();

    /// <summary>What the web app sees as its admin store (a test may swap in a failing one).</summary>
    public IAdminUserRepository AdminRepository { get; set; }

    public string EnvironmentName { get; set; } = "Development";

    /// <summary>Configuration values the app reads while it starts (set before the first request).</summary>
    public Dictionary<string, string> Settings { get; } = new();

    /// <summary>The address the test server reports as the caller's (the test transport has none).</summary>
    public IPAddress RemoteAddress { get; set; } = IPAddress.Parse("203.0.113.7");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);
        builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(() => RemoteAddress)));
        builder.ConfigureTestServices(services =>
        {
            Replace<IAdminUserRepository>(services, AdminRepository);
            Replace<IAuditLogRepository>(services, Audit);
            Replace<ILicenseRepository>(services, Licenses);
            Replace<IActivationRepository>(services, Activations);
            Replace<IUnitOfWork>(services, UnitOfWork);
            Replace<ICustomerRecordRepository>(services, Customers);
            Replace<IBlacklistRepository>(services, Blacklist);
            Replace<ILicenseSecretProvider>(services, Secrets);
            Replace<IPlanRepository>(services, Plans);
            Replace<ILicenseRequestRepository>(services, Requests);
            Replace<ICustomerAccountRepository>(services, Accounts);
            Replace<IEmailCodeRepository>(services, EmailCodes);
            Replace<ITrialRepository>(services, Trials);
            Replace<InfoID.LicenseManager.Application.Interfaces.Notifications.IEmailSender>(services, Email);
            Replace<ILicenseTokenSigner>(services, SignerOverride ?? Signer);
            services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(Logs);
            Replace<IPasswordHasher>(services, new FakePasswordHasher());
            Replace<TimeProvider>(services, Clock);
        });
    }

    private sealed class RemoteAddressFilter : IStartupFilter
    {
        private readonly Func<IPAddress> _address;

        public RemoteAddressFilter(Func<IPAddress> address) => _address = address;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = _address();
                return nextMiddleware();
            });
            next(app);
        };
    }

    private static void Replace<T>(IServiceCollection services, T instance) where T : class
    {
        services.RemoveAll<T>();
        services.AddSingleton(instance);
    }

    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });
}

public static partial class BrowserExtensions
{
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    /// <summary>Loads a page and returns its anti-forgery token (the matching cookie stays in the client's jar).</summary>
    public static async Task<string> GetTokenAsync(this HttpClient browser, string url)
    {
        var html = await browser.GetStringAsync(url);
        var match = TokenPattern().Match(html);
        Assert.True(match.Success, $"No anti-forgery token on {url}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient browser, string url, string token, params (string Key, string Value)[] fields)
    {
        var form = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();
        form.Add(new("__RequestVerificationToken", token));
        return await browser.PostAsync(url, new FormUrlEncodedContent(form));
    }

    public static async Task<HttpResponseMessage> SignInAsync(this HttpClient browser, string email, string password)
    {
        var token = await browser.GetTokenAsync("/Account/Login");
        var response = await browser.PostFormAsync("/Account/Login", token, ("Email", email), ("Password", password));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response;
    }
}

/// <summary>A logger provider that keeps every formatted message.</summary>
public sealed class CapturedLogs : Microsoft.Extensions.Logging.ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages.ToArray();

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly string _category;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages;

        public CapturingLogger(string category, System.Collections.Concurrent.ConcurrentQueue<string> messages)
        {
            _category = category;
            _messages = messages;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            _messages.Enqueue($"[{logLevel}] {_category}: {formatter(state, exception)}" + (exception is null ? "" : " :: " + exception.Message));
    }
}
