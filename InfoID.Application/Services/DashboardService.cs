using InfoID.Application.Dtos.Dashboard;
using InfoID.Application.Interfaces;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Common.Interfaces;
using InfoID.Domain.Entities.CardholderModule;
using InfoID.Domain.Entities.IssuanceModule;
using InfoID.Domain.Entities.PrintingModule;

namespace InfoID.Application.Services;

/// <summary>
/// Dashboard metrics (FR-DSH-1) from the tables Print history fills: PrintJob for cards
/// printed today / this month / failed runs, PrintJobItem for reprints, Card + Cardholder
/// for expiring cards and people with cards. Read-only.
///
/// "Cards" means physical cards (PrintJob.TotalCards = records x copies), the same number
/// the license counts, so the dashboard and the License page agree.
/// </summary>
public class DashboardService : IDashboardService
{
    /// <summary>Same marker PrintHistoryService uses for static (non-data) designs.</summary>
    private const string GenericCardholderRef = "__GENERIC_CARD__";

    private readonly IUnitOfWork _uow;

    public DashboardService(IUnitOfWork uow) => _uow = uow;

    public async Task<DashboardSummaryDto> GetSummaryAsync(DashboardQuery query, CancellationToken ct = default)
    {
        var result = new DashboardSummaryDto();
        var trendDays = Math.Clamp(query.TrendDays, 1, 90);
        var today = query.TodayLocalDate.Date;
        var trendStartLocal = today.AddDays(-(trendDays - 1));
        var trendStartUtc = DateTime.SpecifyKind(trendStartLocal - query.UtcOffset, DateTimeKind.Utc);

        // Everything from the earliest boundary we need, in one query.
        var from = query.MonthStartUtc < trendStartUtc ? query.MonthStartUtc : trendStartUtc;
        var jobs = await _uow.Repository<PrintJob>().FindAsync(j => j.CreatedDate >= from, ct);

        var monthJobs = jobs.Where(j => j.CreatedDate >= query.MonthStartUtc).ToList();
        var completedMonth = monthJobs.Where(j => j.Status == PrintJobStatus.Completed).ToList();
        var completedToday = completedMonth.Where(j => j.CreatedDate >= query.TodayStartUtc).ToList();

        result.CardsThisMonth = completedMonth.Sum(j => j.TotalCards);
        result.RunsThisMonth = completedMonth.Count;
        result.CardsToday = completedToday.Sum(j => j.TotalCards);
        result.RunsToday = completedToday.Count;
        result.FailedRunsThisMonth = monthJobs.Count(j => j.Status == PrintJobStatus.Failed);

        if (completedMonth.Count > 0)
        {
            var monthJobIds = completedMonth.Select(j => j.Id).ToList();
            result.ReprintsThisMonth = await _uow.Repository<PrintJobItem>()
                .CountAsync(i => monthJobIds.Contains(i.PrintJobId) && i.ReprintReasonCode != null, ct);
        }

        // Daily trend by LOCAL day.
        var perDay = jobs
            .Where(j => j.Status == PrintJobStatus.Completed)
            .GroupBy(j => (DateTime.SpecifyKind(j.CreatedDate, DateTimeKind.Utc) + query.UtcOffset).Date)
            .ToDictionary(g => g.Key, g => g.Sum(j => j.TotalCards));
        for (var d = trendStartLocal; d <= today; d = d.AddDays(1))
            result.DailyCards.Add(new DailyCountDto { Date = d, Cards = perDay.TryGetValue(d, out var n) ? n : 0 });

        // Cards still in use (Issued/Active) with an expiry date.
        var horizon = today.AddDays(Math.Max(0, query.ExpiringWithinDays));
        var cards = _uow.Repository<Card>();
        result.ExpiredCount = await cards.CountAsync(c =>
            (c.Status == CardStatus.Issued || c.Status == CardStatus.Active) &&
            c.ExpiryDate != null && c.ExpiryDate < today, ct);

        var expiring = (await cards.FindAsync(c =>
                (c.Status == CardStatus.Issued || c.Status == CardStatus.Active) &&
                c.ExpiryDate != null && c.ExpiryDate >= today && c.ExpiryDate <= horizon, ct))
            .OrderBy(c => c.ExpiryDate)
            .ToList();
        result.ExpiringSoonCount = expiring.Count;

        var shown = expiring.Take(Math.Max(1, query.MaxExpiringRows)).ToList();
        if (shown.Count > 0)
        {
            var holderIds = shown.Select(c => c.CardholderId).Distinct().ToList();
            var holders = (await _uow.Repository<Cardholder>().FindAsync(h => holderIds.Contains(h.Id), ct))
                .ToDictionary(h => h.Id);
            foreach (var card in shown)
            {
                holders.TryGetValue(card.CardholderId, out var holder);
                var expiry = card.ExpiryDate!.Value.Date;
                result.ExpiringCards.Add(new ExpiringCardDto
                {
                    CardNumber = card.CardNumber,
                    FullName = holder?.FullName ?? "",
                    PersonKey = holder?.ExternalRefId == GenericCardholderRef ? null : holder?.ExternalRefId,
                    ExpiryDate = expiry,
                    DaysLeft = (int)(expiry - today).TotalDays,
                });
            }
        }

        // People = cardholders (not the shared generic one) holding an issued/active card.
        var activeHolderIds = (await cards.FindAsync(c => c.Status == CardStatus.Issued || c.Status == CardStatus.Active, ct))
            .Select(c => c.CardholderId).Distinct().ToList();
        if (activeHolderIds.Count > 0)
        {
            result.PeopleWithCards = await _uow.Repository<Cardholder>().CountAsync(h =>
                activeHolderIds.Contains(h.Id) && (h.ExternalRefId == null || h.ExternalRefId != GenericCardholderRef), ct);
        }
        result.TotalCardsRecorded = await cards.CountAsync(null, ct);

        return result;
    }
}
