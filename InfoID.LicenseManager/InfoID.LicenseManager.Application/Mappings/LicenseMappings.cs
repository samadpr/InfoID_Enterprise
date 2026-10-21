using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Mappings;

internal static class LicenseMappings
{
    public static LicenseDetailsDto ToDetailsDto(this LicenseCode license) => new(
        license.Id,
        ActivationCodeFormat.Display(license.ActivationCode),
        license.Tier,
        license.LicenseType,
        license.ExpiryDate,
        license.MaxCardsPerPeriod,
        license.CardLimitPeriod,
        license.Status,
        license.MaxActivations,
        license.ActiveActivations.Count(),
        license.AllowOfflineActivation,
        license.CustomerRecord?.OrganizationName,
        license.Notes,
        license.CreatedDate,
        license.RevokedDate,
        license.Activations
            .Where(a => !a.Cancelled)
            .OrderByDescending(a => a.Id)
            .Select(a => new LicenseActivationDto(
                a.Id, a.MachineFingerprint, a.ActivationMode, a.Status, a.ActivationDate, a.DeactivatedDate,
                a.LastValidationDate, a.IssuedTier, a.IssuedExpiryDate, a.AppVersion))
            .ToList());
}
