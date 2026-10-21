using InfoID.LicenseManager.Application.DTOs.Auth;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Security;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Application.Mappings;
using InfoID.LicenseManager.Application.Validators;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.Shared.Common;
using Microsoft.Extensions.Logging;

namespace InfoID.LicenseManager.Application.Services;

public sealed class AuthService : IAuthService
{
    // One message for "no such account" and "wrong password": the response must not reveal which e-mails exist.
    private const string InvalidCredentials = "Email or password is incorrect.";

    private readonly IAdminUserRepository _admins;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _audit;
    private readonly IPasswordHasher _hasher;
    private readonly TimeProvider _time;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IAdminUserRepository admins, IUnitOfWork unitOfWork, IAuditService audit,
        IPasswordHasher hasher, TimeProvider time, ILogger<AuthService> logger)
    {
        _admins = admins;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _hasher = hasher;
        _time = time;
        _logger = logger;
    }

    public Task<bool> HasAnyAdminAsync(CancellationToken cancellationToken = default) =>
        _admins.AnyActiveAsync(cancellationToken);

    public async Task<Result<AuthenticatedAdminDto>> CreateInitialSuperAdminAsync(
        CreateInitialAdminRequestDto request, CancellationToken cancellationToken = default)
    {
        var problem = AdminAccountValidator.Validate(request);
        if (problem is not null) return Result<AuthenticatedAdminDto>.Failure(problem);

        var email = NormalizeEmail(request.Email);
        _unitOfWork.UseActor(email);
        var admin = new AdminUser
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = _hasher.Hash(request.Password),
            Role = AdminRoles.SuperAdmin,
        };

        if (!await _admins.TryAddFirstAdminAsync(admin, cancellationToken))
            return Result<AuthenticatedAdminDto>.Failure("An administrator already exists. Please sign in.");

        _logger.LogWarning("Initial Super Admin account {AdminUserId} created", admin.Id);
        await _audit.LogAsync("Admin.InitialSuperAdminCreated", new { admin.Email }, admin.Id, cancellationToken);
        return await SignInAsync(new SignInRequestDto(request.Email, request.Password), cancellationToken);
    }

    public async Task<Result<AuthenticatedAdminDto>> SignInAsync(SignInRequestDto request, CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(request.Email);
        var password = request.Password ?? string.Empty;
        var now = _time.GetUtcNow().UtcDateTime;

        var admin = await _admins.FindActiveByEmailAsync(email, cancellationToken);
        if (admin is null)
        {
            _hasher.SimulateVerify(password);
            await _audit.LogAsync("Admin.SignInFailed", new { Email = email, Reason = "UnknownEmail" }, cancellationToken: cancellationToken);
            return Result<AuthenticatedAdminDto>.Failure(InvalidCredentials);
        }

        if (admin.IsLockedOut(now))
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((admin.LockoutEndUtc!.Value - now).TotalMinutes));
            _logger.LogWarning("Sign-in blocked for locked staff account {AdminUserId}", admin.Id);
            await _audit.LogAsync("Admin.SignInBlocked", new { admin.Email }, admin.Id, cancellationToken);
            return Result<AuthenticatedAdminDto>.Failure(
                $"This account is locked after too many failed attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.");
        }

        if (!_hasher.Verify(password, admin.PasswordHash))
        {
            admin.RegisterFailedLogin(now);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Failed sign-in for staff account {AdminUserId}", admin.Id);
            await _audit.LogAsync("Admin.SignInFailed", new { admin.Email, Reason = "WrongPassword" }, admin.Id, cancellationToken);
            return Result<AuthenticatedAdminDto>.Failure(InvalidCredentials);
        }

        admin.RegisterSuccessfulLogin(now);
        _unitOfWork.UseActor(admin.Email);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Staff account {AdminUserId} signed in", admin.Id);
        await _audit.LogAsync("Admin.SignedIn", adminUserIdOverride: admin.Id, cancellationToken: cancellationToken);
        return Result<AuthenticatedAdminDto>.Success(admin.ToAuthenticatedDto());
    }

    public async Task<AuthenticatedAdminDto?> GetActiveAdminAsync(long adminUserId, CancellationToken cancellationToken = default)
    {
        var admin = await _admins.FindActiveByIdAsync(adminUserId, cancellationToken);
        return admin is null || admin.IsLockedOut(_time.GetUtcNow().UtcDateTime) ? null : admin.ToAuthenticatedDto();
    }

    public async Task RecordSignOutAsync(CancellationToken cancellationToken = default)
    {
        await _audit.LogAsync("Admin.SignedOut", cancellationToken: cancellationToken);
    }

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();
}
