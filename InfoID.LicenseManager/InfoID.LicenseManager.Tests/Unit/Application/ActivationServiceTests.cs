using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Activations;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Unit.Application;

/// <summary>The serial-key workflow, including the part that matters most for compatibility: a key the
/// service issues must validate with exactly the calls InfoID.Desktop makes on the customer's machine.</summary>
public class ActivationServiceTests
{
    private const string Machine = "CUSTOMER-PC-1";

    private static string RequestCodeFor(LicenseCode license, string machine = Machine, DateOnly? requestDate = null)
    {
        ActivationCode.TryParse(license.ActivationCode, out var code);
        var soft = new SoftCode(code!, TestData.Fingerprint(machine), requestDate ?? TestData.Today);
        return soft.Encode();
    }

    private static IssueSerialKeyRequestDto Issue(string softCode, LicenseTier? tier = null, DateOnly? expiry = null) => new(softCode, tier, expiry);

    private static SerialKeyValidation ValidateLikeTheDesktopClient(string serialKey, LicenseCode license, string machine = Machine, params LicenseSecret[] secrets)
    {
        ActivationCode.TryParse(license.ActivationCode, out var code);
        return SerialKey.Validate(serialKey, code!, TestData.Fingerprint(machine), secrets.Length == 0 ? [TestSecretProvider.V1] : secrets);
    }

    // ---------------------------------------------------------------- preview ----

    [Fact]
    public async Task Preview_decodes_the_request_and_describes_the_license()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(LicenseTier.Enterprise, maxActivations: 2, maxCards: 3000, code: TestData.KnownCode));
        license.CustomerRecord = new CustomerRecord { OrganizationName = "Acme AS" };
        license.RegisterActivation(TestData.ActiveMachine("OTHER"), TestData.NowUtc);

        var result = await h.Activation.PreviewAsync(RequestCodeFor(license));

        var preview = result.Value!;
        Assert.Equal(TestData.KnownCodeDisplay, preview.ActivationCode);
        Assert.Equal(TestData.Fingerprint(Machine).Hex, preview.FingerprintHex);
        Assert.Equal(license.Id, preview.LicenseId);
        Assert.Equal("Enterprise", preview.Tier);
        Assert.Equal(3000, preview.MaxCardsPerPeriod);
        Assert.Equal("Acme AS", preview.Customer);
        Assert.Equal(1, preview.ActiveMachines);
        Assert.Equal(2, preview.MaxActivations);
        Assert.False(preview.IsReactivationOfSameMachine);
        Assert.Empty(preview.Problems);
        Assert.True(preview.CanIssue);
        Assert.Equal(0, h.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Preview_of_an_unknown_code_says_so_and_cannot_be_issued()
    {
        var h = new ServiceHarness();
        var unknown = TestData.NewLicense(); // never added to the repository

        var preview = (await h.Activation.PreviewAsync(RequestCodeFor(unknown))).Value!;

        Assert.Null(preview.LicenseId);
        Assert.Equal("This activation code does not exist in the license database.", Assert.Single(preview.Problems).Message);
        Assert.False(preview.CanIssue);
    }

    [Fact]
    public async Task Preview_lists_blocking_problems_and_advisories_separately()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense());
        license.Revoke(TestData.NowUtc);

        var preview = (await h.Activation.PreviewAsync(RequestCodeFor(license, requestDate: TestData.Today.AddDays(-45)))).Value!;

        Assert.Equal(2, preview.Problems.Count);
        Assert.Single(preview.Problems, p => p.IsBlocking);
        Assert.Single(preview.Problems, p => !p.IsBlocking);
        Assert.False(preview.CanIssue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a request code")]
    [InlineData("AAAAA-AAAAA-AAAAA-AAAAA-AAAAA-AAAAA-AAAAA-AAAAA")]
    public async Task Preview_rejects_text_that_is_not_a_request_code(string text)
    {
        var h = new ServiceHarness();

        var result = await h.Activation.PreviewAsync(text);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors[0]);
    }

    [Fact]
    public async Task Preview_rejects_a_request_code_with_a_typo()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense());
        var request = RequestCodeFor(license);
        var typo = (request[0] == '2' ? "3" : "2") + request[1..];

        var result = await h.Activation.PreviewAsync(typo);

        Assert.False(result.Succeeded);
    }

    // ---------------------------------------------------------------- issuing ----

    [Fact]
    public async Task An_issued_key_validates_on_the_customers_machine_with_the_licensed_terms()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(LicenseTier.Professional, expiry: TestData.Today.AddMonths(8), maxCards: 2500));

        var result = await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license)));

        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        var issued = result.Value!;
        Assert.Matches("^[0-9A-Z]{5}(-[0-9A-Z]{5}){3}$", issued.SerialKey);

        var validation = ValidateLikeTheDesktopClient(issued.SerialKey, license);
        Assert.True(validation.IsValid, validation.Error);
        Assert.Equal(LicenseTier.Professional, validation.Terms!.Tier);
        Assert.Equal(LicenseType.Subscription, validation.Terms.Type);
        Assert.Equal(TestData.Today.AddMonths(8), validation.Terms.ExpiryDate);
        Assert.Equal(2500, validation.Terms.MaxCardsPerPeriod);
        Assert.Equal(CardLimitPeriod.Monthly, validation.Terms.LimitPeriod);
    }

    [Fact]
    public async Task An_issued_key_is_rejected_on_a_different_machine_or_for_a_different_code()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense());
        var other = h.Licenses.Add(TestData.NewLicense());

        var key = (await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license)))).Value!.SerialKey;

        Assert.False(ValidateLikeTheDesktopClient(key, license, "ANOTHER-PC").IsValid);
        Assert.False(ValidateLikeTheDesktopClient(key, other).IsValid);
    }

    [Fact]
    public async Task The_key_is_exactly_what_Licensing_Core_would_make_for_the_same_inputs()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(LicenseTier.Enterprise, expiry: TestData.Today.AddDays(200), maxCards: 100));
        ActivationCode.TryParse(license.ActivationCode, out var code);

        var key = (await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license)))).Value!.SerialKey;

        var expected = SerialKey.Generate(license.ToTerms(), code!, TestData.Fingerprint(Machine), TestSecretProvider.V1);
        Assert.Equal(expected, key);
    }

    [Fact]
    public async Task The_highest_secret_version_signs_and_older_versions_still_validate_older_keys()
    {
        var h = new ServiceHarness(secrets: new TestSecretProvider(TestSecretProvider.V1, TestSecretProvider.V2));
        var license = h.Licenses.Add(TestData.NewLicense());

        var key = (await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license)))).Value!.SerialKey;

        Assert.True(ValidateLikeTheDesktopClient(key, license, Machine, TestSecretProvider.V1, TestSecretProvider.V2).IsValid);
        Assert.False(ValidateLikeTheDesktopClient(key, license, Machine, TestSecretProvider.V1).IsValid); // V2-signed: a client with only V1 refuses
    }

    [Fact]
    public async Task Issuing_records_the_activation_and_marks_the_code_active()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(LicenseTier.Standard));
        var request = RequestCodeFor(license);

        var key = (await h.Activation.IssueSerialKeyAsync(Issue(request, LicenseTier.Enterprise))).Value!.SerialKey;

        var activation = Assert.Single(license.Activations);
        Assert.Equal(TestData.Fingerprint(Machine).Hex, activation.MachineFingerprint);
        Assert.Equal(key, activation.SerialKeyIssued);
        Assert.Equal("Offline", activation.ActivationMode);
        Assert.Equal(ActivationStatus.Active, activation.Status);
        Assert.Equal("Enterprise", activation.IssuedTier);
        Assert.Equal(7, activation.GeneratedByAdminUserId);
        Assert.Equal(TestData.NowUtc, activation.ActivationDate);
        Assert.Equal(InMemoryLicenseRepository.Normalize(request), activation.SoftCode);
        Assert.Equal(LicenseCodeStatus.Active, license.Status);
        Assert.True(license.IsUsed);
        Assert.Equal(1, h.UnitOfWork.Commits);
        Assert.Equal([license.Id], h.Licenses.LockedIds);
    }

    [Fact]
    public async Task Tier_and_expiry_overrides_go_into_the_key_and_the_activation_record_but_not_the_code()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(LicenseTier.Standard, expiry: TestData.Today.AddMonths(2)));
        var newExpiry = TestData.Today.AddMonths(18);

        var issued = (await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license), LicenseTier.Enterprise, newExpiry))).Value!;

        Assert.Equal(LicenseTier.Enterprise, issued.Tier);
        Assert.Equal(newExpiry, issued.ExpiryDate);
        var validation = ValidateLikeTheDesktopClient(issued.SerialKey, license);
        Assert.Equal(LicenseTier.Enterprise, validation.Terms!.Tier);
        Assert.Equal(newExpiry, validation.Terms.ExpiryDate);
        Assert.Equal("Standard", license.Tier);
        Assert.Equal(TestData.Today.AddMonths(2), license.ExpiryDate);
        Assert.Equal(newExpiry, license.Activations[0].IssuedExpiryDate);
    }

    [Fact]
    public async Task An_expiry_override_on_a_perpetual_license_is_refused()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(type: LicenseType.Perpetual));

        var result = await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license), expiry: TestData.Today.AddYears(1)));

        Assert.False(result.Succeeded);
        Assert.Equal("A perpetual license must not have an expiry date.", result.Errors[0]);
        Assert.Empty(license.Activations);
    }

    [Fact]
    public async Task A_support_agent_may_issue_keys_but_not_override_terms()
    {
        var h = new ServiceHarness(AdminRoles.SupportAgent);
        var license = h.Licenses.Add(TestData.NewLicense());
        var request = RequestCodeFor(license);

        await Assert.ThrowsAsync<ForbiddenException>(() => h.Activation.IssueSerialKeyAsync(Issue(request, LicenseTier.Enterprise)));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Activation.IssueSerialKeyAsync(Issue(request, expiry: TestData.Today.AddYears(5))));
        Assert.Empty(license.Activations);

        var plain = await h.Activation.IssueSerialKeyAsync(Issue(request));
        Assert.True(plain.Succeeded);
    }

    [Fact]
    public async Task Someone_who_is_not_staff_cannot_issue_or_preview()
    {
        var h = new ServiceHarness(role: "Intruder");
        var license = h.Licenses.Add(TestData.NewLicense());

        await Assert.ThrowsAsync<ForbiddenException>(() => h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license))));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Activation.PreviewAsync(RequestCodeFor(license)));
    }

    // ---------------------------------------------------------------- refusals ----

    [Fact]
    public async Task A_full_code_refuses_a_second_machine_but_not_the_first_one_again()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(maxActivations: 1));

        var first = await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license, "PC-A")));
        var second = await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license, "PC-B")));
        var firstAgain = await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license, "PC-A")));

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.StartsWith("All 1 activation slot(s) are in use.", second.Errors[0]);
        Assert.True(firstAgain.Succeeded);
        Assert.Equal(first.Value!.SerialKey, firstAgain.Value!.SerialKey); // same inputs, same key
    }

    [Fact]
    public async Task Re_issuing_for_the_same_machine_retires_the_old_record_instead_of_using_a_new_slot()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(maxActivations: 1));

        await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license)));
        await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license)));

        Assert.Equal(2, license.Activations.Count);
        Assert.Single(license.ActiveActivations);
    }

    [Fact]
    public async Task Revoked_expired_and_blacklisted_codes_get_no_key_and_nothing_is_saved()
    {
        var h = new ServiceHarness();
        var revoked = h.Licenses.Add(TestData.NewLicense());
        revoked.Revoke(TestData.NowUtc);
        var expired = h.Licenses.Add(TestData.NewLicense());
        expired.ExpiryDate = TestData.Today.AddDays(-2);
        var blocked = h.Licenses.Add(TestData.NewLicense());
        h.Blacklist.Fingerprints.Add(TestData.Fingerprint("BLOCKED-PC").Hex);

        var r1 = await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(revoked)));
        var r2 = await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(expired)));
        var r3 = await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(blocked, "BLOCKED-PC")));

        Assert.Equal("This activation code has been revoked.", r1.Errors[0]);
        Assert.Contains("expired on", r2.Errors[0]);
        Assert.Equal("This machine or activation code is blacklisted.", r3.Errors[0]);
        Assert.Equal(0, h.UnitOfWork.Saves);
        Assert.All(new[] { revoked, expired, blocked }, l => Assert.Empty(l.Activations));
        Assert.Empty(h.Audit.Rows);
    }

    [Fact]
    public async Task An_old_request_code_still_gets_a_key_because_the_age_check_is_only_advice()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense());

        var result = await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license, requestDate: TestData.Today.AddDays(-40))));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task An_unknown_activation_code_gets_no_key()
    {
        var h = new ServiceHarness();

        var result = await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(TestData.NewLicense())));

        Assert.Equal("This activation code does not exist in the license database.", result.Errors[0]);
    }

    [Fact]
    public async Task Without_a_configured_secret_no_key_is_made_and_the_message_names_the_setting_not_a_value()
    {
        var h = new ServiceHarness(secrets: TestSecretProvider.None);
        var license = h.Licenses.Add(TestData.NewLicense());

        var result = await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license)));

        Assert.False(result.Succeeded);
        Assert.Contains("INFOID_LICENSE_SECRET_V1", result.Errors[0]);
        Assert.Empty(license.Activations);
    }

    [Fact]
    public async Task An_invalid_edition_override_is_rejected()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense());

        var result = await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license), (LicenseTier)9));

        Assert.False(result.Succeeded);
    }

    // ---------------------------------------------------------------- audit ----

    [Fact]
    public async Task The_audit_entry_never_contains_the_serial_key_or_a_secret()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(code: TestData.KnownCode));

        var key = (await h.Activation.IssueSerialKeyAsync(Issue(RequestCodeFor(license), LicenseTier.Enterprise))).Value!.SerialKey;

        var entry = Assert.Single(h.Audit.Rows);
        Assert.Equal("SerialKey.Issued", entry.Action);
        Assert.DoesNotContain(key, entry.DetailsJson);
        Assert.DoesNotContain(key.Replace("-", ""), entry.DetailsJson);
        Assert.Contains(TestData.KnownCodeDisplay, entry.DetailsJson);
        Assert.Contains("\"Overridden\":true", entry.DetailsJson);
        Assert.Equal(7, entry.AdminUserId);
    }

    // ---------------------------------------------------------------- simulator ----

    [Fact]
    public async Task The_simulator_round_trip_matches_the_real_workflow()
    {
        var h = new ServiceHarness(AdminRoles.SuperAdmin);
        var license = h.Licenses.Add(TestData.NewLicense(LicenseTier.Enterprise, maxCards: 500));

        var request = h.Simulator.CreateRequestCode(ActivationCodeFormat.Display(license.ActivationCode), "SIM-PC").Value!;
        var key = (await h.Activation.IssueSerialKeyAsync(Issue(request.RequestCode))).Value!.SerialKey;
        var good = h.Simulator.VerifySerialKey(ActivationCodeFormat.Display(license.ActivationCode), "SIM-PC", key);
        var otherPc = h.Simulator.VerifySerialKey(ActivationCodeFormat.Display(license.ActivationCode), "OTHER-PC", key);

        Assert.True(good.Succeeded);
        Assert.Equal(LicenseTier.Enterprise, good.Value!.Tier);
        Assert.Equal(500, good.Value.MaxCardsPerPeriod);
        Assert.False(otherPc.Succeeded);
        Assert.StartsWith("InfoID would REJECT this key:", otherPc.Errors[0]);
    }

    [Theory]
    [InlineData(AdminRoles.LicenseManager)]
    [InlineData(AdminRoles.SupportAgent)]
    public void Only_super_admins_may_use_the_simulator(string role)
    {
        var h = new ServiceHarness(role);

        Assert.Throws<ForbiddenException>(() => h.Simulator.CreateRequestCode("AAAA-AAAA-AAAA-AAAA", "PC"));
        Assert.Throws<ForbiddenException>(() => h.Simulator.VerifySerialKey("AAAA-AAAA-AAAA-AAAA", "PC", "x"));
    }

    [Fact]
    public void The_simulator_rejects_a_mistyped_activation_code_and_a_missing_machine_name()
    {
        var h = new ServiceHarness(AdminRoles.SuperAdmin);
        var license = TestData.NewLicense();

        Assert.False(h.Simulator.CreateRequestCode("1234-1234-1234-1234", "PC").Succeeded);
        Assert.False(h.Simulator.CreateRequestCode(ActivationCodeFormat.Display(license.ActivationCode), " ").Succeeded);
        Assert.False(h.Simulator.VerifySerialKey("nope", "PC", "key").Succeeded);
    }

    [Fact]
    public void The_simulator_cannot_check_keys_without_a_secret()
    {
        var h = new ServiceHarness(AdminRoles.SuperAdmin, TestSecretProvider.None);

        var result = h.Simulator.VerifySerialKey(ActivationCodeFormat.Display(TestData.NewLicense().ActivationCode), "PC", "AAAAA-AAAAA-AAAAA-AAAAA");

        Assert.False(result.Succeeded);
        Assert.Contains("INFOID_LICENSE_SECRET_V1", result.Errors[0]);
        Assert.False(h.Simulator.IsSecretConfigured);
    }
}
