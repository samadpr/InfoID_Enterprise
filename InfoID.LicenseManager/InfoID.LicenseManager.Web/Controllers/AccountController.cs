using InfoID.LicenseManager.Application.DTOs.Auth;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.Security;
using InfoID.LicenseManager.Web.ViewModels.Account;
using InfoID.Shared.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InfoID.LicenseManager.Web.Controllers;

public sealed class AccountController : Controller
{
    private readonly IAuthService _auth;

    public AccountController(IAuthService auth) => _auth = auth;

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        if (!await _auth.HasAnyAdminAsync(cancellationToken)) return RedirectToAction(nameof(Setup));

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _auth.SignInAsync(new SignInRequestDto(model.Email, model.Password), cancellationToken);
        if (!result.Succeeded)
        {
            model.Password = "";
            ModelState.AddModelError(string.Empty, result.Errors[0]);
            return View(model);
        }

        await SignInAsync(result.Value!);
        return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction("Index", "Dashboard");
    }

    /// <summary>First run only: while the database has no staff account, anyone reaching the site may
    /// create the initial Super Admin; once one exists this page redirects to sign-in.</summary>
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Setup(CancellationToken cancellationToken)
    {
        if (await _auth.HasAnyAdminAsync(cancellationToken)) return RedirectToAction(nameof(Login));
        return View(new SetupViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Setup(SetupViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(ClearPasswords(model));

        var result = await _auth.CreateInitialSuperAdminAsync(
            new CreateInitialAdminRequestDto(model.FullName, model.Email, model.Password), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
            return View(ClearPasswords(model));
        }

        await SignInAsync(result.Value!);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _auth.RecordSignOutAsync(cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    private Task SignInAsync(AuthenticatedAdminDto admin) =>
        HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AdminPrincipalFactory.Create(admin),
            new AuthenticationProperties { IsPersistent = false });

    private static SetupViewModel ClearPasswords(SetupViewModel model)
    {
        model.Password = "";
        model.ConfirmPassword = "";
        return model;
    }
}
