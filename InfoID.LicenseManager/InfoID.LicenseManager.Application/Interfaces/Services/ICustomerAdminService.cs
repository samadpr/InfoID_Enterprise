using InfoID.LicenseManager.Application.DTOs.Customers;
using InfoID.Shared.Common;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>
/// Staff view of customer accounts (sign-ups from the app): leads, their requests and licenses, resending the
/// verification code, and disabling/enabling. Disabling keeps the e-mail reserved and signs the account out
/// everywhere; enabling restores it.
/// </summary>
public interface ICustomerAdminService
{
    Task<IReadOnlyList<CustomerListItemDto>> SearchAsync(CustomerSearchDto search, CancellationToken cancellationToken = default);

    /// <exception cref="Common.NotFoundException">No such account.</exception>
    Task<CustomerDetailsDto> GetDetailsAsync(long accountId, CancellationToken cancellationToken = default);

    Task<Result> SetDisabledAsync(SetCustomerDisabledRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Sends the account a fresh e-mail verification code (at most one a minute).</summary>
    Task<Result> ResendVerificationAsync(long accountId, CancellationToken cancellationToken = default);
}
