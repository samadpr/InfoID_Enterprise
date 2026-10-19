using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Licensing.Server.Services;

public sealed record SignInResult(bool Succeeded, string? Error, AdminUser? Admin)
{
    public static SignInResult Fail(string error) => new(false, error, null);
}

/// <summary>Staff sign-in (FR-ADM-1): mandatory every session, PBKDF2 passwords,
/// lockout after repeated failures, every attempt audited. TOTP two-factor (FR-ADM-2)
/// is the next increment -- the AdminUser table already has its fields.</summary>
public sealed class AuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IDbContextFactory<LicenseDbContext> _dbFactory;
    private readonly AdminContext _admin;
    private readonly AuditService _audit;

    public AuthService(IDbContextFactory<LicenseDbContext> dbFactory, AdminContext admin, AuditService audit)
    {
        _dbFactory = dbFactory;
        _admin = admin;
        _audit = audit;
    }

    public async Task<bool> HasAnyAdminAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.AdminUsers.AnyAsync(u => !u.Cancelled);
    }

    /// <summary>First-run only: creates the initial Super Admin when the table is empty.</summary>
    public async Task<SignInResult> CreateInitialSuperAdminAsync(string fullName, string email, string password)
    {
        if (await HasAnyAdminAsync())
            return SignInResult.Fail("An administrator already exists. Please sign in.");

        var problem = ValidateAccount(fullName, email) ?? PasswordHasher.ValidateStrength(password);
        if (problem is not null) return SignInResult.Fail(problem);

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = email.Trim();
        var admin = new AdminUser
        {
            FullName = fullName.Trim(),
            Email = NormalizeEmail(email),
            PasswordHash = PasswordHasher.Hash(password),
            Role = AdminRoles.SuperAdmin,
        };
        db.AdminUsers.Add(admin);
        await db.SaveChangesAsync();

        await _audit.LogAsync("Admin.InitialSuperAdminCreated", new { admin.Email }, admin.Id);
        return await SignInAsync(email, password);
    }

    public async Task<SignInResult> SignInAsync(string email, string password)
    {
        var normalized = NormalizeEmail(email);
        await using var db = await _dbFactory.CreateDbContextAsync();
        var admin = await db.AdminUsers.FirstOrDefaultAsync(u => u.Email == normalized && !u.Cancelled);

        const string generic = "Email or password is incorrect.";
        if (admin is null)
        {
            await _audit.LogAsync("Admin.SignInFailed", new { Email = normalized, Reason = "UnknownEmail" });
            return SignInResult.Fail(generic);
        }

        if (admin.LockoutEndUtc is { } until && until > DateTime.UtcNow)
        {
            await _audit.LogAsync("Admin.SignInBlocked", new { admin.Email }, admin.Id);
            return SignInResult.Fail($"This account is locked after too many failed attempts. Try again after {until.ToLocalTime():t}.");
        }

        if (!PasswordHasher.Verify(password, admin.PasswordHash))
        {
            admin.FailedLoginCount++;
            if (admin.FailedLoginCount >= MaxFailedAttempts)
            {
                admin.LockoutEndUtc = DateTime.UtcNow.Add(LockoutDuration);
                admin.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync();
            await _audit.LogAsync("Admin.SignInFailed", new { admin.Email, Reason = "WrongPassword" }, admin.Id);
            return SignInResult.Fail(generic);
        }

        admin.FailedLoginCount = 0;
        admin.LockoutEndUtc = null;
        admin.LastLoginDate = DateTime.UtcNow;
        db.CurrentUser = admin.Email;
        await db.SaveChangesAsync();

        _admin.SignIn(admin);
        await _audit.LogAsync("Admin.SignedIn");
        return new SignInResult(true, null, admin);
    }

    public async Task SignOutAsync()
    {
        if (!_admin.IsSignedIn) return;
        await _audit.LogAsync("Admin.SignedOut");
        _admin.SignOut();
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string? ValidateAccount(string fullName, string email)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "Enter your full name.";
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return "Enter a valid email address.";
        return null;
    }
}
