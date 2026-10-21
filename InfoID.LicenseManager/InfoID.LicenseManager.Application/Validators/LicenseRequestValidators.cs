using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.LicenseManager.Application.DTOs.Requests;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Validators;

/// <summary>Input rules for the license forms. Each returns every problem found, as user-facing messages.</summary>
public static class LicenseRequestValidators
{
    public const int MaxBatchSize = 1000;
    public const int MaxReasonLength = 300;
    public const int MaxNotesLength = 2000;

    public static IReadOnlyList<string> Validate(CreateLicenseRequestDto request, DateOnly today)
    {
        var errors = new List<string>();

        if (request.Quantity is < 1 or > MaxBatchSize)
            errors.Add($"Quantity must be between 1 and {MaxBatchSize}.");
        errors.AddRange(ValidateTerms(request.Tier, request.Type, request.ExpiryDate, request.MaxCardsPerPeriod, request.LimitPeriod, request.MaxActivations, today));

        TooLong(errors, request.OrganizationName, 200, "Organization name");
        TooLong(errors, request.OrganizationType, 50, "Organization type");
        TooLong(errors, request.Country, 100, "Country");
        TooLong(errors, request.ContactEmail, 200, "Contact email");
        TooLong(errors, request.ContactPhone, 50, "Contact phone");
        TooLong(errors, request.Notes, MaxNotesLength, "Notes");
        if (!string.IsNullOrWhiteSpace(request.ContactEmail) && AdminAccountValidator.ValidateEmail(request.ContactEmail) is not null)
            errors.Add("Enter a valid contact email address, or leave it empty.");

        return errors;
    }

    /// <summary>The terms staff confirm when approving a plan request.</summary>
    public static IReadOnlyList<string> Validate(ApproveLicenseRequestDto request, DateOnly today)
    {
        var errors = new List<string>(ValidateTerms(request.Tier, request.Type, request.ExpiryDate, request.MaxCardsPerPeriod, request.LimitPeriod, request.MaxActivations, today));
        TooLong(errors, request.Notes, MaxNotesLength, "Notes");
        return errors;
    }

    /// <summary>The license terms every way of making a code must satisfy (same rules as the serial key format).</summary>
    public static IReadOnlyList<string> ValidateTerms(
        LicenseTier tier, LicenseType type, DateOnly? expiryDate, int? maxCardsPerPeriod, CardLimitPeriod limitPeriod, int maxActivations, DateOnly today)
    {
        var errors = new List<string>();

        if (maxActivations is < 1 or > LicenseCode.MaxMachinesPerCode)
            errors.Add($"Machines per code must be between 1 and {LicenseCode.MaxMachinesPerCode}.");
        if (!Enum.IsDefined(tier) || !Enum.IsDefined(type) || !Enum.IsDefined(limitPeriod))
            errors.Add("Choose a valid edition, license type and card-limit period.");

        var termsProblem = new SerialKeyTerms(tier, type, expiryDate, maxCardsPerPeriod, limitPeriod).Validate();
        if (termsProblem is not null) errors.Add(termsProblem);
        else if (expiryDate is { } expiry && expiry <= today) errors.Add("The expiry date must be in the future.");

        return errors;
    }

    public static string? ValidateReason(string? reason, string whatFor) =>
        string.IsNullOrWhiteSpace(reason) ? $"Enter a reason for {whatFor}."
        : reason.Trim().Length > MaxReasonLength ? $"The reason must be at most {MaxReasonLength} characters."
        : null;

    private static void TooLong(List<string> errors, string? value, int max, string label)
    {
        if (value?.Trim().Length > max) errors.Add($"{label} must be at most {max} characters.");
    }
}
