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
    Task<IReadOnlyList<RecentCardItem>> GetRecentCardsAsync(int maxCount = 50);

    /// <summary>"Remove" a recent item: soft-deletes the database Template row for a
    /// database-backed item, or just forgets the file was ever opened (never deletes
    /// the actual file) for a file-backed one. Dispatches on RecentCardItem.Id's "file:"
    /// prefix, same scheme GetRecentCardsAsync itself uses.</summary>
    Task DeleteAsync(RecentCardItem item);

    Task TogglePinAsync(RecentCardItem item);
}
