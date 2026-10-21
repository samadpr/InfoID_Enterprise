using InfoID.Licensing.Core;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Domain.Rules;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Unit.Domain;

public class ActivationRulesTests
{
    private static readonly DateOnly Today = TestData.Today;
    private static readonly DateTime Now = TestData.NowUtc;

    private static IReadOnlyList<ActivationProblem> Evaluate(LicenseCode code, string fingerprint = "FFFF", bool blacklisted = false, DateOnly? requestDate = null) =>
        ActivationRules.Evaluate(code, fingerprint, blacklisted, requestDate ?? Today, Today);

    [Fact]
    public void A_fresh_code_and_request_have_no_problems() => Assert.Empty(Evaluate(TestData.NewLicense()));

    [Fact]
    public void A_revoked_code_is_refused()
    {
        var code = TestData.NewLicense();
        code.Revoke(Now);

        var problem = Assert.Single(Evaluate(code));

        Assert.Equal("This activation code has been revoked.", problem.Message);
        Assert.True(problem.IsBlocking);
    }

    [Fact]
    public void A_full_code_refuses_a_new_machine_and_names_the_limit()
    {
        var code = TestData.NewLicense(maxActivations: 2);
        code.RegisterActivation(TestData.ActiveMachine("AA"), Now);
        code.RegisterActivation(TestData.ActiveMachine("BB"), Now);

        var problem = Assert.Single(Evaluate(code, "CC"));

        Assert.StartsWith("All 2 activation slot(s) are in use.", problem.Message);
        Assert.True(problem.IsBlocking);
    }

    [Fact]
    public void A_full_code_still_accepts_a_machine_that_already_holds_it()
    {
        var code = TestData.NewLicense(maxActivations: 1);
        code.RegisterActivation(TestData.ActiveMachine("AA"), Now);

        Assert.Empty(Evaluate(code, "AA"));
        Assert.True(ActivationRules.IsSameMachineActive(code, "AA"));
        Assert.False(ActivationRules.IsSameMachineActive(code, "BB"));
    }

    [Fact]
    public void A_deactivated_machine_does_not_hold_a_slot()
    {
        var code = TestData.NewLicense(maxActivations: 1);
        code.RegisterActivation(TestData.ActiveMachine("AA"), Now);
        code.FreeMachineSlots(Now, "moved");

        Assert.Empty(Evaluate(code, "BB"));
    }

    [Fact]
    public void An_expired_license_is_refused_but_one_expiring_today_is_not()
    {
        var expired = TestData.NewLicense();
        expired.ExpiryDate = Today.AddDays(-1);
        var lastDay = TestData.NewLicense();
        lastDay.ExpiryDate = Today;

        Assert.Contains("expired on", Assert.Single(Evaluate(expired)).Message);
        Assert.Empty(Evaluate(lastDay));
    }

    [Fact]
    public void A_blacklisted_machine_or_code_is_refused() =>
        Assert.Equal("This machine or activation code is blacklisted.", Assert.Single(Evaluate(TestData.NewLicense(), blacklisted: true)).Message);

    [Fact]
    public void An_old_request_code_is_only_a_warning()
    {
        var problem = Assert.Single(Evaluate(TestData.NewLicense(), requestDate: Today.AddDays(-31)));

        Assert.Contains("more than 30 days ago", problem.Message);
        Assert.False(problem.IsBlocking);
    }

    [Fact]
    public void A_request_exactly_thirty_days_old_is_fine() =>
        Assert.Empty(Evaluate(TestData.NewLicense(), requestDate: Today.AddDays(-30)));

    [Fact]
    public void Several_problems_are_all_reported()
    {
        var code = TestData.NewLicense();
        code.ExpiryDate = Today.AddDays(-3);
        code.Revoke(Now);

        var problems = Evaluate(code, blacklisted: true, requestDate: Today.AddDays(-60));

        Assert.Equal(4, problems.Count);
        Assert.Single(problems, p => !p.IsBlocking);
    }
}
