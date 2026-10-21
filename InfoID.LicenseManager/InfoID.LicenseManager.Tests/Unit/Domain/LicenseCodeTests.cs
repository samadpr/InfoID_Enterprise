using InfoID.Licensing.Core;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Domain.Exceptions;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Unit.Domain;

public class LicenseCodeTests
{
    private static readonly DateTime Now = TestData.NowUtc;
    private static readonly DateOnly Today = TestData.Today;

    private static LicenseCode Create(
        LicenseTier tier = LicenseTier.Professional, LicenseType type = LicenseType.Subscription, DateOnly? expiry = null,
        int? maxCards = 500 * 2, int machines = 1) =>
        LicenseCode.Create("ABCDEFGHJKMNPQRS", tier, type, type == LicenseType.Perpetual ? null : expiry ?? Today.AddYears(1),
            maxCards, CardLimitPeriod.Yearly, machines, false, ProductCodes.InfoID, Today);

    // ---- creation invariants ----

    [Fact]
    public void A_new_code_is_available_unused_and_carries_the_requested_terms()
    {
        var code = Create(LicenseTier.Enterprise, expiry: Today.AddMonths(6), maxCards: 2000, machines: 3);

        Assert.Equal(LicenseCodeStatus.Available, code.Status);
        Assert.False(code.IsUsed);
        Assert.Equal("Enterprise", code.Tier);
        Assert.Equal("Subscription", code.LicenseType);
        Assert.Equal(Today.AddMonths(6), code.ExpiryDate);
        Assert.Equal(2000, code.MaxCardsPerPeriod);
        Assert.Equal("Yearly", code.CardLimitPeriod);
        Assert.Equal(3, code.MaxActivations);
        Assert.Equal(ProductCodes.InfoID, code.ProductCode);
    }

    [Fact]
    public void A_perpetual_license_cannot_have_an_expiry_date()
    {
        var ex = Assert.Throws<DomainRuleException>(() => LicenseCode.Create(
            "ABCDEFGHJKMNPQRS", LicenseTier.Standard, LicenseType.Perpetual, Today.AddYears(1), null, CardLimitPeriod.Monthly, 1, false, ProductCodes.InfoID, Today));

        Assert.Equal("A perpetual license must not have an expiry date.", ex.Message);
    }

    [Fact]
    public void A_subscription_needs_an_expiry_date()
    {
        var ex = Assert.Throws<DomainRuleException>(() => LicenseCode.Create(
            "ABCDEFGHJKMNPQRS", LicenseTier.Standard, LicenseType.Subscription, null, null, CardLimitPeriod.Monthly, 1, false, ProductCodes.InfoID, Today));

        Assert.Equal("Subscription and trial licenses need an expiry date.", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(150)]
    [InlineData(-100)]
    [InlineData(102_400)]
    public void The_card_limit_must_be_a_multiple_of_100_within_the_key_format(int limit) =>
        Assert.Throws<DomainRuleException>(() => Create(maxCards: limit));

    [Theory]
    [InlineData(100)]
    [InlineData(102_300)]
    public void The_extreme_valid_card_limits_are_accepted(int limit) => Assert.Equal(limit, Create(maxCards: limit).MaxCardsPerPeriod);

    [Fact]
    public void The_expiry_date_must_be_in_the_future()
    {
        Assert.Throws<DomainRuleException>(() => Create(expiry: Today));
        Assert.Throws<DomainRuleException>(() => Create(expiry: Today.AddDays(-1)));
        Assert.NotNull(Create(expiry: Today.AddDays(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Machines_per_code_are_limited_to_1_to_100(int machines) =>
        Assert.Throws<DomainRuleException>(() => Create(machines: machines));

    // ---- terms ----

    [Fact]
    public void Terms_reflect_the_code_and_overrides_win()
    {
        var code = Create(LicenseTier.Standard, expiry: Today.AddMonths(3));

        var plain = code.ToTerms();
        var overridden = code.ToTerms(LicenseTier.Enterprise, Today.AddYears(2));

        Assert.Equal(LicenseTier.Standard, plain.Tier);
        Assert.Equal(Today.AddMonths(3), plain.ExpiryDate);
        Assert.Equal(LicenseTier.Enterprise, overridden.Tier);
        Assert.Equal(Today.AddYears(2), overridden.ExpiryDate);
        Assert.Equal(code.MaxCardsPerPeriod, overridden.MaxCardsPerPeriod);
        Assert.Equal(CardLimitPeriod.Yearly, overridden.LimitPeriod);
    }

    // ---- revoke / extend / offline ----

    [Fact]
    public void Revoking_ends_every_active_machine_and_stamps_the_date()
    {
        var code = Create(machines: 3);
        code.RegisterActivation(TestData.ActiveMachine("AA"), Now);
        code.RegisterActivation(TestData.ActiveMachine("BB"), Now);

        code.Revoke(Now);

        Assert.Equal(LicenseCodeStatus.Revoked, code.Status);
        Assert.Equal(Now, code.RevokedDate);
        Assert.All(code.Activations, a =>
        {
            Assert.Equal(ActivationStatus.Deactivated, a.Status);
            Assert.Equal(Now, a.DeactivatedDate);
        });
        Assert.Empty(code.ActiveActivations);
    }

    [Fact]
    public void Extending_moves_the_expiry_date()
    {
        var code = Create(expiry: Today.AddMonths(1));

        code.Extend(Today.AddYears(1), Today);

        Assert.Equal(Today.AddYears(1), code.ExpiryDate);
    }

    [Fact]
    public void Extending_to_a_date_that_is_not_in_the_future_is_refused_and_changes_nothing()
    {
        var code = Create(expiry: Today.AddMonths(1));

        Assert.Throws<DomainRuleException>(() => code.Extend(Today, Today));

        Assert.Equal(Today.AddMonths(1), code.ExpiryDate);
    }

    [Fact]
    public void A_perpetual_license_cannot_be_extended()
    {
        var code = Create(type: LicenseType.Perpetual, maxCards: null);

        var ex = Assert.Throws<DomainRuleException>(() => code.Extend(Today.AddYears(1), Today));

        Assert.Equal("Perpetual licenses have no expiry date.", ex.Message);
        Assert.Null(code.ExpiryDate);
    }

    // ---- machine slots ----

    [Fact]
    public void Freeing_slots_deactivates_each_machine_records_a_transfer_and_makes_the_code_available_again()
    {
        var code = Create(machines: 2);
        code.RegisterActivation(TestData.ActiveMachine("AA"), Now);
        code.RegisterActivation(TestData.ActiveMachine("BB"), Now);

        var history = code.FreeMachineSlots(Now, "Customer moved to a new PC");

        Assert.Equal(2, history.Count);
        Assert.All(history, h =>
        {
            Assert.Same(code, h.LicenseCode);
            Assert.NotNull(h.OldActivationRecord);
            Assert.Equal("Customer moved to a new PC", h.Reason);
            Assert.Equal(Now, h.TransferDate);
        });
        Assert.Empty(code.ActiveActivations);
        Assert.Equal(LicenseCodeStatus.Available, code.Status);
    }

    [Fact]
    public void Freeing_slots_of_a_revoked_code_keeps_it_revoked()
    {
        var code = Create();
        code.RegisterActivation(TestData.ActiveMachine("AA"), Now);
        code.Revoke(Now);

        var history = code.FreeMachineSlots(Now, "cleanup");

        Assert.Empty(history);
        Assert.Equal(LicenseCodeStatus.Revoked, code.Status);
    }

    [Fact]
    public void Freeing_slots_of_a_code_with_no_machines_changes_nothing()
    {
        var code = Create();

        Assert.Empty(code.FreeMachineSlots(Now, "nothing to free"));
        Assert.Equal(LicenseCodeStatus.Available, code.Status);
    }

    [Fact]
    public void Registering_an_activation_marks_the_code_used_and_active()
    {
        var code = Create();

        code.RegisterActivation(TestData.ActiveMachine("AA"), Now);

        Assert.True(code.IsUsed);
        Assert.Equal(LicenseCodeStatus.Active, code.Status);
        Assert.Single(code.ActiveActivations);
    }

    [Fact]
    public void Re_activating_the_same_machine_replaces_its_record_instead_of_using_another_slot()
    {
        var code = Create(machines: 1);
        var first = TestData.ActiveMachine("AA");
        code.RegisterActivation(first, Now);

        var second = TestData.ActiveMachine("AA");
        code.RegisterActivation(second, Now.AddDays(1));

        Assert.Equal(ActivationStatus.Deactivated, first.Status);
        Assert.Equal(Now.AddDays(1), first.DeactivatedDate);
        Assert.Same(second, Assert.Single(code.ActiveActivations));
        Assert.Equal(2, code.Activations.Count);
    }

    [Fact]
    public void Cancelled_activation_records_do_not_count_as_active()
    {
        var code = Create();
        var record = TestData.ActiveMachine("AA");
        record.Cancelled = true;
        code.Activations.Add(record);

        Assert.Empty(code.ActiveActivations);
    }
}
