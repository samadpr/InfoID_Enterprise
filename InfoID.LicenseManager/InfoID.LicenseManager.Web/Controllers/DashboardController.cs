using InfoID.LicenseManager.Application.DTOs.Requests;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.Security;
using InfoID.LicenseManager.Web.ViewModels.Dashboard;
using Microsoft.AspNetCore.Mvc;

namespace InfoID.LicenseManager.Web.Controllers;

/// <summary>The operational overview. Every number is read from the database for this request; the date window of the
/// activation chart comes from the query string (7, 30, 90 days or a custom range) and is clamped by the service.</summary>
public sealed class DashboardController : Controller
{
    private const int PendingPreview = 5;

    private readonly IDashboardAnalyticsService _dashboard;
    private readonly ILicenseRequestService _requests;
    private readonly TimeProvider _time;

    public DashboardController(IDashboardAnalyticsService dashboard, ILicenseRequestService requests, TimeProvider time)
    {
        _dashboard = dashboard;
        _requests = requests;
        _time = time;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DashboardQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var (rangeKey, range) = query.Resolve(today);

        var overview = await _dashboard.GetOverviewAsync(range, cancellationToken);
        var pending = await _requests.SearchAsync(new LicenseRequestSearchDto(RequestStatusFilter.Open, null, PendingPreview), cancellationToken);

        return View(new DashboardViewModel(overview, pending, rangeKey, User.Identity?.Name ?? "", User.CanManageLicenses()));
    }
}
