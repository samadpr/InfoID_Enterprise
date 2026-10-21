using InfoID.Licensing.Core;
using System.Threading.RateLimiting;
using InfoID.LicenseManager.Application;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Trial;
using InfoID.LicenseManager.Application.Interfaces.Security;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Infrastructure;
using InfoID.LicenseManager.Infrastructure.Persistence;
using InfoID.LicenseManager.Web.Filters;
using InfoID.LicenseManager.Web.Middleware;
using InfoID.LicenseManager.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Net;


// Setup helpers (same switches as the original License API / License Manager):
//   dotnet run -- --new-signing-key   writes license-signing-private.pem (SERVER ONLY) and license-signing-public.pem (embed in InfoID)
//   dotnet run -- --new-secret        prints a random serial-key secret for INFOID_LICENSE_SECRET_V1
if (args.Contains("--new-signing-key"))
{
    var (privatePem, publicPem) = SignedLicense.CreateKeyPair();
    File.WriteAllText("license-signing-private.pem", privatePem);
    File.WriteAllText("license-signing-public.pem", publicPem);
    Console.WriteLine("Created license-signing-private.pem (SERVER ONLY, keep it secret and outside the repository)");
    Console.WriteLine("Created license-signing-public.pem  (embed in InfoID)");
    return;
}

if (args.Contains("--new-secret"))
{
    Console.WriteLine(LicenseSecret.GenerateBase64());
    return;
}


var builder = WebApplication.CreateBuilder(args);

// Connection string: INFOID_LICENSE_DB wins (so the password never has to live in a file),
// then ConnectionStrings:LicenseDb (dotnet user-secrets in development).
var connectionString = Environment.GetEnvironmentVariable("INFOID_LICENSE_DB")
                       ?? builder.Configuration.GetConnectionString("LicenseDb")
                       ?? throw new InvalidOperationException(
                           "No license database configured. Set INFOID_LICENSE_DB or ConnectionStrings:LicenseDb.");

builder.Services.AddControllersWithViews(options =>
{
    // CSRF protection on every POST/PUT/DELETE; GET stays side-effect free.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add<ApplicationExceptionFilter>();
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddScoped<InfoID.LicenseManager.Web.Ui.IPendingWorkProvider, InfoID.LicenseManager.Web.Ui.PendingWorkProvider>();
// Settings the services need: trial printing limits ("Trial" section) and who is told about new plan requests.
builder.Services.AddSingleton(builder.Configuration.GetSection("Trial").Get<TrialOptions>() ?? new TrialOptions());
builder.Services.AddSingleton(new SalesNotificationOptions { Email = builder.Configuration["INFOID_SALES_NOTIFY_EMAIL"] });
builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);

// Kestrel: do not advertise the server, and keep request bodies small (the client API and the forms send a few KB).
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 1024 * 1024;
});

// Sign-in cookies and anti-forgery tokens are protected with keys. Without a persistent location they live in the
// profile of the account running the site and are lost when it changes (everyone is signed out, open forms break).
var dataProtectionKeys = builder.Configuration["DataProtection:KeysPath"];
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("InfoID.LicenseManager");
if (!string.IsNullOrWhiteSpace(dataProtectionKeys))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeys));

// Behind a reverse proxy the caller's address and scheme arrive in X-Forwarded-* headers. They are honoured only from
// the proxies listed here: trusting them from anyone would let a client pick its own address and dodge the rate limits.
var knownProxies = builder.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? [];
if (knownProxies.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var proxy in knownProxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
    });
}

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

builder.Services.AddScoped<AdminCookieEvents>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "InfoID.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Error/403";
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = true;
        options.EventsType = typeof(AdminCookieEvents);
    });

builder.Services.AddAuthorization(options =>
{
    // Secure by default: every endpoint needs a signed-in staff member unless marked [AllowAnonymous].
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy(AuthPolicies.ManageLicenses, policy => policy.RequireAssertion(context =>
        context.User.Claims.Any(c => c.Type == System.Security.Claims.ClaimTypes.Role && AdminRoles.CanManageLicenses(c.Value))));
    options.AddPolicy(AuthPolicies.SuperAdmin, policy => policy.RequireRole(AdminRoles.SuperAdmin));
});

// Brute-force protection for sign-in and first-run setup (the account lockout is per account; this is per caller).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Login, httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));

    // Client API (InfoID.Desktop): same limits as the original License API. Activation codes cannot be guessed at scale.
    static void AddPerAddressPolicy(RateLimiterOptions options, string name, int permits) =>
        options.AddPolicy(name, httpContext => RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1) }));
    AddPerAddressPolicy(options, RateLimitPolicies.Api, 20);
    AddPerAddressPolicy(options, RateLimitPolicies.Account, 15);
    AddPerAddressPolicy(options, RateLimitPolicies.Trial, 60);
    AddPerAddressPolicy(options, RateLimitPolicies.Phone, 10);
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    // Opt-in: applies pending migrations to the configured database. Off by default so a
    // production schema only changes when an operator decides it should.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<LicenseManagerDbContext>().Database.MigrateAsync();
    // First run: the InfoID product and its three default plans (does nothing once they exist).
    await scope.ServiceProvider.GetRequiredService<InfoID.LicenseManager.Application.Interfaces.Services.IPlanService>().EnsureDefaultsAsync();
}

{
    var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("InfoID.LicenseManager.Startup");
    if (!app.Services.GetRequiredService<ILicenseTokenSigner>().IsConfigured)
        startupLog.LogWarning("No signing key: online activation will refuse requests. Set INFOID_LICENSE_SIGNING_KEY_FILE (create the key with: dotnet run -- --new-signing-key).");
    if (!app.Services.GetRequiredService<ILicenseSecretProvider>().IsConfigured)
        startupLog.LogWarning("INFOID_LICENSE_SECRET_V1 is not set: serial keys and the phone (QR) activation page will fail.");
    startupLog.LogInformation(app.Services.GetRequiredService<InfoID.LicenseManager.Application.Interfaces.Notifications.IEmailSender>().IsRealDelivery
        ? "E-mail: real delivery through the configured SMTP server."
        : "E-mail: no SMTP host configured (EmailSettings:Host), messages are written to the mail-outbox folder instead of being sent.");
    if (!app.Environment.IsDevelopment())
    {
        if (string.IsNullOrWhiteSpace(dataProtectionKeys))
            startupLog.LogWarning("DataProtection:KeysPath is not set: sign-in cookies and forms stop working whenever the key store is lost (restart of a container, new app-pool identity). Point it at a persistent, access-restricted folder.");
        if (app.Configuration["AllowedHosts"] is null or "*")
            startupLog.LogWarning("AllowedHosts allows any host name. Set it to the site's real host name(s), e.g. \"licenses.example.com\".");
    }
}

// Must run before anything that looks at the caller's address or scheme (rate limiting, HTTPS redirection, audit).
if (knownProxies.Length > 0) app.UseForwardedHeaders();

// First in the pipeline so it sees everything: one log line per request, and a JSON 500 for the client API.
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseWhen(context => ApiErrorMiddleware.AppliesTo(context.Request.Path), branch => branch.UseMiddleware<ApiErrorMiddleware>());

if (app.Environment.IsDevelopment())
{
    // The client API answers errors itself (ApiErrorMiddleware, JSON); only the staff site gets the developer page.
    app.UseWhen(context => !ApiErrorMiddleware.AppliesTo(context.Request.Path), branch => branch.UseDeveloperExceptionPage());
}
else
{
    app.UseWhen(context => !ApiErrorMiddleware.AppliesTo(context.Request.Path), branch => branch.UseExceptionHandler("/Error/500"));
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Friendly error pages for the staff site; the client API answers with bare status codes, as before.
app.UseWhen(context => !ApiErrorMiddleware.AppliesTo(context.Request.Path), branch => branch.UseStatusCodePagesWithReExecute("/Error/{0}"));
app.UseStaticFiles();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow })).AllowAnonymous();
app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();

/// <summary>Entry point marker so the test project can host the app with WebApplicationFactory.</summary>
public partial class Program;
