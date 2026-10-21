using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Tests.Unit.Domain;

public class AdminUserTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Four_failed_logins_do_not_lock_the_account()
    {
        var admin = new AdminUser();

        for (var i = 0; i < AdminUser.MaxFailedLogins - 1; i++) admin.RegisterFailedLogin(Now);

        Assert.False(admin.IsLockedOut(Now));
        Assert.Equal(AdminUser.MaxFailedLogins - 1, admin.FailedLoginCount);
    }

    [Fact]
    public void The_fifth_failed_login_locks_for_fifteen_minutes_and_restarts_the_counter()
    {
        var admin = new AdminUser();

        for (var i = 0; i < AdminUser.MaxFailedLogins; i++) admin.RegisterFailedLogin(Now);

        Assert.Equal(Now.AddMinutes(15), admin.LockoutEndUtc);
        Assert.Equal(0, admin.FailedLoginCount);
        Assert.True(admin.IsLockedOut(Now.AddMinutes(14)));
        Assert.False(admin.IsLockedOut(Now.AddMinutes(15)));
    }

    [Fact]
    public void A_successful_login_clears_failures_and_lockout_and_records_the_time()
    {
        var admin = new AdminUser { FailedLoginCount = 3, LockoutEndUtc = Now.AddMinutes(-1) };

        admin.RegisterSuccessfulLogin(Now);

        Assert.Equal(0, admin.FailedLoginCount);
        Assert.Null(admin.LockoutEndUtc);
        Assert.Equal(Now, admin.LastLoginDate);
    }

    [Theory]
    [InlineData(AdminRoles.SuperAdmin, true)]
    [InlineData(AdminRoles.LicenseManager, true)]
    [InlineData(AdminRoles.SupportAgent, false)]
    [InlineData("Intruder", false)]
    [InlineData(null, false)]
    public void Only_super_admins_and_license_managers_may_manage_licenses(string? role, bool expected) =>
        Assert.Equal(expected, AdminRoles.CanManageLicenses(role));

    [Theory]
    [InlineData(AdminRoles.SuperAdmin, true)]
    [InlineData(AdminRoles.LicenseManager, true)]
    [InlineData(AdminRoles.SupportAgent, true)]
    [InlineData("Intruder", false)]
    [InlineData(null, false)]
    public void Every_known_role_may_generate_serial_keys_but_nobody_else(string? role, bool expected) =>
        Assert.Equal(expected, AdminRoles.CanGenerateSerialKeys(role));
}
