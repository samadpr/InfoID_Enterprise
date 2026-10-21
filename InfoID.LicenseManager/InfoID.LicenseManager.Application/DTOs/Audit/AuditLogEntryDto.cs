namespace InfoID.LicenseManager.Application.DTOs.Audit;

/// <summary>Filters of the audit log page. Dates are UTC days (both ends included); every filter is optional.</summary>
public sealed record AuditSearchDto(DateOnly? From = null, DateOnly? To = null, string? Who = null, string? Action = null, int Take = 200);

public sealed record AuditLogEntryDto(
    long Id,
    DateTime ActionDateUtc,
    string? AdminName,
    string Action,
    string? IpAddress,
    string? DetailsJson);
