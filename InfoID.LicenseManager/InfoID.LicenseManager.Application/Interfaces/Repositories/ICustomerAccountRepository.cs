using InfoID.LicenseManager.Application.DTOs.Customers;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Interfaces.Repositories;

public sealed record CustomerSearchCriteria(CustomerFilter Filter, string? Text, int Take);

public interface ICustomerAccountRepository
{
    /// <summary>Every customer account, disabled ones included (a disabled account is still a customer).</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Newest first, with request and license counts; the filter is applied in the database.</summary>
    Task<IReadOnlyList<CustomerListItemDto>> SearchAsync(CustomerSearchCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>Tracked; includes disabled accounts (they are what "enable" targets).</summary>
    Task<CustomerAccount?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountLicenseDto>> GetLicensesAsync(long accountId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountRequestDto>> GetRequestsAsync(long accountId, CancellationToken cancellationToken = default);

    /// <summary>Marks every live session of the account as revoked (tracked; saved with the unit of work).</summary>
    Task RevokeActiveSessionsAsync(long accountId, DateTime utcNow, CancellationToken cancellationToken = default);

    // ---- the app sign-in flow ----

    /// <summary>Tracked; matches disabled accounts too (their e-mail stays reserved).</summary>
    Task<CustomerAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    void Add(CustomerAccount account);

    void AddSession(CustomerSession session);

    /// <summary>Tracked live session by the SHA-256 hash of its token, with its account.</summary>
    Task<CustomerSession?> FindLiveSessionAsync(string tokenHash, CancellationToken cancellationToken = default);
}
