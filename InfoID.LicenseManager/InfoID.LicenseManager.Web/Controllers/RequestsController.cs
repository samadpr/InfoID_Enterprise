using InfoID.LicenseManager.Application.DTOs.Requests;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.Mappings;
using InfoID.LicenseManager.Web.Security;
using InfoID.LicenseManager.Web.ViewModels.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InfoID.LicenseManager.Web.Controllers;

/// <summary>License requests inbox (FR-LIC plan requests from the app). Every staff role may look;
/// processing a request needs the ManageLicenses policy, and the service re-checks the role itself.</summary>
public sealed class RequestsController : StaffController
{
    private readonly ILicenseRequestService _requests;
    private readonly TimeProvider _time;

    public RequestsController(ILicenseRequestService requests, TimeProvider time)
    {
        _requests = requests;
        _time = time;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] RequestListQuery query, CancellationToken cancellationToken)
    {
        var rows = ModelState.IsValid
            ? await _requests.SearchAsync(new LicenseRequestSearchDto(query.Status, query.Text), cancellationToken)
            : [];
        return View(new RequestListViewModel(query, rows));
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] long id, CancellationToken cancellationToken)
    {
        var request = await _requests.GetAsync(id, cancellationToken);
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        return View(new RequestDetailsViewModel(request, request.ToApproveForm(today)));
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> MarkInProgress([FromRoute] long id, CancellationToken cancellationToken)
    {
        Flash(await _requests.MarkInProgressAsync(id, cancellationToken), "The request is marked as in progress.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> Approve([FromRoute] long id, [Bind(Prefix = nameof(RequestDetailsViewModel.Approve))] ApproveFormViewModel form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Flash(FirstModelError(), isError: true);
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _requests.ApproveAsync(form.ToRequest(id), cancellationToken);
        if (!result.Succeeded)
        {
            Flash(result.Errors[0], isError: true);
        }
        else if (result.Value!.EmailSent)
        {
            Flash("Approved. The activation code was created and e-mailed to the customer.");
        }
        else
        {
            Flash("Approved and the activation code was created, but the e-mail could not be sent. Pass the code on to the customer yourself.", isError: true);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageLicenses)]
    public async Task<IActionResult> Reject([FromRoute] long id, [Bind(Prefix = nameof(RequestDetailsViewModel.Reject))] RejectFormViewModel form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Flash(FirstModelError(), isError: true);
            return RedirectToAction(nameof(Details), new { id });
        }

        Flash(await _requests.RejectAsync(new RejectLicenseRequestDto(id, form.Reason), cancellationToken),
            "The request was rejected. The customer was e-mailed the reason.");
        return RedirectToAction(nameof(Details), new { id });
    }
}
