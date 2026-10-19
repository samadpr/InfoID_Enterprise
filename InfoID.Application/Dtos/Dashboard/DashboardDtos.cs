namespace InfoID.Application.Dtos.Dashboard;

/// <summary>Time boundaries for the dashboard, supplied by the caller because "today"
/// and "this month" depend on the operator's local time zone (the database stores UTC).</summary>
public class DashboardQuery
{
    /// <summary>Start of today (local midnight) in UTC.</summary>
    public DateTime TodayStartUtc { get; set; }

    /// <summary>Start of this month (local, 1st 00:00) in UTC.</summary>
    public DateTime MonthStartUtc { get; set; }

    /// <summary>Today's local date. Card expiry dates are stored as plain dates.</summary>
    public DateTime TodayLocalDate { get; set; }

    /// <summary>Cards expiring within this many days count as "expiring soon".</summary>
    public int ExpiringWithinDays { get; set; } = 30;

    /// <summary>Days in the daily-cards trend (oldest first, ending today).</summary>
    public int TrendDays { get; set; } = 14;

    /// <summary>UTC offset of the operator's time zone, for grouping the trend by local day.</summary>
    public TimeSpan UtcOffset { get; set; }

    /// <summary>Maximum rows in the expiring-cards list.</summary>
    public int MaxExpiringRows { get; set; } = 50;
}

/// <summary>Card and printing metrics for the dashboard (FR-DSH-1), computed from Print history.</summary>
public class DashboardSummaryDto
{
    /// <summary>Physical cards from successful print runs since local midnight.</summary>
    public int CardsToday { get; set; }
    public int RunsToday { get; set; }

    /// <summary>Physical cards from successful print runs this month.</summary>
    public int CardsThisMonth { get; set; }
    public int RunsThisMonth { get; set; }

    /// <summary>Cards printed this month that replaced an earlier card.</summary>
    public int ReprintsThisMonth { get; set; }

    /// <summary>Print runs this month that failed.</summary>
    public int FailedRunsThisMonth { get; set; }

    /// <summary>Issued/active cards whose expiry falls within the next N days (including today).</summary>
    public int ExpiringSoonCount { get; set; }

    /// <summary>Issued/active cards whose expiry date has already passed.</summary>
    public int ExpiredCount { get; set; }

    /// <summary>People with at least one issued/active card (generic static cards excluded).</summary>
    public int PeopleWithCards { get; set; }

    /// <summary>Total cards ever recorded (all statuses).</summary>
    public int TotalCardsRecorded { get; set; }

    public List<ExpiringCardDto> ExpiringCards { get; set; } = new();

    /// <summary>One entry per local day, oldest first, ending today.</summary>
    public List<DailyCountDto> DailyCards { get; set; } = new();
}

public class ExpiringCardDto
{
    public string CardNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PersonKey { get; set; }
    public DateTime ExpiryDate { get; set; }
    /// <summary>Negative when already expired.</summary>
    public int DaysLeft { get; set; }
}

public class DailyCountDto
{
    public DateTime Date { get; set; }
    public int Cards { get; set; }
}
