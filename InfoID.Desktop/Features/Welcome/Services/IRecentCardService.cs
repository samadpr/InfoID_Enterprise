using System.Collections.Generic;
using System.Threading.Tasks;
using InfoID.Desktop.Features.Welcome.Models;

namespace InfoID.Desktop.Features.Welcome.Services;

/// <summary>
/// Abstraction over "recently opened cards". The Welcome page depends only on this
/// interface, never on sample data directly, so a database-backed implementation can
/// replace <see cref="RecentCardService"/> later without touching the ViewModel/View.
/// </summary>
public interface IRecentCardService
{
    Task<IReadOnlyList<RecentCardItem>> GetRecentCardsAsync();
}
