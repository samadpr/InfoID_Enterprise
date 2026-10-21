using InfoID.LicenseManager.Application.DTOs.Auth;
using InfoID.Shared.Common;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>Staff sign-in (FR-ADM-1): PBKDF2 passwords, lockout after repeated failures,
/// every attempt audited. TOTP two-factor (FR-ADM-2) was never implemented in the desktop
/// manager either and is out of scope here.</summary>
public interface IAuthService
{
    Task<bool> HasAnyAdminAsync(CancellationToken cancellationToken = default);

    /// <summary>First-run only: creates the initial Super Admin when no staff account exists.</summary>
    Task<Result<AuthenticatedAdminDto>> CreateInitialSuperAdminAsync(CreateInitialAdminRequestDto request, CancellationToken cancellationToken = default);

    Task<Result<AuthenticatedAdminDto>> SignInAsync(SignInRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Re-checks a signed-in session against the database: null when the account no
    /// longer exists, was disabled or is locked. The returned role is the current one.</summary>
    Task<AuthenticatedAdminDto?> GetActiveAdminAsync(long adminUserId, CancellationToken cancellationToken = default);

    Task RecordSignOutAsync(CancellationToken cancellationToken = default);
}
