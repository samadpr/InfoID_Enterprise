using System.Reflection;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Domain.Rules;
using LegacyAuditLog = InfoID.Licensing.Server.Entities.AdminAuditLog;
using LegacyAuditService = InfoID.Licensing.Server.Services.AuditService;

namespace InfoID.LicenseManager.Tests.Unit.Domain;

public class AuditChainTests
{
    private static AdminAuditLog Entry(string action, string? previous, long? admin = 7) => Seal(new AdminAuditLog
    {
        AdminUserId = admin,
        Action = action,
        IPAddress = "10.0.0.5",
        ActionDate = new DateTime(2026, 10, 19, 12, 30, 45, 123, DateTimeKind.Utc).AddTicks(4560),
        DetailsJson = "{\"Email\":\"a@b.c\"}",
        PreviousHash = previous,
    });

    private static AdminAuditLog Seal(AdminAuditLog entry)
    {
        entry.RecordHash = AuditChain.ComputeHash(entry);
        return entry;
    }

    [Fact]
    public void A_correctly_linked_row_is_intact()
    {
        var first = Entry("A", null);
        var second = Entry("B", first.RecordHash);

        Assert.True(AuditChain.IsIntact(first, null));
        Assert.True(AuditChain.IsIntact(second, first.RecordHash));
    }

    [Fact]
    public void Editing_a_row_breaks_its_hash()
    {
        var row = Entry("A", null);

        row.Action = "Edited";

        Assert.False(AuditChain.IsIntact(row, null));
    }

    [Fact]
    public void Removing_a_row_from_the_middle_breaks_the_link()
    {
        var first = Entry("A", null);
        var second = Entry("B", first.RecordHash);
        var third = Entry("C", second.RecordHash);

        Assert.False(AuditChain.IsIntact(third, first.RecordHash));
    }

    [Fact]
    public void Timestamps_are_truncated_to_the_microseconds_PostgreSQL_keeps()
    {
        var truncated = AuditChain.TruncateToMicroseconds(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(12345));

        Assert.Equal(12340, truncated.Ticks - new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks);
        Assert.Equal(DateTimeKind.Utc, truncated.Kind);
    }

    /// <summary>The stored hash format must match what InfoID.Licensing.Server wrote, byte for byte,
    /// or every existing audit row would be reported as tampered.</summary>
    [Fact]
    public void Hash_matches_the_legacy_license_server_implementation()
    {
        var entry = Entry("LicenseCode.Revoked", "ABCDEF", admin: 42);
        var legacy = new LegacyAuditLog
        {
            AdminUserId = entry.AdminUserId,
            Action = entry.Action,
            IPAddress = entry.IPAddress,
            ActionDate = entry.ActionDate,
            DetailsJson = entry.DetailsJson,
            PreviousHash = entry.PreviousHash,
        };
        var legacyHash = typeof(LegacyAuditService)
            .GetMethod("ComputeHash", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [legacy]);

        Assert.Equal(legacyHash, entry.RecordHash);
    }

    [Fact]
    public void Hash_matches_the_legacy_implementation_for_a_first_row_without_admin_or_address()
    {
        var entry = Seal(new AdminAuditLog { Action = "Admin.SignInFailed", ActionDate = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc) });
        var legacy = new LegacyAuditLog { Action = entry.Action, ActionDate = entry.ActionDate };
        var legacyHash = typeof(LegacyAuditService)
            .GetMethod("ComputeHash", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [legacy]);

        Assert.Equal(legacyHash, entry.RecordHash);
    }
}
