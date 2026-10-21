using InfoID.LicenseManager.Application.DTOs.Audit;

namespace InfoID.LicenseManager.Application.DTOs.Dashboard;

/// <summary>The period the activation chart covers (UTC days, both ends included). The service clamps it to a sensible window.</summary>
public sealed record DashboardRangeDto(DateOnly From, DateOnly To);

public sealed record DailyCountDto(DateOnly Day, int Count);

public sealed record LabelCountDto(string Label, int Count);

public sealed record TopCustomerDto(string Name, int Licenses, int ActiveMachines);

/// <summary>Activation codes split into buckets that never overlap and add up to the total.</summary>
public sealed record LicenseLifecycleDto(int Available, int Active, int Expired, int Revoked);

/// <summary>Everything the dashboard shows, read from the database for this request (nothing estimated).</summary>
public sealed record DashboardOverviewDto(
    DashboardSummaryDto Summary,
    int PendingRequests,
    int NewRequests,
    int TotalCustomers,
    int ActivePlans,
    LicenseLifecycleDto Lifecycle,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DailyCountDto> ActivationTrend,
    int ActivationsInRange,
    int ActivationsInPreviousRange,
    IReadOnlyList<LabelCountDto> RequestsByStatus,
    IReadOnlyList<TopCustomerDto> TopCustomers,
    IReadOnlyList<AuditLogEntryDto> RecentActivity);
