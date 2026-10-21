using InfoID.LicenseManager.Application.DTOs.Licenses;
using InfoID.Shared.Common;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>License creation and search (FR-ADM-4, FR-ADM-7) plus the actions that change a code
/// (FR-ADM-8). Actions that must reach a customer machine (a revoke taking effect, an extended expiry)
/// apply at that machine's next online re-validation or with a newly generated serial key.</summary>
/// <remarks>Operations return a failed <see cref="Result"/> for anything staff can fix in the form
/// (invalid input, a rule such as "perpetual licenses have no expiry"). A role that is not allowed to
/// act throws <c>ForbiddenException</c>; an unknown id throws <c>NotFoundException</c>.</remarks>
public interface ILicenseService
{
    /// <returns>The new codes in display form (XXXX-XXXX-XXXX-XXXX), in creation order, with their batch.</returns>
    Task<Result<CreatedLicensesDto>> CreateAsync(CreateLicenseRequestDto request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LicenseListItemDto>> SearchAsync(LicenseSearchDto search, CancellationToken cancellationToken = default);

    Task<LicenseDetailsDto> GetDetailsAsync(long licenseId, CancellationToken cancellationToken = default);

    Task<Result> RevokeAsync(RevokeLicenseRequestDto request, CancellationToken cancellationToken = default);

    Task<Result> ExtendAsync(ExtendLicenseRequestDto request, CancellationToken cancellationToken = default);

    Task<Result> SetOfflineActivationAsync(SetOfflineActivationRequestDto request, CancellationToken cancellationToken = default);

    /// <returns>How many machine slots were freed.</returns>
    Task<Result<int>> FreeMachineSlotsAsync(FreeMachineSlotsRequestDto request, CancellationToken cancellationToken = default);
}
