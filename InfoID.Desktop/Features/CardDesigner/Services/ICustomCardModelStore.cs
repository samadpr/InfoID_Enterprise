using System.Collections.Generic;
using System.Threading.Tasks;
using InfoID.Desktop.Features.CardDesigner.Models;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Persistence for user-saved custom card models. JSON-file-backed for now (consistent
/// with the existing CustomCardSizeStore pattern) -- swap for a DB-backed repository
/// once Module C gains a CustomCardModel entity, without changing callers.
/// </summary>
public interface ICustomCardModelStore
{
    Task<IReadOnlyList<CustomCardModel>> GetAllAsync();
    Task<CustomCardModel> SaveAsync(CustomCardModel model);
    Task DeleteAsync(string id);
}