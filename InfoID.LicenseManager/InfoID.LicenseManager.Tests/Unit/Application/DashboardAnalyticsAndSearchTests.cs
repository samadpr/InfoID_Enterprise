using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Dashboard;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Services;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Unit.Application;

public class DashboardAnalyticsServiceTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(TestData.NowUtc);

    /// <summary>A stored code in the given state (an already-expired code cannot be created through the domain, only reached by time passing).</summary>
    private static void Code(ServiceHarness h, string status, DateOnly expiry)
    {
        var code = h.Licenses.Add(TestData.NewLicense());
        code.Status = status;
        code.ExpiryDate = expiry;
    }

    [Fact]
    public async Task The_default_window_is_the_last_30_days_with_a_row_for_every_day()
    {
        var h = new ServiceHarness();
        h.Activations.PerDay.Add(new DailyCount(Today, 3));
        h.Activations.PerDay.Add(new DailyCount(Today.AddDays(-5), 2));
        h.Activations.PerDay.Add(new DailyCount(Today.AddDays(-45), 9)); // outside the window

        var overview = await h.Analytics.GetOverviewAsync();

        Assert.Equal(Today.AddDays(-29), overview.From);
        Assert.Equal(Today, overview.To);
        Assert.Equal(30, overview.ActivationTrend.Count);
        Assert.Equal(overview.From, overview.ActivationTrend[0].Day);
        Assert.Equal(3, overview.ActivationTrend[^1].Count);
        Assert.Equal(2, overview.ActivationTrend.Single(d => d.Day == Today.AddDays(-5)).Count);
        Assert.Equal(5, overview.ActivationsInRange);
        Assert.Equal(9, overview.ActivationsInPreviousRange); // the 30 days before the window
        Assert.Equal(0, overview.ActivationTrend.Count(d => d.Count > 0 && d.Day < Today.AddDays(-29)));
    }

    [Fact]
    public async Task The_trend_asks_the_database_for_whole_utc_days()
    {
        var h = new ServiceHarness();

        await h.Analytics.GetOverviewAsync(new DashboardRangeDto(Today.AddDays(-6), Today));

        var (from, toExclusive) = h.Activations.RequestedRanges[0];
        Assert.Equal(Today.AddDays(-6).ToDateTime(TimeOnly.MinValue), from);
        Assert.Equal(Today.AddDays(1).ToDateTime(TimeOnly.MinValue), toExclusive);

        // ...and, to compare, the 7 days before that.
        var (previousFrom, previousTo) = h.Activations.RequestedRanges[1];
        Assert.Equal(Today.AddDays(-13).ToDateTime(TimeOnly.MinValue), previousFrom);
        Assert.Equal(Today.AddDays(-6).ToDateTime(TimeOnly.MinValue), previousTo);
    }

    [Theory]
    [InlineData(0, 0, 0, 30)] // upside-down range falls back to the default length
    [InlineData(-10, 5, 0, 11)] // the future is cut off at today
    [InlineData(-1000, 0, -365, 366)] // at most 366 days
    public void Ranges_are_clamped(int fromOffset, int toOffset, int expectedFromOffset, int expectedDays)
    {
        var range = fromOffset == 0 && toOffset == 0
            ? new DashboardRangeDto(Today.AddDays(3), Today)
            : new DashboardRangeDto(Today.AddDays(fromOffset), Today.AddDays(toOffset));

        var (from, to) = DashboardAnalyticsService.Clamp(range, Today);

        Assert.True(to <= Today);
        Assert.Equal(expectedDays, to.DayNumber - from.DayNumber + 1);
        if (expectedFromOffset != 0) Assert.Equal(Today.AddDays(expectedFromOffset), from);
    }

    [Fact]
    public async Task Counts_come_from_the_repositories_and_never_double_count()
    {
        var h = new ServiceHarness();
        var plan = h.Plans.Seed("Standard");
        h.Plans.Seed("Hidden", active: false, order: 2);
        h.Accounts.Seed("a@c.test");
        h.Accounts.Seed("b@c.test", disabled: true);
        h.Requests.Seed(plan);
        h.Requests.Seed(plan, "p@c.test", status: LicenseRequestStatus.InProgress);
        h.Requests.Seed(plan, "d@c.test", status: LicenseRequestStatus.Approved);
        Code(h, LicenseCodeStatus.Available, Today.AddDays(200));
        Code(h, LicenseCodeStatus.Revoked, Today.AddDays(200));
        Code(h, LicenseCodeStatus.Available, Today.AddDays(-1)); // past its expiry date
        Code(h, LicenseCodeStatus.Active, Today.AddDays(10));

        var overview = await h.Analytics.GetOverviewAsync();

        Assert.Equal(2, overview.PendingRequests);
        Assert.Equal(1, overview.NewRequests);
        Assert.Equal(2, overview.TotalCustomers);
        Assert.Equal(1, overview.ActivePlans);
        Assert.Equal(new LicenseLifecycleDto(Available: 1, Active: 1, Expired: 1, Revoked: 1), overview.Lifecycle);
        Assert.Equal(h.Licenses.Codes.Count, overview.Lifecycle.Available + overview.Lifecycle.Active + overview.Lifecycle.Expired + overview.Lifecycle.Revoked);
        Assert.Equal([("New", 1), ("In progress", 1), ("Approved", 1), ("Rejected", 0)], overview.RequestsByStatus.Select(r => (r.Label, r.Count)));
    }

    [Fact]
    public async Task Top_customers_are_ranked_by_licenses_and_recent_activity_comes_from_the_audit_log()
    {
        var h = new ServiceHarness();
        var big = new CustomerRecord { OrganizationName = "Big School" };
        var small = new CustomerRecord { OrganizationName = "Small Co" };
        for (var i = 0; i < 3; i++) h.Licenses.Add(TestData.NewLicense()).CustomerRecord = big;
        h.Licenses.Add(TestData.NewLicense()).CustomerRecord = small;
        h.Licenses.Add(TestData.NewLicense()); // no customer
        await new AuditService(h.Audit, h.CurrentUser, h.Clock).LogAsync("LicenseCode.Revoked", new { reason = "test" });

        var overview = await h.Analytics.GetOverviewAsync();

        Assert.Equal([("Big School", 3), ("Small Co", 1)], overview.TopCustomers.Select(t => (t.Name, t.Licenses)));
        Assert.Equal("LicenseCode.Revoked", Assert.Single(overview.RecentActivity).Action);
    }

    [Fact]
    public async Task The_pending_badge_counts_new_and_in_progress_requests_only()
    {
        var h = new ServiceHarness(AdminRoles.SupportAgent);
        var plan = h.Plans.Seed();
        h.Requests.Seed(plan);
        h.Requests.Seed(plan, "x@c.test", status: LicenseRequestStatus.InProgress);
        h.Requests.Seed(plan, "y@c.test", status: LicenseRequestStatus.Rejected);

        Assert.Equal(2, await h.Analytics.GetPendingRequestCountAsync());
    }
}

public class GlobalSearchServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" a ")]
    public async Task Too_short_queries_search_nothing(string? query)
    {
        var h = new ServiceHarness();
        h.Accounts.Seed("a@c.test");

        var result = await h.GlobalSearch.SearchAsync(query);

        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task One_query_finds_codes_customers_requests_and_plans()
    {
        var h = new ServiceHarness();
        var plan = h.Plans.Seed("Acme Special");
        h.Accounts.Seed("owner@acme.test", fullName: "Olga Acme", company: "Acme");
        h.Requests.Seed(plan, "buyer@acme.test", "Bea Buyer", "Acme AS");
        h.Licenses.Add(TestData.NewLicense()).CustomerRecord = new CustomerRecord { OrganizationName = "Acme Schools", ContactEmail = "x@acme.test" };

        var result = await h.GlobalSearch.SearchAsync("  acme ");

        Assert.Equal("acme", result.Query);
        Assert.Single(result.Customers);
        Assert.Single(result.Requests);
        Assert.Single(result.Plans);
        Assert.Equal("acme", h.Licenses.LastSearch!.Text);
    }

    [Fact]
    public async Task Every_list_is_capped_and_overlong_text_is_cut()
    {
        var h = new ServiceHarness();
        for (var i = 0; i < 20; i++) h.Accounts.Seed($"user{i}@acme.test");

        var result = await h.GlobalSearch.SearchAsync(new string('a', 500));
        var capped = await h.GlobalSearch.SearchAsync("acme");

        Assert.Equal(GlobalSearchService.MaxLength, result.Query.Length);
        Assert.Equal(GlobalSearchService.PerKind, capped.Customers.Count);
    }

    [Fact]
    public async Task The_search_keeps_the_role_rules_of_the_underlying_lists()
    {
        var h = new ServiceHarness("Intruder");

        await Assert.ThrowsAnyAsync<Exception>(() => h.GlobalSearch.SearchAsync("acme"));
    }
}
