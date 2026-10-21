using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Services;

/// <summary>Everything needed to make one or more activation codes. Staff forms fill only the license
/// terms and customer details; the request-approval flow also sets the product and customer account,
/// which no browser form can.</summary>
public sealed record NewLicenseSpec(
    int Quantity,
    LicenseTier Tier,
    LicenseType Type,
    DateOnly? ExpiryDate,
    int? MaxCardsPerPeriod,
    CardLimitPeriod LimitPeriod,
    int MaxActivations,
    bool AllowOfflineActivation,
    string? OrganizationName,
    string? OrganizationType,
    string? Country,
    string? ContactEmail,
    string? ContactPhone,
    string? Notes,
    string ProductCode,
    long? CustomerAccountId,
    long? CreatedByAdminUserId);

public sealed record BuiltLicenses(IReadOnlyList<LicenseCode> Codes, Guid? BatchId, CustomerRecord? Customer);

/// <summary>
/// The one place activation codes are made, for staff-created batches and for approved plan requests, so
/// both follow the same rules: unique 64-bit random codes, one batch id for several, the customer record
/// found by e-mail (or name) before a new one is created. Stages everything on the repositories; the caller
/// saves, so codes and whatever goes with them commit together.
/// </summary>
public sealed class LicenseCodeFactory
{
    private readonly ILicenseRepository _licenses;
    private readonly ICustomerRecordRepository _customers;
    private readonly TimeProvider _time;

    public LicenseCodeFactory(ILicenseRepository licenses, ICustomerRecordRepository customers, TimeProvider time)
    {
        _licenses = licenses;
        _customers = customers;
        _time = time;
    }

    /// <exception cref="Domain.Exceptions.DomainRuleException">The terms break a licensing rule.</exception>
    public async Task<BuiltLicenses> BuildAsync(NewLicenseSpec spec, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var customer = await FindOrCreateCustomerAsync(spec, cancellationToken);
        var batchId = spec.Quantity > 1 ? Guid.NewGuid() : (Guid?)null;
        var notes = string.IsNullOrWhiteSpace(spec.Notes) ? null : spec.Notes.Trim();

        var codes = (await GenerateUniqueCodesAsync(spec.Quantity, cancellationToken)).Select(code =>
        {
            var license = LicenseCode.Create(code, spec.Tier, spec.Type, spec.ExpiryDate, spec.MaxCardsPerPeriod,
                spec.LimitPeriod, spec.MaxActivations, spec.AllowOfflineActivation, spec.ProductCode, today);
            license.BatchId = batchId;
            license.CustomerRecord = customer;
            license.CustomerAccountId = spec.CustomerAccountId;
            license.CreatedByAdminUserId = spec.CreatedByAdminUserId;
            license.Notes = notes;
            return license;
        }).ToList();

        _licenses.AddRange(codes);
        return new BuiltLicenses(codes, batchId, customer);
    }

    private async Task<IReadOnlyList<string>> GenerateUniqueCodesAsync(int quantity, CancellationToken cancellationToken)
    {
        // 64 random bits make a clash practically impossible, but a duplicate must never be issued: check in one query.
        var accepted = new List<string>(quantity);
        var seen = new HashSet<string>();
        while (accepted.Count < quantity)
        {
            var candidates = new List<string>();
            while (accepted.Count + candidates.Count < quantity)
            {
                var code = ActivationCode.Generate().Value;
                if (seen.Add(code)) candidates.Add(code);
            }

            var existing = await _licenses.GetExistingActivationCodesAsync(candidates, cancellationToken);
            accepted.AddRange(candidates.Where(c => !existing.Contains(c)));
        }

        return accepted;
    }

    private async Task<CustomerRecord?> FindOrCreateCustomerAsync(NewLicenseSpec spec, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(spec.OrganizationName)) return null;

        var name = spec.OrganizationName.Trim();
        var email = string.IsNullOrWhiteSpace(spec.ContactEmail) ? null : spec.ContactEmail.Trim().ToLowerInvariant();

        var existing = await _customers.FindMatchingAsync(name, email, cancellationToken);
        if (existing is not null) return existing;

        var customer = new CustomerRecord
        {
            OrganizationName = name,
            OrganizationType = Clean(spec.OrganizationType),
            Country = Clean(spec.Country),
            ContactEmail = email,
            ContactPhone = Clean(spec.ContactPhone),
        };
        _customers.Add(customer);
        return customer;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
