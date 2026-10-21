using InfoID.LicenseManager.Application.Services;
using InfoID.LicenseManager.Domain.Rules;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Unit.Application;

public class AuditServiceTests
{
    private readonly InMemoryAdminUserRepository _admins = new();
    private readonly InMemoryAuditLogRepository _rows;
    private readonly FakeCurrentUser _user = new() { IsAuthenticated = true, AdminUserId = 5, Email = "staff@infoid.test", ClientAddress = "203.0.113.9" };
    private readonly TestClock _clock = new();
    private readonly AuditService _service;

    public AuditServiceTests()
    {
        _rows = new InMemoryAuditLogRepository(_admins);
        _service = new AuditService(_rows, _user, _clock);
    }

    [Fact]
    public async Task Each_entry_links_to_the_previous_one_and_records_who_and_where()
    {
        await _service.LogAsync("LicenseCode.Created", new { Count = 3 });
        await _service.LogAsync("LicenseCode.Revoked");

        var (first, second) = (_rows.Rows[0], _rows.Rows[1]);
        Assert.Null(first.PreviousHash);
        Assert.Equal(first.RecordHash, second.PreviousHash);
        Assert.Equal(5, first.AdminUserId);
        Assert.Equal("203.0.113.9", first.IPAddress);
        Assert.Equal("{\"Count\":3}", first.DetailsJson);
        Assert.Null(second.DetailsJson);
        Assert.True(AuditChain.IsIntact(first, null));
        Assert.True(AuditChain.IsIntact(second, first.RecordHash));
    }

    [Fact]
    public async Task An_explicit_admin_id_overrides_the_current_user()
    {
        await _service.LogAsync("Admin.SignedIn", adminUserIdOverride: 99);

        Assert.Equal(99, _rows.Rows[0].AdminUserId);
    }

    [Fact]
    public async Task The_stored_timestamp_is_the_clock_time_truncated_to_microseconds()
    {
        await _service.LogAsync("X");

        var stored = _rows.Rows[0].ActionDate;
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, stored);
        Assert.Equal(0, stored.Ticks % 10);
        Assert.Equal(DateTimeKind.Utc, stored.Kind);
    }

    [Fact]
    public async Task A_blank_action_name_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.LogAsync("  "));
    }

    [Fact]
    public async Task An_untouched_log_verifies()
    {
        for (var i = 0; i < 5; i++) await _service.LogAsync("Action" + i, new { i });

        Assert.Null(await _service.VerifyChainAsync());
    }

    [Fact]
    public async Task An_empty_log_verifies()
    {
        Assert.Null(await _service.VerifyChainAsync());
    }

    [Fact]
    public async Task Verification_reports_the_first_tampered_row()
    {
        for (var i = 0; i < 5; i++) await _service.LogAsync("Action" + i);
        _rows.Rows[2].Action = "Rewritten";

        Assert.Equal(3, await _service.VerifyChainAsync());
    }

    [Fact]
    public async Task Verification_reports_a_deleted_row()
    {
        for (var i = 0; i < 5; i++) await _service.LogAsync("Action" + i);
        _rows.Rows.RemoveAt(1); // ids keep their gap: 1, 3, 4, 5

        Assert.Equal(3, await _service.VerifyChainAsync());
    }

    [Fact]
    public async Task Recent_entries_come_newest_first_with_the_staff_name_and_respect_the_limit()
    {
        var staff = _admins.Seed("staff@infoid.test", "x", fullName: "Sam Staff");
        _user.AdminUserId = staff.Id;
        for (var i = 0; i < 4; i++) await _service.LogAsync("Action" + i);

        var recent = await _service.GetRecentAsync(3);

        Assert.Equal(["Action3", "Action2", "Action1"], recent.Select(r => r.Action));
        Assert.All(recent, r => Assert.Equal("Sam Staff", r.AdminName));
    }
}
