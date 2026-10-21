using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.Mappings;
using InfoID.LicenseManager.Web.Security;
using InfoID.LicenseManager.Web.ViewModels.Licenses;
using InfoID.Shared.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InfoID.LicenseManager.Web.Controllers;

/// <summary>Activation codes (FR-ADM-4, FR-ADM-7, FR-ADM-8). Every signed-in staff member may look;
/// changing anything needs the ManageLicenses policy, and the service re-checks the role itself.</summary>
public sealed class LicensesController : StaffController
{
    private readonly ILicenseService _licenses;
    private readonly TimeProvider _time;

    public LicensesController(ILicenseService licenses, TimeProvider time)
    {
        _licenses = licenses;
        _time = time;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] LicenseListQuery query, CancellationToken cancellationToken)
    {
        var rows = ModelState.IsValid ? await _licenses.SearchAsync(query.ToSearch(), cancellationToken) : [];
        return View(new LicenseListViewModel(query, rows));
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public IActionResult Create() =>
        View(new CreateLicenseViewModel { ExpiryDate = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime).AddYears(1) });

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> Create(CreateLicenseViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _licenses.CreateAsync(model.ToRequest(), cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error);
            return View(model);
        }

        var created = result.Value!;
        Flash($"Created {created.Codes.Count} activation code{(created.Codes.Count == 1 ? "" : "s")}.");
        return created.BatchId is { } batch
            ? RedirectToAction(nameof(Index), new { batchId = batch })
            : RedirectToAction(nameof(Details), new { id = created.FirstLicenseId });
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] long id, CancellationToken cancellationToken) =>
        View(new LicenseDetailsViewModel(await _licenses.GetDetailsAsync(id, cancellationToken)));

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> Revoke([FromRoute] long id, [Bind(Prefix = nameof(LicenseDetailsViewModel.Revoke))] RevokeFormViewModel form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return FlashInvalid(id);

        var result = await _licenses.RevokeAsync(new RevokeLicenseRequestDto(id, form.Reason), cancellationToken);
        Flash(result, "The license was revoked. It stops working at the customer's next online re-validation.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> Extend([FromRoute] long id, [Bind(Prefix = nameof(LicenseDetailsViewModel.Extend))] ExtendFormViewModel form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || form.NewExpiryDate is null) return FlashInvalid(id);

        var result = await _licenses.ExtendAsync(new ExtendLicenseRequestDto(id, form.NewExpiryDate.Value), cancellationToken);
        Flash(result, $"The expiry date is now {form.NewExpiryDate:yyyy-MM-dd}. Generate a new serial key for machines that activated offline.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> FreeSlots([FromRoute] long id, [Bind(Prefix = nameof(LicenseDetailsViewModel.FreeSlots))] FreeSlotsFormViewModel form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return FlashInvalid(id);

        var result = await _licenses.FreeMachineSlotsAsync(new FreeMachineSlotsRequestDto(id, form.Reason), cancellationToken);
        if (result.Succeeded)
            Flash(result.Value == 0
                ? "No machines were in use."
                : $"Freed {result.Value} machine slot(s). The customer can now activate on a new computer.");
        else
            Flash(result.Errors[0], isError: true);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> SetOffline([FromRoute] long id, bool allow, CancellationToken cancellationToken)
    {
        var result = await _licenses.SetOfflineActivationAsync(new SetOfflineActivationRequestDto(id, allow), cancellationToken);
        Flash(result, allow
            ? "This code can now also be activated offline (phone / QR)."
            : "This code can now only be activated online.");
        return RedirectToAction(nameof(Details), new { id });
    }

    private IActionResult FlashInvalid(long id)
    {
        Flash(FirstModelError(), isError: true);
        return RedirectToAction(nameof(Details), new { id });
    }

}
