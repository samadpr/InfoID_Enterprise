using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.Security;
using InfoID.LicenseManager.Web.ViewModels.Simulator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InfoID.LicenseManager.Web.Controllers;

/// <summary>Test tool for Super Admins: plays the customer's side of offline activation. Changes nothing in the database.</summary>
[Authorize(Policy = AuthPolicies.SuperAdmin)]
public sealed class SimulatorController : Controller
{
    private readonly IActivationSimulatorService _simulator;

    public SimulatorController(IActivationSimulatorService simulator) => _simulator = simulator;

    [HttpGet]
    public IActionResult Index() => View(new SimulatorViewModel { SecretConfigured = _simulator.IsSecretConfigured });

    [HttpPost]
    public IActionResult RequestCode(SimulatorViewModel model)
    {
        model.SecretConfigured = _simulator.IsSecretConfigured;
        if (!ModelState.IsValid) return View(nameof(Index), model);

        var result = _simulator.CreateRequestCode(model.ActivationCode, model.MachineName);
        if (result.Succeeded) model.Request = result.Value;
        else model.Error = result.Errors[0];
        return View(nameof(Index), model);
    }

    [HttpPost]
    public IActionResult Verify(SimulatorViewModel model)
    {
        model.SecretConfigured = _simulator.IsSecretConfigured;
        if (!ModelState.IsValid) return View(nameof(Index), model);

        var result = _simulator.VerifySerialKey(model.ActivationCode, model.MachineName, model.SerialKey);
        if (result.Succeeded) model.Verification = result.Value;
        else model.Error = result.Errors[0];
        return View(nameof(Index), model);
    }
}
