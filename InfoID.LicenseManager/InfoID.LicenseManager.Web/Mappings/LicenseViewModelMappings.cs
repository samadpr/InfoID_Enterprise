using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.DTOs.Activations;
using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Web.ViewModels.Activations;
using InfoID.LicenseManager.Web.ViewModels.Licenses;

namespace InfoID.LicenseManager.Web.Mappings;

/// <summary>Maps web forms to application requests. Only whitelisted form fields are read.</summary>
public static class LicenseViewModelMappings
{
    public static CreateLicenseRequestDto ToRequest(this CreateLicenseViewModel form) => new(
        form.Quantity,
        form.Tier,
        form.Type,
        form.Type == LicenseType.Perpetual ? null : form.ExpiryDate,
        form.UnlimitedCards ? null : form.MaxCardsPerPeriod ?? 0,
        form.LimitPeriod,
        form.MaxActivations,
        form.AllowOfflineActivation,
        form.OrganizationName,
        form.OrganizationType,
        form.Country,
        form.ContactEmail,
        form.ContactPhone,
        form.Notes);

    public static LicenseSearchDto ToSearch(this LicenseListQuery query) => new(query.Text, query.Status, 500, query.BatchId);

    public static IssueSerialKeyRequestDto ToRequest(this GenerateSerialKeyViewModel form) => new(
        form.SoftCode,
        form.OverrideTier ? form.OverrideTierValue : null,
        form.OverrideExpiry ? form.OverrideExpiryDate : null);
}
