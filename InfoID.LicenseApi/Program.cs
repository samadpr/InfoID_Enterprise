using System.Diagnostics;
using System.Net;
using System.Threading.RateLimiting;
using InfoID.LicenseApi;
using InfoID.Licensing.Core;
using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Services;
using InfoID.Shared.Contracts.Licensing;
using InfoID.Shared.Logging;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

// --- setup helper: `dotnet run -- --new-signing-key` prints a fresh key pair ---------
if (args.Contains("--new-signing-key"))
{
    var (privatePem, publicPem) = SignedLicense.CreateKeyPair();
    File.WriteAllText("license-signing-private.pem", privatePem);
    File.WriteAllText("license-signing-public.pem", publicPem);
    Console.WriteLine("Created license-signing-private.pem (SERVER ONLY, keep secret)");
    Console.WriteLine("Created license-signing-public.pem  (embed in InfoID)");
    return;
}

var builder = WebApplication.CreateBuilder(args);

// --- logging: daily file next to the API (logs\InfoID.LicenseApi-yyyy-MM-dd.log) plus the
// console. Folder and retention from "FileLogging" in appsettings.json. Never logged:
// activation codes, request codes, serial keys, passwords, emails, the license secret.
var logDirectory = builder.Configuration["FileLogging:Directory"] is { Length: > 0 } configuredLogDir
    ? (Path.IsPathRooted(configuredLogDir) ? configuredLogDir : Path.Combine(builder.Environment.ContentRootPath, configuredLogDir))
    : Path.Combine(builder.Environment.ContentRootPath, "logs");
var fileLog = new FileLoggerProvider(new FileLoggerOptions
{
    Directory = logDirectory,
    FilePrefix = "InfoID.LicenseApi",
    RetainDays = builder.Configuration.GetValue("FileLogging:RetainDays", 30),
    WriteToDebugOutput = false,
});
builder.Logging.AddProvider(fileLog);
var startupLog = fileLog.CreateLogger("InfoID.LicenseApi.Startup");

// A crash during startup (missing connection string, signing key...) or anywhere else
// that escapes is written to the file before the process ends.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    startupLog.LogCritical(e.ExceptionObject as Exception, "Unhandled exception; the License API is stopping");
    fileLog.Dispose();
};
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    startupLog.LogError(e.Exception, "Unobserved exception in a background task");
    e.SetObserved();
};

var connectionString = Environment.GetEnvironmentVariable("INFOID_LICENSE_DB")
                       ?? builder.Configuration.GetConnectionString("LicenseDb")
                       ?? throw new InvalidOperationException("No LicenseDb connection string configured.");

var signingKey = LicenseSigningKey.FromEnvironment()
                 ?? throw new InvalidOperationException(
                     $"No signing key. Set {LicenseSigningKey.FileVariable} to the path of license-signing-private.pem " +
                     "(create it with: dotnet run -- --new-signing-key).");

var secrets = LicenseSecretStore.FromEnvironment();
if (!secrets.IsConfigured)
{
    Console.WriteLine($"WARNING: {LicenseSecretStore.VariablePrefix}1 is not set -- the phone (QR) offline activation page will fail.");
    startupLog.LogWarning("{Variable}1 is not set: the phone (QR) offline activation page will fail.", LicenseSecretStore.VariablePrefix);
}

builder.Services.AddDbContextFactory<LicenseDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(signingKey);
builder.Services.AddSingleton(secrets);
// Per request: "who" for the audit log is the caller's IP address (no staff member).
builder.Services.AddScoped(sp => new AdminContext
{
    ClientAddress = sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Connection.RemoteIpAddress?.ToString(),
});
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<ActivationService>();

// Customer platform: accounts (email verification), plans, license requests.
var emailSender = EmailSenderFactory.FromEnvironment();
Console.WriteLine(emailSender.IsRealDelivery
    ? "Email: SMTP delivery."
    : "Email: TEST MODE - emails are saved as files in the 'mail-outbox' folder (set INFOID_SMTP_HOST for real delivery).");
startupLog.LogInformation(emailSender.IsRealDelivery ? "Email: SMTP delivery." : "Email: TEST MODE (emails saved to the mail-outbox folder).");
builder.Services.AddSingleton(emailSender);
builder.Services.AddScoped<CustomerAccountService>();
builder.Services.AddScoped<PlanService>();
builder.Services.AddScoped<LicenseCodeService>();
builder.Services.AddScoped<LicenseRequestService>();

// Trial printing for signed-in accounts without an activation code ("Trial" in appsettings).
var trialOptions = builder.Configuration.GetSection("Trial").Get<TrialOptions>() ?? new TrialOptions();
builder.Services.AddSingleton(trialOptions);
builder.Services.AddScoped<TrialService>();

// Brute-force protection: activation codes can't be guessed at scale.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = (int)HttpStatusCode.TooManyRequests;
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    // Sign-up, login, codes, requests: tighter, against guessing and spam.
    options.AddPolicy("account", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1) }));
    // Trial printing: status + reserve + commit per print job, possibly from several PCs
    // behind one office address.
    options.AddPolicy("trial", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("phone", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
AppLog.Configure(app.Services.GetRequiredService<ILoggerFactory>());

app.Lifetime.ApplicationStarted.Register(() =>
    startupLog.LogInformation("License API started ({Environment}) on {Urls}. Logs: {LogDirectory}",
        app.Environment.EnvironmentName, string.Join(", ", app.Urls), logDirectory));
app.Lifetime.ApplicationStopping.Register(() => startupLog.LogInformation("License API stopping."));
app.Lifetime.ApplicationStopped.Register(fileLog.Dispose);

// --- request log + safety net (first in the pipeline so it sees everything) ---------
// One line per request: method, path WITHOUT the query string (the QR page carries the
// request code in ?c=), status, time and caller IP. Request bodies are never logged.
// An unhandled exception is logged with its stack trace and the caller gets a plain
// 500 with a trace id to quote -- never the exception details.
var requestLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("InfoID.LicenseApi.Requests");
app.Use(async (context, next) =>
{
    var started = Stopwatch.GetTimestamp();
    var method = context.Request.Method;
    var path = LogText.PathOnly(context.Request.Path.Value);
    try
    {
        await next(context);
    }
    catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
    {
        requestLog.LogError(ex, "Unhandled error on {Method} {Path} (trace {TraceId})", method, path, context.TraceIdentifier);
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "The license server had a problem. Please try again later.", traceId = context.TraceIdentifier });
    }
    finally
    {
        var status = context.Response.StatusCode;
        var level = path == "/health" ? LogLevel.Debug
            : status >= 500 ? LogLevel.Error
            : status == StatusCodes.Status429TooManyRequests ? LogLevel.Warning
            : LogLevel.Information;
        requestLog.Log(level, "{Method} {Path} -> {Status} in {ElapsedMs:0} ms from {Ip}",
            method, path, status, Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
});

// Production: HTTPS only. Development: plain HTTP is also allowed, so a phone on the
// same Wi-Fi can open the QR page at http://<this PC's IP>:5043 (phones don't trust
// this PC's development certificate).
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

var api = app.MapGroup("/api/v1").RequireRateLimiting("api");

// FR-LIC-5: online activation.
api.MapPost("/activate", async (OnlineActivationRequest request, ActivationService activation) =>
{
    var result = await activation.ActivateOnlineAsync(request.ActivationCode, request.MachineFingerprint, request.AppVersion);
    return new OnlineActivationResponse { Success = result.Succeeded, License = result.License, ErrorMessage = result.Error };
});

// FR-LIC-13: periodic re-validation.
api.MapPost("/checkin", async (LicenseCheckInRequest request, ActivationService activation) =>
{
    var result = await activation.CheckInAsync(request.ActivationCode, request.MachineFingerprint, request.CardsIssuedThisPeriod, request.AppVersion);
    return new LicenseCheckInResponse { Status = result.Status, License = result.License, Message = result.Error };
});

// FR-LIC-10: deactivate / transfer.
api.MapPost("/deactivate", async (LicenseDeactivationRequest request, ActivationService activation) =>
{
    var (ok, error) = await activation.DeactivateOnlineAsync(request.ActivationCode, request.MachineFingerprint);
    return new LicenseDeactivationResponse { Success = ok, ErrorMessage = error };
});

// Offline activation by phone: InfoID's QR code opens /a?c=<request code>.
app.MapGet("/a", async (string? c, ActivationService activation) =>
{
    if (string.IsNullOrWhiteSpace(c))
        return Results.Content(PhonePage.Render(error: "This link is incomplete. Scan the QR code in InfoID again."), "text/html");

    var result = await activation.IssueForPhoneAsync(c);
    return Results.Content(result.Succeeded
        ? PhonePage.Render(serialKey: result.SerialKey, terms: result.Terms)
        : PhonePage.Render(error: result.Error), "text/html");
}).RequireRateLimiting("phone");

app.MapAccountEndpoints();

app.Run();
