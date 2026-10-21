using InfoID.Shared.Logging;

namespace InfoID.LicenseManager.Web.Middleware;

/// <summary>
/// Safety net for the client API (<c>/api</c>) and the phone page (<c>/a</c>): an unhandled exception is logged with its
/// stack trace and the caller gets a plain 500 JSON body with a trace id to quote, as the original License API did,
/// never the exception details.
/// </summary>
public sealed class ApiErrorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger _logger;

    public ApiErrorMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
    {
        _next = next;
        _logger = loggerFactory.CreateLogger("InfoID.LicenseManager.Api");
    }

    public static bool AppliesTo(PathString path) => path.StartsWithSegments("/api") || path.Equals("/a");

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(ex, "Unhandled error on {Method} {Path} (trace {TraceId})",
                context.Request.Method, LogText.PathOnly(context.Request.Path.Value), context.TraceIdentifier);
            if (context.Response.HasStarted) throw;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "The license server had a problem. Please try again later.", traceId = context.TraceIdentifier });
        }
    }
}
