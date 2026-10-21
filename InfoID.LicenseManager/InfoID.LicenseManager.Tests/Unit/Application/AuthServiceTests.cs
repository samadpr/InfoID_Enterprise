using InfoID.LicenseManager.Application.DTOs.Auth;
using InfoID.LicenseManager.Application.Services;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfoID.LicenseManager.Tests.Unit.Application;

public class AuthServiceTests
{
    private const string Email = "boss@infoid.test";
    private const string Password = "Correct-Horse-9";

    private readonly InMemoryAdminUserRepository _admins = new();
    private readonly InMemoryAuditLogRepository _auditRows;
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly TestClock _clock = new();
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _auditRows = new InMemoryAuditLogRepository(_admins);
        var audit = new AuditService(_auditRows, new FakeCurrentUser(), _clock);
        _service = new AuthService(_admins, _unitOfWork, audit, _hasher, _clock, NullLogger<AuthService>.Instance);
    }

    private IEnumerable<string> AuditedActions => _auditRows.Rows.Select(r => r.Action);

    [Fact]
    public async Task Correct_credentials_sign_in_with_the_stored_role_and_audit_it()
    {
        var admin = _admins.Seed(Email, Password, AdminRoles.LicenseManager, "Lee Manager");

        var result = await _service.SignInAsync(new SignInRequestDto(Email, Password));

        Assert.True(result.Succeeded);
        Assert.Equal(new AuthenticatedAdminDto(admin.Id, "Lee Manager", Email, AdminRoles.LicenseManager), result.Value);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, admin.LastLoginDate);
        Assert.Equal(Email, _unitOfWork.Actor);
        Assert.Equal(["Admin.SignedIn"], AuditedActions);
        Assert.Equal(admin.Id, _auditRows.Rows[0].AdminUserId);
    }

    [Fact]
    public async Task Email_is_matched_ignoring_case_and_surrounding_spaces()
    {
        _admins.Seed(Email, Password);

        var result = await _service.SignInAsync(new SignInRequestDto("  BOSS@InfoID.test ", Password));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_give_the_same_message()
    {
        _admins.Seed(Email, Password);

        var unknown = await _service.SignInAsync(new SignInRequestDto("nobody@infoid.test", "whatever"));
        var wrong = await _service.SignInAsync(new SignInRequestDto(Email, "wrong"));

        Assert.False(unknown.Succeeded);
        Assert.False(wrong.Succeeded);
        Assert.Equal(unknown.Errors, wrong.Errors);
        Assert.Equal("Email or password is incorrect.", unknown.Errors[0]);
    }

    [Fact]
    public async Task Unknown_email_still_spends_password_verification_time()
    {
        await _service.SignInAsync(new SignInRequestDto("nobody@infoid.test", "whatever"));

        Assert.Equal(1, _hasher.SimulatedVerifications);
    }

    [Fact]
    public async Task Failed_attempts_are_audited_without_recording_the_password()
    {
        _admins.Seed(Email, Password);

        await _service.SignInAsync(new SignInRequestDto(Email, "wrong-secret-guess"));
        await _service.SignInAsync(new SignInRequestDto("nobody@infoid.test", "another-secret-guess"));

        Assert.Equal(["Admin.SignInFailed", "Admin.SignInFailed"], AuditedActions);
        Assert.DoesNotContain(_auditRows.Rows, r => r.DetailsJson!.Contains("secret-guess", StringComparison.Ordinal));
        Assert.Contains("WrongPassword", _auditRows.Rows[0].DetailsJson);
        Assert.Contains("UnknownEmail", _auditRows.Rows[1].DetailsJson);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_against_the_correct_password()
    {
        var admin = _admins.Seed(Email, Password);

        for (var i = 0; i < AdminUser.MaxFailedLogins; i++)
            await _service.SignInAsync(new SignInRequestDto(Email, "wrong"));
        var blocked = await _service.SignInAsync(new SignInRequestDto(Email, Password));

        Assert.False(blocked.Succeeded);
        Assert.Contains("locked", blocked.Errors[0], StringComparison.Ordinal);
        Assert.Contains("15 minutes", blocked.Errors[0], StringComparison.Ordinal);
        Assert.Contains("Admin.SignInBlocked", AuditedActions);
        Assert.NotNull(admin.LockoutEndUtc);
    }

    [Fact]
    public async Task The_lockout_ends_after_fifteen_minutes()
    {
        _admins.Seed(Email, Password);
        for (var i = 0; i < AdminUser.MaxFailedLogins; i++)
            await _service.SignInAsync(new SignInRequestDto(Email, "wrong"));

        _clock.Advance(TimeSpan.FromMinutes(15));
        var result = await _service.SignInAsync(new SignInRequestDto(Email, Password));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_successful_login_resets_the_failure_counter()
    {
        var admin = _admins.Seed(Email, Password);
        for (var i = 0; i < 3; i++) await _service.SignInAsync(new SignInRequestDto(Email, "wrong"));

        await _service.SignInAsync(new SignInRequestDto(Email, Password));

        Assert.Equal(0, admin.FailedLoginCount);
    }

    [Fact]
    public async Task A_disabled_account_cannot_sign_in()
    {
        var admin = _admins.Seed(Email, Password);
        admin.Cancelled = true;

        var result = await _service.SignInAsync(new SignInRequestDto(Email, Password));

        Assert.False(result.Succeeded);
        Assert.Equal("Email or password is incorrect.", result.Errors[0]);
    }

    [Fact]
    public async Task First_run_creates_a_super_admin_and_signs_in()
    {
        var result = await _service.CreateInitialSuperAdminAsync(new CreateInitialAdminRequestDto("Ada Admin", " ADA@infoid.test ", "Str0ng-Passphrase"));

        Assert.True(result.Succeeded);
        Assert.Equal(AdminRoles.SuperAdmin, result.Value!.Role);
        Assert.Equal("ada@infoid.test", result.Value.Email);
        var stored = Assert.Single(_admins.All);
        Assert.Equal("fake$Str0ng-Passphrase", stored.PasswordHash);
        Assert.Equal(["Admin.InitialSuperAdminCreated", "Admin.SignedIn"], AuditedActions);
    }

    [Fact]
    public async Task First_run_is_refused_once_any_administrator_exists()
    {
        _admins.Seed(Email, Password);

        var result = await _service.CreateInitialSuperAdminAsync(new CreateInitialAdminRequestDto("Mallory", "mallory@evil.test", "Str0ng-Passphrase"));

        Assert.False(result.Succeeded);
        Assert.Single(_admins.All);
    }

    [Theory]
    [InlineData("", "ada@infoid.test", "Str0ng-Passphrase")]
    [InlineData("Ada", "not-an-email", "Str0ng-Passphrase")]
    [InlineData("Ada", "ada@infoid.test", "weak")]
    public async Task First_run_validates_its_input_and_creates_nothing_when_invalid(string name, string email, string password)
    {
        var result = await _service.CreateInitialSuperAdminAsync(new CreateInitialAdminRequestDto(name, email, password));

        Assert.False(result.Succeeded);
        Assert.Empty(_admins.All);
        Assert.Empty(_auditRows.Rows);
    }

    [Fact]
    public async Task Two_simultaneous_first_run_requests_create_exactly_one_administrator()
    {
        var attempts = Enumerable.Range(0, 8)
            .Select(i => Task.Run(() => _service.CreateInitialSuperAdminAsync(
                new CreateInitialAdminRequestDto("Admin " + i, $"admin{i}@infoid.test", "Str0ng-Passphrase"))))
            .ToArray();

        var results = await Task.WhenAll(attempts);

        Assert.Single(_admins.All);
        Assert.Equal(1, results.Count(r => r.Succeeded));
    }

    [Fact]
    public async Task Session_revalidation_returns_the_current_role_and_rejects_locked_or_removed_accounts()
    {
        var admin = _admins.Seed(Email, Password, AdminRoles.SupportAgent);

        Assert.Equal(AdminRoles.SupportAgent, (await _service.GetActiveAdminAsync(admin.Id))!.Role);

        admin.Role = AdminRoles.SuperAdmin;
        Assert.Equal(AdminRoles.SuperAdmin, (await _service.GetActiveAdminAsync(admin.Id))!.Role);

        admin.LockoutEndUtc = _clock.GetUtcNow().UtcDateTime.AddMinutes(5);
        Assert.Null(await _service.GetActiveAdminAsync(admin.Id));

        admin.LockoutEndUtc = null;
        admin.Cancelled = true;
        Assert.Null(await _service.GetActiveAdminAsync(admin.Id));
        Assert.Null(await _service.GetActiveAdminAsync(9999));
    }

    [Fact]
    public async Task Sign_out_is_audited()
    {
        await _service.RecordSignOutAsync();

        Assert.Equal(["Admin.SignedOut"], AuditedActions);
    }
}
