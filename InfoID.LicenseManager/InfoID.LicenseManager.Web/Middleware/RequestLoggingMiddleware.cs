using System.Diagnostics;
using InfoID.Shared.Logging;

namespace InfoID.LicenseManager.Web.Middleware;

/// <summary>
/// One log line per request: method, path WITHOUT the query string (the phone page carries the request code in ?c=),
/// status, time and caller address. Request and response bodies are never logged, so activation codes, serial keys,
/// passwords and tokens cannot end up in the log.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
    {
        _next = next;
        _logger = loggerFactory.CreateLogger("InfoID.LicenseManager.Requests");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await _next(context);
        }
        finally
        {
            var path = LogText.PathOnly(context.Request.Path.Value);
            var status = context.Response.StatusCode;
            var level = path == "/health" ? LogLevel.Debug
                : status >= 500 ? LogLevel.Error
                : status == StatusCodes.Status429TooManyRequests ? LogLevel.Warning
                : LogLevel.Information;
            _logger.Log(level, "{Method} {Path} -> {Status} in {ElapsedMs:0} ms from {Ip}",
                context.Request.Method, path, status, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }
    }
}
