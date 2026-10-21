using System.Globalization;
using InfoID.LicenseManager.Application.DTOs.Activations;

namespace InfoID.LicenseManager.Web.ViewModels.Phone;

public sealed class PhoneActivationViewModel
{
    private PhoneActivationViewModel()
    {
    }

    public string? SerialKey { get; private init; }

    /// <summary>One sentence describing the license, e.g. "Professional license, valid until 01 Jan 2027, 500 cards per month."</summary>
    public string? Description { get; private init; }

    public string? Error { get; private init; }

    public static PhoneActivationViewModel Failure(string error) => new() { Error = error };

    public static PhoneActivationViewModel Success(SerialKeyIssuedDto issued) => new()
    {
        SerialKey = issued.SerialKey,
        Description = $"{issued.Tier} license" +
                      (issued.ExpiryDate is { } expiry ? $", valid until {expiry.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}" : ", no expiry") +
                      (issued.MaxCardsPerPeriod is { } max
                          ? $", {max.ToString("N0", CultureInfo.InvariantCulture)} cards per {issued.LimitPeriod.ToString().ToLowerInvariant()[..^2]}."
                          : ", unlimited cards."),
    };
}
