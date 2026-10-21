using InfoID.LicenseManager.Application.DTOs.Dashboard;
using InfoID.LicenseManager.Application.Services;

namespace InfoID.LicenseManager.Web.ViewModels.Search;

public sealed class SearchViewModel
{
    public SearchViewModel(GlobalSearchResultDto result) => Result = result;

    public GlobalSearchResultDto Result { get; }

    public string Query => Result.Query;

    public bool TooShort => Query.Length < GlobalSearchService.MinLength;
}
