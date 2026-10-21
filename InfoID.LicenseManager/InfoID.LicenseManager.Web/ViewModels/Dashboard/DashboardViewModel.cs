using System.Globalization;
using System.Text.Json;
using InfoID.LicenseManager.Application.DTOs.Dashboard;
using InfoID.LicenseManager.Application.DTOs.Requests;

namespace InfoID.LicenseManager.Web.ViewModels.Dashboard;

/// <summary>Query-string choices of the dashboard: a preset window (7, 30 or 90 days) or a custom from/to.</summary>
public sealed class DashboardQuery
{
    public const string Seven = "7";
    public const string Thirty = "30";
    public const string Ninety = "90";
    public const string Custom = "custom";

    public string? Range { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    /// <summary>The window this query asks for, relative to today (UTC). Unknown values mean the default 30 days.</summary>
    public (string Key, DashboardRangeDto? Range) Resolve(DateOnly today)
    {
        switch (Range)
        {
            case Seven: return (Seven, new DashboardRangeDto(today.AddDays(-6), today));
            case Ninety: return (Ninety, new DashboardRangeDto(today.AddDays(-89), today));
            case Custom when From is not null && To is not null: return (Custom, new DashboardRangeDto(From.Value, To.Value));
            default: return (Thirty, null);
        }
    }
}

public sealed class DashboardViewModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public DashboardViewModel(DashboardOverviewDto overview, IReadOnlyList<LicenseRequestDto> pendingRequests, string rangeKey, string displayName, bool canManage)
    {
        Overview = overview;
        PendingRequests = pendingRequests;
        RangeKey = rangeKey;
        DisplayName = displayName;
        CanManage = canManage;
    }

    public DashboardOverviewDto Overview { get; }

    /// <summary>The oldest open requests first would hide new ones, so these are the most recent open ones.</summary>
    public IReadOnlyList<LicenseRequestDto> PendingRequests { get; }

    public string RangeKey { get; }

    public string DisplayName { get; }

    public bool CanManage { get; }

    public string FirstName => DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    public int Expiring0To30 => Overview.Summary.ExpiringIn30Days;

    public int Expiring31To60 => Math.Max(0, Overview.Summary.ExpiringIn60Days - Overview.Summary.ExpiringIn30Days);

    public int Expiring61To90 => Math.Max(0, Overview.Summary.ExpiringIn90Days - Overview.Summary.ExpiringIn60Days);

    public int RequestsTotal => Overview.RequestsByStatus.Sum(r => r.Count);

    public int RequestsMax => Overview.RequestsByStatus.Count == 0 ? 0 : Overview.RequestsByStatus.Max(r => r.Count);

    public int TopCustomersMax => Overview.TopCustomers.Count == 0 ? 0 : Overview.TopCustomers.Max(t => t.Licenses);

    /// <summary>Change of the activations compared with the period before; null when there is nothing before to compare with.</summary>
    public int? ActivationChangePercent =>
        Overview.ActivationsInPreviousRange > 0
            ? (int)Math.Round((Overview.ActivationsInRange - Overview.ActivationsInPreviousRange) * 100.0 / Overview.ActivationsInPreviousRange)
            : null;

    public string RangeLabel =>
        Overview.From == Overview.To ? Overview.From.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
        : Overview.From.ToString("d MMM yyyy", CultureInfo.InvariantCulture) + " - " + Overview.To.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public string TrendChartJson => Serialize(new
    {
        label = "Activations per day",
        unit = Overview.ActivationsInRange == 1 ? "activation" : "activations",
        emptyText = "No machines were activated in this period.",
        points = Overview.ActivationTrend.Select(d => new
        {
            label = d.Day.ToString("d MMM", CultureInfo.InvariantCulture),
            title = d.Day.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture),
            value = d.Count,
        }),
    });

    public IReadOnlyList<(string Label, int Value, int Series)> LifecycleSlices =>
    [
        ("Active", Overview.Lifecycle.Active, 3),
        ("Available", Overview.Lifecycle.Available, 2),
        ("Expired", Overview.Lifecycle.Expired, 4),
        ("Revoked", Overview.Lifecycle.Revoked, 1),
    ];

    public string LifecycleChartJson => Serialize(new
    {
        label = "Activation codes by status",
        totalLabel = "codes",
        emptyText = "No activation codes have been created yet.",
        items = LifecycleSlices.Select(s => new { label = s.Label, value = s.Value, series = s.Series }),
    });

    public string ExpiryChartJson => Serialize(new
    {
        label = "Licenses expiring soon",
        unit = "licenses",
        emptyText = "No license expires in the next 90 days.",
        items = new[]
        {
            new { label = "0-30 days", value = Expiring0To30, series = 1 },
            new { label = "31-60 days", value = Expiring31To60, series = 4 },
            new { label = "61-90 days", value = Expiring61To90, series = 2 },
        },
    });

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}
