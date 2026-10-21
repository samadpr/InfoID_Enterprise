using InfoID.LicenseManager.Application.DTOs.Dashboard;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.Licensing.Core;

namespace InfoID.LicenseManager.Application.Services;

public sealed class DashboardAnalyticsService : IDashboardAnalyticsService
{
    public const int DefaultDays = 30;
    public const int MaxDays = 366;
    private const int TopCustomerCount = 5;
    private const int RecentActivityCount = 8;

    private readonly IDashboardService _summary;
    private readonly ILicenseRepository _licenses;
    private readonly IActivationRepository _activations;
    private readonly ILicenseRequestRepository _requests;
    private readonly ICustomerAccountRepository _accounts;
    private readonly IPlanRepository _plans;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;

    public DashboardAnalyticsService(
        IDashboardService summary,
        ILicenseRepository licenses,
        IActivationRepository activations,
        ILicenseRequestRepository requests,
        ICustomerAccountRepository accounts,
        IPlanRepository plans,
        IAuditService audit,
        TimeProvider time)
    {
        _summary = summary;
        _licenses = licenses;
        _activations = activations;
        _requests = requests;
        _accounts = accounts;
        _plans = plans;
        _audit = audit;
        _time = time;
    }

    public async Task<DashboardOverviewDto> GetOverviewAsync(DashboardRangeDto? range = null, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var (from, to) = Clamp(range, today);

        var summary = await _summary.GetSummaryAsync(cancellationToken);
        var lifecycle = await _licenses.CountLifecycleAsync(today, cancellationToken);

        var perDay = await _activations.CountActivationsPerDayAsync(
            from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), cancellationToken);
        var trend = FillDays(from, to, perDay);

        // The period just before this one, of the same length, so the dashboard can show a change only when there is something to compare with.
        var length = to.DayNumber - from.DayNumber + 1;
        var previous = await _activations.CountActivationsPerDayAsync(
            from.AddDays(-length).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), cancellationToken);

        var byStatus = await _requests.CountByStatusAsync(cancellationToken);
        int Requests(string status) => byStatus.TryGetValue(status, out var n) ? n : 0;

        var top = await _licenses.GetTopCustomersAsync(TopCustomerCount, cancellationToken);
        var recent = await _audit.GetRecentAsync(RecentActivityCount, cancellationToken);

        return new DashboardOverviewDto(
            summary,
            PendingRequests: Requests(LicenseRequestStatus.New) + Requests(LicenseRequestStatus.InProgress),
            NewRequests: Requests(LicenseRequestStatus.New),
            TotalCustomers: await _accounts.CountAsync(cancellationToken),
            ActivePlans: (await _plans.GetActiveAsync(ProductCodes.InfoID, cancellationToken)).Count,
            Lifecycle: new LicenseLifecycleDto(lifecycle.Available, lifecycle.Active, lifecycle.Expired, lifecycle.Revoked),
            From: from,
            To: to,
            ActivationTrend: trend,
            ActivationsInRange: trend.Sum(d => d.Count),
            ActivationsInPreviousRange: previous.Sum(d => d.Count),
            RequestsByStatus:
            [
                new(LicenseRequestStatus.New, Requests(LicenseRequestStatus.New)),
                new("In progress", Requests(LicenseRequestStatus.InProgress)),
                new(LicenseRequestStatus.Approved, Requests(LicenseRequestStatus.Approved)),
                new(LicenseRequestStatus.Rejected, Requests(LicenseRequestStatus.Rejected)),
            ],
            TopCustomers: top.Select(t => new TopCustomerDto(t.Name, t.Licenses, t.ActiveMachines)).ToList(),
            RecentActivity: recent);
    }

    public async Task<int> GetPendingRequestCountAsync(CancellationToken cancellationToken = default)
    {
        var byStatus = await _requests.CountByStatusAsync(cancellationToken);
        return byStatus.GetValueOrDefault(LicenseRequestStatus.New) + byStatus.GetValueOrDefault(LicenseRequestStatus.InProgress);
    }

    /// <summary>Never later than today, never longer than <see cref="MaxDays"/>, and an upside-down range falls back to the default window.</summary>
    public static (DateOnly From, DateOnly To) Clamp(DashboardRangeDto? range, DateOnly today)
    {
        if (range is null) return (today.AddDays(1 - DefaultDays), today);

        var to = range.To > today ? today : range.To;
        var from = range.From;
        if (from > to) from = to.AddDays(1 - DefaultDays);
        if (to.DayNumber - from.DayNumber + 1 > MaxDays) from = to.AddDays(1 - MaxDays);
        return (from, to);
    }

    private static List<DailyCountDto> FillDays(DateOnly from, DateOnly to, IReadOnlyList<DailyCount> counted)
    {
        var byDay = counted.GroupBy(c => c.Day).ToDictionary(g => g.Key, g => g.Sum(c => c.Count));
        var days = new List<DailyCountDto>(to.DayNumber - from.DayNumber + 1);
        for (var day = from; day <= to; day = day.AddDays(1))
            days.Add(new DailyCountDto(day, byDay.GetValueOrDefault(day)));
        return days;
    }
}
