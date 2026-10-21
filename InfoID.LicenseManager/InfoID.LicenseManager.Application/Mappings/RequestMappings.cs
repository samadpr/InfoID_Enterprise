using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Requests;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Mappings;

internal static class RequestMappings
{
    public static LicenseRequestDto ToDto(this LicenseRequest r) => new(
        r.Id,
        r.CreatedDate,
        r.Status,
        r.Plan?.Name ?? "",
        r.Plan?.Tier ?? nameof(LicenseTier.Standard),
        r.Plan?.DurationMonths,
        r.Plan?.MaxCardsPerPeriod,
        r.Plan?.CardLimitPeriod ?? nameof(CardLimitPeriod.Monthly),
        r.Machines,
        r.FullName,
        r.CompanyName,
        r.Email,
        r.Phone,
        r.Country,
        r.StateProvince,
        r.Message,
        r.CustomerAccountId is not null,
        r.StatusReason,
        r.LicenseCode is { } code ? ActivationCodeFormat.Display(code.ActivationCode) : null,
        r.HandledByAdminUser?.FullName,
        r.HandledDate);
}
