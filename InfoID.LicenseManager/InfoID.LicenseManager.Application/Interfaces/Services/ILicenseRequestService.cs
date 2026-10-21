using InfoID.LicenseManager.Application.DTOs.Accounts;
using InfoID.LicenseManager.Application.DTOs.Requests;
using InfoID.Shared.Common;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>
/// Plan requests sent from the app: customers choose a plan and send their details (payment happens
/// outside the system); staff review them here and Approve, which creates the activation code (linked
/// to the customer account, if any) and e-mails it, or Reject with a reason the customer sees.
/// </summary>
public interface ILicenseRequestService
{
    Task<IReadOnlyList<LicenseRequestDto>> SearchAsync(LicenseRequestSearchDto search, CancellationToken cancellationToken = default);

    /// <exception cref="Common.NotFoundException">No such request.</exception>
    Task<LicenseRequestDto> GetAsync(long requestId, CancellationToken cancellationToken = default);

    Task<int> CountNewAsync(CancellationToken cancellationToken = default);

    Task<Result> MarkInProgressAsync(long requestId, CancellationToken cancellationToken = default);

    /// <summary>Creates the code, links it to the request and the customer account, and records the decision
    /// in ONE transaction (no code without an approved request, no approval without a code); the e-mail follows.</summary>
    Task<Result<ApprovedLicenseRequestDto>> ApproveAsync(ApproveLicenseRequestDto request, CancellationToken cancellationToken = default);

    Task<Result> RejectAsync(RejectLicenseRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>A customer asks for a plan from the app. Anonymous; <paramref name="signedInAccount"/> (a verified account
    /// resolved from its session token) supplies the e-mail and links the request to the account. At most 5 open requests per e-mail.</summary>
    Task<SubmittedRequestDto> SubmitAsync(SubmitLicenseRequestDto request, AccountSessionDto? signedInAccount, CancellationToken cancellationToken = default);
}
