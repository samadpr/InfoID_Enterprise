using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Web.ViewModels.Search;
using Microsoft.AspNetCore.Mvc;

namespace InfoID.LicenseManager.Web.Controllers;

/// <summary>The header search: one query across activation codes, customers, license requests and plans.
/// It only reads, through the same services (and role checks) as the lists it links to.</summary>
public sealed class SearchController : Controller
{
    private readonly IGlobalSearchService _search;

    public SearchController(IGlobalSearchService search) => _search = search;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? q, CancellationToken cancellationToken) =>
        View(new SearchViewModel(await _search.SearchAsync(q, cancellationToken)));
}
