using InfoID.LicenseManager.Application.DTOs.Customers;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.Security;
using InfoID.LicenseManager.Web.ViewModels.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InfoID.LicenseManager.Web.Controllers;

/// <summary>Customer accounts (sign-ups from the app): leads, their requests and licenses. Every staff role may
/// look and resend a verification code; disabling or enabling an account needs the ManageLicenses policy.</summary>
public sealed class CustomersController : StaffController
{
    private readonly ICustomerAdminService _customers;

    public CustomersController(ICustomerAdminService customers) => _customers = customers;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] CustomerListQuery query, CancellationToken cancellationToken)
    {
        var rows = ModelState.IsValid
            ? await _customers.SearchAsync(new CustomerSearchDto(query.Filter, query.Text), cancellationToken)
            : [];
        return View(new CustomerListViewModel(query, rows));
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] long id, CancellationToken cancellationToken) =>
        View(new CustomerDetailsViewModel(await _customers.GetDetailsAsync(id, cancellationToken)));

    [HttpPost]
    public async Task<IActionResult> ResendVerification([FromRoute] long id, CancellationToken cancellationToken)
    {
        Flash(await _customers.ResendVerificationAsync(id, cancellationToken), "A new verification code was sent to the customer.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> Disable([FromRoute] long id, [Bind(Prefix = nameof(CustomerDetailsViewModel.Disable))] DisableCustomerFormViewModel form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Flash(FirstModelError(), isError: true);
            return RedirectToAction(nameof(Details), new { id });
        }

        Flash(await _customers.SetDisabledAsync(new SetCustomerDisabledRequestDto(id, true, form.Reason), cancellationToken),
            "The account is disabled and signed out everywhere. Its e-mail stays reserved.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> Enable([FromRoute] long id, CancellationToken cancellationToken)
    {
        Flash(await _customers.SetDisabledAsync(new SetCustomerDisabledRequestDto(id, false, null), cancellationToken), "The account is enabled again.");
        return RedirectToAction(nameof(Details), new { id });
    }
}
