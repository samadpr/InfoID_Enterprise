using InfoID.Application.Dtos.Organization;
using InfoID.Application.Interfaces;
using InfoID.Application.Security;
using InfoID.Domain.Common.Interfaces;
using InfoID.Shared.Common;
using AppUserEntity = InfoID.Domain.Entities.OrganizationModule.AppUser;

namespace InfoID.Application.Services;

public class AppUserService : IAppUserService
{
    private readonly IUnitOfWork _uow;

    public AppUserService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<AppUserDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var user = await _uow.Repository<AppUserEntity>().GetByIdAsync(id, ct);
        return user is null ? null : ToDto(user);
    }

    public async Task<IReadOnlyList<AppUserDto>> GetByOrganizationAsync(long organizationId, CancellationToken ct = default)
    {
        var users = await _uow.Repository<AppUserEntity>().FindAsync(u => u.OrganizationId == organizationId, ct);
        return users.Select(ToDto).ToList();
    }

    public async Task<Result<long>> CreateAsync(CreateAppUserRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return Result<long>.Failure("Full name is required.");
        }

        var entity = new AppUserEntity
        {
            OrganizationId = request.OrganizationId,
            FullName = request.FullName.Trim(),
            Email = request.Email,
            Phone = request.Phone,
            IsPasswordProtected = false,
        };

        await _uow.Repository<AppUserEntity>().AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<long>.Success(entity.Id);
    }

    public async Task<Result> UpdateProfileAsync(UpdateAppUserProfileRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return Result.Failure("Full name is required.");
        }

        var entity = await _uow.Repository<AppUserEntity>().GetByIdAsync(request.AppUserId, ct);
        if (entity is null)
        {
            return Result.Failure($"AppUser {request.AppUserId} was not found.");
        }

        entity.FullName = request.FullName.Trim();
        entity.Email = request.Email;
        entity.Phone = request.Phone;

        _uow.Repository<AppUserEntity>().Update(entity);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> UpdateSecurityAsync(UpdateAppUserSecurityRequest request, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<AppUserEntity>().GetByIdAsync(request.AppUserId, ct);
        if (entity is null)
        {
            return Result.Failure($"AppUser {request.AppUserId} was not found.");
        }

        if (request.IsPasswordProtected)
        {
            if (string.IsNullOrEmpty(request.NewPassword))
            {
                // Turning protection on but no new password given, and none set previously
                if (string.IsNullOrEmpty(entity.PasswordHash))
                {
                    return Result.Failure("A password is required to enable password protection.");
                }
            }
            else
            {
                entity.PasswordHash = PasswordHasher.Hash(request.NewPassword);
            }
        }
        else
        {
            entity.PasswordHash = null;
        }

        entity.IsPasswordProtected = request.IsPasswordProtected;
        entity.AutoLockMinutes = request.AutoLockMinutes;

        _uow.Repository<AppUserEntity>().Update(entity);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> UpdatePreferencesAsync(UpdateAppUserPreferencesRequest request, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<AppUserEntity>().GetByIdAsync(request.AppUserId, ct);
        if (entity is null)
        {
            return Result.Failure($"AppUser {request.AppUserId} was not found.");
        }

        entity.PreferredLanguage = request.PreferredLanguage;
        entity.ThemeMode = request.ThemeMode;

        _uow.Repository<AppUserEntity>().Update(entity);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<bool> VerifyPasswordAsync(long appUserId, string password, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<AppUserEntity>().GetByIdAsync(appUserId, ct);
        if (entity is null) return false;

        // No password protection enabled -- nothing to verify against (FR-SEC-4: opt-in only).
        if (!entity.IsPasswordProtected || string.IsNullOrEmpty(entity.PasswordHash))
        {
            return true;
        }

        return PasswordHasher.Verify(password, entity.PasswordHash);
    }

    public async Task RecordLoginAsync(long appUserId, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<AppUserEntity>().GetByIdAsync(appUserId, ct);
        if (entity is null) return;

        entity.LastLoginDate = DateTime.UtcNow;
        _uow.Repository<AppUserEntity>().Update(entity);
        await _uow.SaveChangesAsync(ct);
    }

    private static AppUserDto ToDto(AppUserEntity u) => new()
    {
        Id = u.Id,
        OrganizationId = u.OrganizationId,
        FullName = u.FullName,
        Email = u.Email,
        Phone = u.Phone,
        PreferredLanguage = u.PreferredLanguage,
        ThemeMode = u.ThemeMode,
        IsPasswordProtected = u.IsPasswordProtected,
        AutoLockMinutes = u.AutoLockMinutes,
        LastLoginDate = u.LastLoginDate,
    };
}
