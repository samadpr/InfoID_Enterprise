using InfoID.Domain.Common.Enums;

namespace InfoID.Application.Dtos.Organization;

public class AppUserDto
{
    public long Id { get; set; }
    public long OrganizationId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? PreferredLanguage { get; set; }
    public ThemeMode? ThemeMode { get; set; }
    public bool IsPasswordProtected { get; set; }
    public int? AutoLockMinutes { get; set; }
    public DateTime? LastLoginDate { get; set; }
}

public class CreateAppUserRequest
{
    public long OrganizationId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

/// <summary>Plain identity edits (name/email/phone) after the profile already
/// exists. Kept separate from CreateAppUserRequest (first-run capture) and from
/// UpdateAppUserSecurityRequest/UpdateAppUserPreferencesRequest so the User
/// Management -- Profile tab can save name/contact changes without touching
/// password or language state.</summary>
public class UpdateAppUserProfileRequest
{
    public long AppUserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

/// <summary>Covers FR-SEC-4/FR-SEC-6: password protection is opt-in, not
/// mandatory login on every launch (see the licensing/auth decoupling
/// decision from earlier). Plain-text Password is only ever accepted here,
/// at the Application boundary -- it's hashed before it touches the DB and
/// never stored or logged as plain text.</summary>
public class UpdateAppUserSecurityRequest
{
    public long AppUserId { get; set; }
    public bool IsPasswordProtected { get; set; }
    public string? NewPassword { get; set; }
    public int? AutoLockMinutes { get; set; }
}

public class UpdateAppUserPreferencesRequest
{
    public long AppUserId { get; set; }
    public string? PreferredLanguage { get; set; }
    public ThemeMode? ThemeMode { get; set; }
}
