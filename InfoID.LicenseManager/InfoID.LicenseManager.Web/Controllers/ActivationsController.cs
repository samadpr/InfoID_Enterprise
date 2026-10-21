using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.Mappings;
using InfoID.LicenseManager.Web.ViewModels.Activations;
using Microsoft.AspNetCore.Mvc;

namespace InfoID.LicenseManager.Web.Controllers;

/// <summary>Serial Key Generation from a request (soft) code (FR-ADM-6): decode, check, optionally
/// override, generate. Open to every staff role; overrides need ManageLicenses, enforced by the service.</summary>
public sealed class ActivationsController : Controller
{
    private readonly IActivationService _activations;

    public ActivationsController(IActivationService activations) => _activations = activations;

    [HttpGet]
    public IActionResult Generate() => View(new GenerateSerialKeyViewModel());

    [HttpPost]
    public async Task<IActionResult> Decode(GenerateSerialKeyViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(nameof(Generate), model);

        var result = await _activations.PreviewAsync(model.SoftCode, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(model.SoftCode), result.Errors[0]);
            return View(nameof(Generate), model);
        }

        model.Preview = result.Value;
        return View(nameof(Generate), model);
    }

    [HttpPost]
    public async Task<IActionResult> Issue(GenerateSerialKeyViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(nameof(Generate), model);

        var result = await _activations.IssueSerialKeyAsync(model.ToRequest(), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
            // Show the decoded request again so staff can see why it was refused.
            var preview = await _activations.PreviewAsync(model.SoftCode, cancellationToken);
            model.Preview = preview.Value;
            return View(nameof(Generate), model);
        }

        model.Issued = result.Value;
        return View(nameof(Generate), model);
    }
}
