using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using InfoID.Desktop.Features.Welcome.Models;

namespace InfoID.Desktop.Features.Welcome.Services;

/// <summary>
/// Sample/in-memory implementation. Today the Cardholder/Template/Card persistence
/// layer has no "open documents" concept yet, so this returns an empty list -- the
/// Welcome page already renders a clean empty state for that case. Swap this out for a
/// repository-backed implementation once Card/Template documents can be reopened.
/// </summary>
public sealed class RecentCardService : IRecentCardService
{
    public Task<IReadOnlyList<RecentCardItem>> GetRecentCardsAsync()
    {
        IReadOnlyList<RecentCardItem> items = Array.Empty<RecentCardItem>();
        return Task.FromResult(items);
    }
}
