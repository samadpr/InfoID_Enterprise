using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Services;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Unit.Application;

public class DashboardServiceTests
{
    [Fact]
    public async Task Summary_combines_license_and_activation_counts()
    {
        var licenses = new InMemoryLicenseRepository
        {
            Counts = new LicenseStatusCounts(Total: 40, Available: 25, Active: 10, Revoked: 5),
        };
        licenses.Expiring[30] = 2;
        licenses.Expiring[60] = 4;
        licenses.Expiring[90] = 7;
        var activations = new InMemoryActivationRepository { Active = 12, Recent = 6 };
        var clock = new TestClock(new DateTimeOffset(2026, 10, 19, 15, 30, 0, TimeSpan.Zero));

        var summary = await new DashboardService(licenses, activations, clock).GetSummaryAsync();

        Assert.Equal(40, summary.TotalCodes);
        Assert.Equal(25, summary.AvailableCodes);
        Assert.Equal(10, summary.ActiveCodes);
        Assert.Equal(5, summary.RevokedCodes);
        Assert.Equal(12, summary.ActiveMachines);
        Assert.Equal(2, summary.ExpiringIn30Days);
        Assert.Equal(4, summary.ExpiringIn60Days);
        Assert.Equal(7, summary.ExpiringIn90Days);
        Assert.Equal(6, summary.ActivationsLast30Days);
    }

    [Fact]
    public async Task Expiry_windows_start_today_in_utc_and_activity_looks_back_thirty_days()
    {
        var licenses = new InMemoryLicenseRepository();
        var activations = new InMemoryActivationRepository();
        // 23:30 UTC on the 19th: "today" must be the 19th regardless of the server's time zone.
        var clock = new TestClock(new DateTimeOffset(2026, 10, 19, 23, 30, 0, TimeSpan.Zero));

        await new DashboardService(licenses, activations, clock).GetSummaryAsync();

        Assert.Equal(
            [(new DateOnly(2026, 10, 19), 30), (new DateOnly(2026, 10, 19), 60), (new DateOnly(2026, 10, 19), 90)],
            licenses.ExpiringCalls);
        Assert.Equal(new DateTime(2026, 9, 19, 23, 30, 0, DateTimeKind.Utc), activations.RequestedSince);
    }
}
