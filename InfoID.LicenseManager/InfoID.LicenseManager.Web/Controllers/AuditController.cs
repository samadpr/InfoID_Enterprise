using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.ViewModels.Audit;
using Microsoft.AspNetCore.Mvc;

namespace InfoID.LicenseManager.Web.Controllers;

public sealed class AuditController : Controller
{
    private const string ChainMessageKey = "AuditChainMessage";
    private const string ChainIntactKey = "AuditChainIntact";

    private readonly IAuditService _audit;

    public AuditController(IAuditService audit) => _audit = audit;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AuditQuery query, CancellationToken cancellationToken)
    {
        var entries = ModelState.IsValid ? await _audit.SearchAsync(query.ToSearch(), cancellationToken) : [];
        return View(new AuditLogViewModel(
            entries,
            query,
            TempData[ChainMessageKey] as string,
            TempData[ChainIntactKey] is true));
    }

    [HttpPost]
    public async Task<IActionResult> Verify(CancellationToken cancellationToken)
    {
        var brokenRowId = await _audit.VerifyChainAsync(cancellationToken);
        TempData[ChainIntactKey] = brokenRowId is null;
        TempData[ChainMessageKey] = brokenRowId is null
            ? "The audit log is intact: every entry matches its hash and links to the one before it."
            : $"The audit log has been tampered with. The chain breaks at entry #{brokenRowId}.";
        return RedirectToAction(nameof(Index));
    }
}
