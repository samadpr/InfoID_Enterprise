using InfoID.LicenseManager.Application.DTOs.Plans;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.Mappings;
using InfoID.LicenseManager.Web.Security;
using InfoID.LicenseManager.Web.ViewModels.Plans;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InfoID.LicenseManager.Web.Controllers;

/// <summary>Plans shown on the app Editions page. Every staff role may look; create and edit need ManageLicenses.
/// Changes reach every customer app at once, with no new release.</summary>
public sealed class PlansController : StaffController
{
    private readonly IPlanService _plans;

    public PlansController(IPlanService plans) => _plans = plans;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new PlanListViewModel(await _plans.GetAllAsync(cancellationToken)));

    [HttpGet]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var existing = await _plans.GetAllAsync(cancellationToken);
        return View(new PlanFormViewModel { SortOrder = existing.Count + 1 });
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> Create(PlanFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _plans.CreateAsync(model.ToInput(), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
            return View(model);
        }

        Flash($"Plan \"{result.Value!.Name}\" created. Customers see it the next time they open the Editions page.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> Edit([FromRoute] long id, CancellationToken cancellationToken)
    {
        ViewData["PlanId"] = id;
        return View((await _plans.GetAsync(id, cancellationToken)).ToForm());
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> Edit([FromRoute] long id, PlanFormViewModel model, CancellationToken cancellationToken)
    {
        ViewData["PlanId"] = id;
        if (!ModelState.IsValid) return View(model);

        var result = await _plans.UpdateAsync(new UpdatePlanRequestDto(id, model.ToInput()), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
            return View(model);
        }

        Flash($"Plan \"{result.Value!.Name}\" saved. Customers see the change the next time they open the Editions page.");
        return RedirectToAction(nameof(Index));
    }
}
