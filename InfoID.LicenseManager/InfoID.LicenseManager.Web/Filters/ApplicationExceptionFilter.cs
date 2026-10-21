using InfoID.LicenseManager.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace InfoID.LicenseManager.Web.Filters;

/// <summary>
/// Turns the application layer's "this request should not have been made" exceptions into the right
/// HTTP status: a role that may not act becomes 403, an unknown (stale or guessed) id becomes 404. The
/// error page shows a fixed friendly message, never the exception text. Everything else is left to the
/// global exception handler, which logs it and shows a generic 500.
/// </summary>
public sealed class ApplicationExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ApplicationExceptionFilter> _logger;

    public ApplicationExceptionFilter(ILogger<ApplicationExceptionFilter> logger) => _logger = logger;

    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case ForbiddenException forbidden:
                // A security event: the UI never offers these actions to such a role, so someone sent the request by hand.
                _logger.LogWarning("Forbidden: {Message} ({Method} {Path})", forbidden.Message, context.HttpContext.Request.Method, context.HttpContext.Request.Path);
                context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
                context.ExceptionHandled = true;
                break;
            case NotFoundException notFound:
                _logger.LogInformation("Not found: {Message}", notFound.Message);
                context.Result = new StatusCodeResult(StatusCodes.Status404NotFound);
                context.ExceptionHandled = true;
                break;
        }
    }
}
