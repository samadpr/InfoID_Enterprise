using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.Features.Welcome.Models;

namespace InfoID.Desktop.Features.Welcome.Services;

/// <summary>
/// Backed by ICardDesignRepository -- the same Template rows the Card Designer saves
/// to. The Welcome page never touches Template/EF Core directly; it only knows this
/// interface, so nothing here needed to change on the ViewModel/View side.
/// </summary>
public sealed class RecentCardService : IRecentCardService
{
    private readonly ICardDesignRepository _cardDesignRepository;

    public RecentCardService(ICardDesignRepository cardDesignRepository)
    {
        _cardDesignRepository = cardDesignRepository;
    }

    public async Task<IReadOnlyList<RecentCardItem>> GetRecentCardsAsync()
    {
        var recent = await _cardDesignRepository.GetRecentAsync();

        return recent
            .Select(r => new RecentCardItem
            {
                Id = r.TemplateId.ToString(),
                Name = r.Name,
                Subtitle = $"{r.CardFormatLabel} \u2022 {FormatRelativeDate(r.LastOpenedDate ?? r.ModifiedDate)}",
                LastModified = r.ModifiedDate,
                IsPinned = r.IsPinned,
            })
            .ToList();
    }

    private static string FormatRelativeDate(DateTime date)
    {
        var span = DateTime.UtcNow - date;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        return date.ToLocalTime().ToString("MMM d, yyyy");
    }
}