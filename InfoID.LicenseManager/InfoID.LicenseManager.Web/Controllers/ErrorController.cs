using InfoID.LicenseManager.Web.ViewModels.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InfoID.LicenseManager.Web.Controllers;

/// <summary>The one place unhandled errors and error status codes end up. Shows a short, friendly
/// message and a trace id for support; the exception itself is only ever written to the log.</summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken] // error pages are re-executed with the failed request method (often POST); they change nothing
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class ErrorController : Controller
{
    private readonly ILogger<ErrorController> _logger;

    public ErrorController(ILogger<ErrorController> logger) => _logger = logger;

    [Route("Error/{code:int?}")]
    public IActionResult Index(int? code)
    {
        var status = code is >= 400 and <= 599 ? code.Value : StatusCodes.Status500InternalServerError;

        var exception = HttpContext.Features.Get<IExceptionHandlerFeature>();
        if (exception is not null)
            _logger.LogError(exception.Error, "Unhandled exception on {Path} (trace {TraceId})", exception.Path, HttpContext.TraceIdentifier);

        Response.StatusCode = status;
        var (title, message) = status switch
        {
            400 => ("Request not accepted", "The form could not be processed. It may have expired - go back, reload the page and try again."),
            403 => ("Access denied", "Your role does not allow this action."),
            404 => ("Page not found", "The page you asked for does not exist."),
            429 => ("Too many attempts", "Please wait a minute and try again."),
            _ => ("Something went wrong", "The license manager had a problem. Please try again; if it keeps happening, quote the trace id to support."),
        };

        return View("Error", new ErrorViewModel(status, title, message, HttpContext.TraceIdentifier));
    }
}
