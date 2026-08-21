using InfoID.Application.Dtos.Organization;
using InfoID.Shared.Common;

namespace InfoID.Application.Interfaces;

public interface IAppUserService
{
    Task<AppUserDto?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<AppUserDto>> GetByOrganizationAsync(long organizationId, CancellationToken ct = default);

    Task<Result<long>> CreateAsync(CreateAppUserRequest request, CancellationToken ct = default);

    /// <summary>Updates FullName/Email/Phone only -- see UpdateAppUserProfileRequest.</summary>
    Task<Result> UpdateProfileAsync(UpdateAppUserProfileRequest request, CancellationToken ct = default);

    Task<Result> UpdateSecurityAsync(UpdateAppUserSecurityRequest request, CancellationToken ct = default);

    Task<Result> UpdatePreferencesAsync(UpdateAppUserPreferencesRequest request, CancellationToken ct = default);

    /// <summary>Verifies a login/unlock attempt. Returns false (never throws)
    /// on a wrong password -- wrong password is an expected outcome, not an
    /// exceptional one.</summary>
    Task<bool> VerifyPasswordAsync(long appUserId, string password, CancellationToken ct = default);

    Task RecordLoginAsync(long appUserId, CancellationToken ct = default);
}
