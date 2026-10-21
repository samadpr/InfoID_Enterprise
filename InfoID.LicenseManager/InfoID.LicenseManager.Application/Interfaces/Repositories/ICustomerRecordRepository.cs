using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Interfaces.Repositories;

/// <summary>The vendor-side organisation a license is sold to (not the app's sign-up accounts).</summary>
public interface ICustomerRecordRepository
{
    /// <summary>Tracked. Matches by e-mail when one is given, otherwise by organisation name.</summary>
    Task<CustomerRecord?> FindMatchingAsync(string organizationName, string? normalizedEmail, CancellationToken cancellationToken = default);

    void Add(CustomerRecord customer);
}
