namespace InfoID.LicenseManager.Application.DTOs.Dashboard;

public sealed record DashboardSummaryDto(
    int TotalCodes,
    int AvailableCodes,
    int ActiveCodes,
    int RevokedCodes,
    int ActiveMachines,
    int ExpiringIn30Days,
    int ExpiringIn60Days,
    int ExpiringIn90Days,
    int ActivationsLast30Days);
