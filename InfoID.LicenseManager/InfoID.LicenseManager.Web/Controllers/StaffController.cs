using InfoID.Shared.Common;
using Microsoft.AspNetCore.Mvc;

namespace InfoID.LicenseManager.Web.Controllers;

/// <summary>Base of the staff pages: one-shot "flash" messages shown after a redirect (the layout renders them).</summary>
public abstract class StaffController : Controller
{
    private const string FlashKey = "Flash";
    private const string FlashErrorKey = "FlashIsError";

    protected void Flash(string message, bool isError = false)
    {
        TempData[FlashKey] = message;
        TempData[FlashErrorKey] = isError;
    }

    /// <summary>Flashes <paramref name="success"/> when the operation worked, otherwise its first error.</summary>
    protected void Flash(Result result, string success)
    {
        if (result.Succeeded) Flash(success);
        else Flash(result.Errors[0], isError: true);
    }

    /// <summary>The first validation message of the posted form, for redirect-style pages that cannot redisplay it.</summary>
    protected string FirstModelError() =>
        ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
        ?? "Check the form and try again.";
}
