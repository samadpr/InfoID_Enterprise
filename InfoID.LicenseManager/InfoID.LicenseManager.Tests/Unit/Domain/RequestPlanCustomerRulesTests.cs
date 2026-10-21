using InfoID.Licensing.Core;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Domain.Exceptions;
using InfoID.LicenseManager.Domain.ValueObjects;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Unit.Domain;

public class LicenseRequestTests
{
    private static readonly DateTime Now = TestData.NowUtc;

    private static LicenseRequest NewRequest(string status = LicenseRequestStatus.New) => new() { Id = 5, Status = status, Email = "a@b.test", FullName = "A B" };

    [Theory]
    [InlineData(LicenseRequestStatus.New, true)]
    [InlineData(LicenseRequestStatus.InProgress, true)]
    [InlineData(LicenseRequestStatus.Approved, false)]
    [InlineData(LicenseRequestStatus.Rejected, false)]
    public void Only_new_and_in_progress_requests_are_open(string status, bool open) =>
        Assert.Equal(open, NewRequest(status).IsOpen);

    [Fact]
    public void Marking_in_progress_records_who_and_when()
    {
        var request = NewRequest();

        request.MarkInProgress(9, Now);

        Assert.Equal(LicenseRequestStatus.InProgress, request.Status);
        Assert.Equal(9, request.HandledByAdminUserId);
        Assert.Equal(Now, request.HandledDate);
    }

    [Fact]
    public void Approving_links_the_code_clears_any_reason_and_closes_the_request()
    {
        var request = NewRequest(LicenseRequestStatus.InProgress);
        request.StatusReason = "stale";
        var code = TestData.NewLicense();

        request.Approve(code, 9, Now);

        Assert.Equal(LicenseRequestStatus.Approved, request.Status);
        Assert.Same(code, request.LicenseCode);
        Assert.Null(request.StatusReason);
        Assert.False(request.IsOpen);
    }

    [Fact]
    public void Rejecting_needs_a_reason_and_stores_it_trimmed()
    {
        var request = NewRequest();

        Assert.Throws<DomainRuleException>(() => request.Reject("  ", 9, Now));
        Assert.Equal(LicenseRequestStatus.New, request.Status);

        request.Reject("  Payment not received  ", 9, Now);

        Assert.Equal(LicenseRequestStatus.Rejected, request.Status);
        Assert.Equal("Payment not received", request.StatusReason);
    }

    [Theory]
    [InlineData(LicenseRequestStatus.Approved)]
    [InlineData(LicenseRequestStatus.Rejected)]
    public void A_decided_request_cannot_be_decided_again(string status)
    {
        var request = NewRequest(status);

        var message = $"This request is already {status.ToLowerInvariant()}.";
        Assert.Equal(message, Assert.Throws<DomainRuleException>(() => request.MarkInProgress(1, Now)).Message);
        Assert.Equal(message, Assert.Throws<DomainRuleException>(() => request.Approve(TestData.NewLicense(), 1, Now)).Message);
        Assert.Equal(message, Assert.Throws<DomainRuleException>(() => request.Reject("x", 1, Now)).Message);
        Assert.Equal(status, request.Status);
        Assert.Null(request.HandledDate);
    }
}

public class PlanTests
{
    private static PlanDetails Details(
        string name = "Pro", LicenseTier tier = LicenseTier.Professional, int? months = 12, int machines = 2, int? cards = null,
        string? tagline = "Schools", string? price = "Contact sales") =>
        new(name, tier, tagline, "A\nB", months, machines, cards, CardLimitPeriod.Monthly, price, 2, true);

    [Fact]
    public void A_valid_plan_is_created_with_trimmed_text_and_the_given_product()
    {
        var plan = Plan.Create(ProductCodes.InfoID, Details(name: "  Pro  ", tagline: "  ", price: " Contact sales "));

        Assert.Equal("Pro", plan.Name);
        Assert.Equal("Professional", plan.Tier);
        Assert.Null(plan.Tagline);
        Assert.Equal("Contact sales", plan.PriceText);
        Assert.Equal(ProductCodes.InfoID, plan.ProductCode);
        Assert.Equal(12, plan.DurationMonths);
    }

    [Theory]
    [InlineData("", 12, 1, null, "Enter a plan name.")]
    [InlineData("P", 0, 1, null, "Duration must be 1-120 months, or empty for perpetual.")]
    [InlineData("P", 121, 1, null, "Duration must be 1-120 months, or empty for perpetual.")]
    [InlineData("P", 12, 0, null, "Machines must be between 1 and 100.")]
    [InlineData("P", 12, 101, null, "Machines must be between 1 and 100.")]
    [InlineData("P", 12, 1, 150, "The card limit must be a multiple of 100 between 100 and 102,300, or empty for unlimited.")]
    [InlineData("P", 12, 1, 0, "The card limit must be a multiple of 100 between 100 and 102,300, or empty for unlimited.")]
    [InlineData("P", 12, 1, 102_400, "The card limit must be a multiple of 100 between 100 and 102,300, or empty for unlimited.")]
    public void Invalid_plans_are_refused_with_a_clear_message(string name, int months, int machines, int? cards, string expected)
    {
        var ex = Assert.Throws<DomainRuleException>(() => Plan.Create(ProductCodes.InfoID, Details(name, months: months, machines: machines, cards: cards)));

        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public void A_perpetual_plan_has_no_duration_and_unlimited_cards_are_allowed()
    {
        var plan = Plan.Create(ProductCodes.InfoID, Details(months: null, cards: null));

        Assert.Null(plan.DurationMonths);
        Assert.Null(plan.MaxCardsPerPeriod);
    }

    [Fact]
    public void An_invalid_update_changes_nothing()
    {
        var plan = Plan.Create(ProductCodes.InfoID, Details(name: "Original"));

        Assert.Throws<DomainRuleException>(() => plan.Update(Details(name: "Changed", machines: 0)));

        Assert.Equal("Original", plan.Name);
        Assert.Equal(2, plan.DefaultMachines);
    }

    [Fact]
    public void Overlong_text_is_refused()
    {
        Assert.Throws<DomainRuleException>(() => Plan.Create(ProductCodes.InfoID, Details(name: new string('x', 151))));
        Assert.Throws<DomainRuleException>(() => Plan.Create(ProductCodes.InfoID, Details(tagline: new string('x', 301))));
        Assert.Throws<DomainRuleException>(() => Plan.Create(ProductCodes.InfoID, Details(price: new string('x', 101))));
    }
}

public class CustomerAccountTests
{
    [Fact]
    public void Disabling_uses_the_soft_delete_flag_and_enabling_restores_it()
    {
        var account = new CustomerAccount { Email = "a@b.test" };
        Assert.False(account.IsDisabled);

        account.Disable();
        Assert.True(account.IsDisabled);
        Assert.True(account.Cancelled);

        account.Enable();
        Assert.False(account.IsDisabled);
        Assert.False(account.Cancelled);
    }
}
