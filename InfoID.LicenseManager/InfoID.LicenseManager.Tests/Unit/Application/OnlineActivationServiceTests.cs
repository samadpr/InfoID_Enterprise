using InfoID.Licensing.Core;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;

namespace InfoID.LicenseManager.Tests.Unit.Application;

/// <summary>The client protocol: what InfoID.Desktop sends and what it must be able to verify. Tokens are checked with
/// the public key and Licensing.Core exactly as the desktop app does.</summary>
public class OnlineActivationServiceTests
{
    private static string Hex(string machine) => TestData.Fingerprint(machine).Hex;

    private static string Display(LicenseCode license) => InfoID.LicenseManager.Application.Common.ActivationCodeFormat.Display(license.ActivationCode);

    private static OnlineLicense Verify(string? token)
    {
        var license = SignedLicense.Verify(token, FakeLicenseTokenSigner.PublicKeyPem, out var error);
        Assert.True(license is not null, error);
        return license!;
    }

    // ---------------------------------------------------------------- activate ----

    [Fact]
    public async Task Activation_returns_a_token_the_desktop_can_verify_with_the_public_key_only()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(LicenseTier.Enterprise, expiry: TestData.Today.AddMonths(7), maxCards: 3000));
        license.CustomerRecord = new CustomerRecord { OrganizationName = "Acme AS" };

        var result = await h.Online.ActivateAsync(Display(license), Hex("PC-1"), "1.2.3");

        Assert.True(result.Succeeded);
        Assert.Equal("Active", result.Status);
        var token = Verify(result.License);
        Assert.StartsWith("IIDL1.", result.License);
        Assert.Equal(license.ActivationCode, token.ActivationCode);
        Assert.Equal(Hex("PC-1"), token.Fingerprint);
        Assert.Equal(LicenseTier.Enterprise, token.Tier);
        Assert.Equal(LicenseType.Subscription, token.Type);
        Assert.Equal(TestData.Today.AddMonths(7), token.ExpiryDate);
        Assert.Equal(3000, token.MaxCardsPerPeriod);
        Assert.Equal("Acme AS", token.OrganizationName);
        Assert.Equal(TimeSpan.FromDays(60), token.CheckInDeadlineUtc - token.IssuedUtc);
    }

    [Fact]
    public async Task A_tampered_token_fails_verification()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense());

        var token = (await h.Online.ActivateAsync(Display(license), Hex("PC-1"), null)).License!;
        var parts = token.Split('.');
        var forged = $"{parts[0]}.{parts[1][..^2]}AA.{parts[2]}";

        Assert.Null(SignedLicense.Verify(forged, FakeLicenseTokenSigner.PublicKeyPem, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Activation_records_an_online_activation_with_the_app_version_and_marks_the_code_active()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense());

        await h.Online.ActivateAsync(Display(license), Hex("PC-1"), "1.2.3");

        var record = Assert.Single(license.Activations);
        Assert.Equal("Online", record.ActivationMode);
        Assert.Equal(Hex("PC-1"), record.MachineFingerprint);
        Assert.Equal("1.2.3", record.AppVersion);
        Assert.Equal(TestData.NowUtc, record.LastValidationDate);
        Assert.Null(record.SerialKeyIssued);
        Assert.Equal(LicenseCodeStatus.Active, license.Status);
        Assert.Equal("LicenseApi", h.UnitOfWork.Actor);
        Assert.Contains("Online.Activated", h.AuditedActions);
        Assert.Equal([license.Id], h.Licenses.LockedIds);
    }

    [Fact]
    public async Task A_second_machine_is_refused_when_the_slots_are_full_but_the_first_can_activate_again()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(maxActivations: 1));

        var first = await h.Online.ActivateAsync(Display(license), Hex("PC-A"), null);
        var second = await h.Online.ActivateAsync(Display(license), Hex("PC-B"), null);
        var firstAgain = await h.Online.ActivateAsync(Display(license), Hex("PC-A"), null);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.StartsWith("All 1 activation slot(s) are in use.", second.Error);
        Assert.True(firstAgain.Succeeded);
        Assert.Single(license.ActiveActivations);
        Assert.Contains("Online.ActivationRefused", h.AuditedActions);
    }

    [Fact]
    public async Task Revoked_expired_blacklisted_and_unknown_codes_are_refused_without_a_token()
    {
        var h = new ServiceHarness();
        var revoked = h.Licenses.Add(TestData.NewLicense());
        revoked.Revoke(TestData.NowUtc);
        var expired = h.Licenses.Add(TestData.NewLicense());
        expired.ExpiryDate = TestData.Today.AddDays(-1);
        var blocked = h.Licenses.Add(TestData.NewLicense());
        h.Blacklist.Fingerprints.Add(Hex("BLOCKED"));

        var results = new[]
        {
            await h.Online.ActivateAsync(Display(revoked), Hex("PC"), null),
            await h.Online.ActivateAsync(Display(expired), Hex("PC"), null),
            await h.Online.ActivateAsync(Display(blocked), Hex("BLOCKED"), null),
            await h.Online.ActivateAsync(Display(TestData.NewLicense()), Hex("PC"), null),
        };

        Assert.All(results, r =>
        {
            Assert.False(r.Succeeded);
            Assert.Null(r.License);
            Assert.Equal("Refused", r.Status);
        });
        Assert.Equal("This activation code has been revoked.", results[0].Error);
        Assert.Contains("expired on", results[1].Error);
        Assert.Equal("This machine or activation code is blacklisted.", results[2].Error);
        Assert.Equal("This activation code does not exist in the license database.", results[3].Error);
    }

    [Theory]
    [InlineData("not a code", "AAAAAAAAAAAAAAAAAAAA")]
    [InlineData("", "")]
    public async Task Garbage_input_is_rejected_with_the_original_messages(string code, string fingerprint)
    {
        var h = new ServiceHarness();

        var badCode = await h.Online.ActivateAsync(code, fingerprint, null);

        Assert.False(badCode.Succeeded);
        Assert.Equal("That activation code isn't valid. Check it was typed exactly.", badCode.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ZZ")]
    [InlineData("ZZZZZZZZZZZZZZZZZZZZ")]
    public async Task A_malformed_fingerprint_is_rejected(string fingerprint)
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense());

        var result = await h.Online.ActivateAsync(Display(license), fingerprint, null);

        Assert.False(result.Succeeded);
        Assert.Equal("The request from InfoID was not valid.", result.Error);
        Assert.Empty(license.Activations);
    }

    [Fact]
    public async Task Without_a_signing_key_nothing_is_activated_and_the_status_says_error()
    {
        var h = new ServiceHarness();
        h.Signer.Configured = false;
        var license = h.Licenses.Add(TestData.NewLicense());

        var activate = await h.Online.ActivateAsync(Display(license), Hex("PC"), null);
        var checkIn = await h.Online.CheckInAsync(Display(license), Hex("PC"), null, null);

        Assert.Equal("Error", activate.Status);
        Assert.Equal("Error", checkIn.Status);
        Assert.Equal("The license server is not configured (no signing key).", activate.Error);
        Assert.Empty(license.Activations);
    }

    // ---------------------------------------------------------------- check-in ----

    [Fact]
    public async Task Check_in_renews_the_token_and_picks_up_an_extension_made_by_staff()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(expiry: TestData.Today.AddMonths(1)));
        await h.Online.ActivateAsync(Display(license), Hex("PC"), null);
        h.Clock.Advance(TimeSpan.FromDays(10));
        license.ExpiryDate = TestData.Today.AddYears(2);

        var result = await h.Online.CheckInAsync(Display(license), Hex("PC"), 42, "2.0");

        Assert.True(result.Succeeded);
        Assert.Equal("Active", result.Status);
        Assert.Equal(TestData.Today.AddYears(2), Verify(result.License).ExpiryDate);
        var record = Assert.Single(license.Activations);
        Assert.Equal(TestData.NowUtc.AddDays(10), record.LastValidationDate);
        Assert.Equal(42, record.CardsIssuedReported);
        Assert.Equal("2.0", record.AppVersion);
        Assert.Equal(TestData.Today.AddYears(2), record.IssuedExpiryDate);
    }

    [Fact]
    public async Task Check_in_never_moves_the_expiry_backwards_and_keeps_an_edition_override()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(LicenseTier.Standard, expiry: TestData.Today.AddMonths(6)));
        await h.Online.ActivateAsync(Display(license), Hex("PC"), null);
        license.Activations[0].IssuedTier = "Enterprise";
        license.Activations[0].IssuedExpiryDate = TestData.Today.AddYears(3);

        var token = Verify((await h.Online.CheckInAsync(Display(license), Hex("PC"), null, null)).License);

        Assert.Equal(LicenseTier.Enterprise, token.Tier);
        Assert.Equal(TestData.Today.AddYears(3), token.ExpiryDate);
    }

    [Fact]
    public async Task A_perpetual_license_checks_in_without_an_expiry()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(type: LicenseType.Perpetual));
        await h.Online.ActivateAsync(Display(license), Hex("PC"), null);

        var token = Verify((await h.Online.CheckInAsync(Display(license), Hex("PC"), null, null)).License);

        Assert.Null(token.ExpiryDate);
        Assert.Equal(LicenseType.Perpetual, token.Type);
    }

    [Fact]
    public async Task Check_in_reports_revoked_deactivated_unknown_and_blocked_so_the_pc_stops_printing()
    {
        var h = new ServiceHarness();
        var revoked = h.Licenses.Add(TestData.NewLicense());
        await h.Online.ActivateAsync(Display(revoked), Hex("PC"), null);
        revoked.Revoke(TestData.NowUtc);
        var freed = h.Licenses.Add(TestData.NewLicense());
        await h.Online.ActivateAsync(Display(freed), Hex("PC"), null);
        freed.FreeMachineSlots(TestData.NowUtc, "moved");
        var blocked = h.Licenses.Add(TestData.NewLicense());
        await h.Online.ActivateAsync(Display(blocked), Hex("PC"), null);
        h.Blacklist.Fingerprints.Add(Hex("PC"));

        var r1 = await h.Online.CheckInAsync(Display(revoked), Hex("PC"), null, null);
        var r2 = await h.Online.CheckInAsync(Display(freed), Hex("PC"), null, null);
        var r3 = await h.Online.CheckInAsync(Display(TestData.NewLicense()), Hex("PC"), null, null);
        var r4 = await h.Online.CheckInAsync(Display(blocked), Hex("PC"), null, null);
        var r5 = await h.Online.CheckInAsync("garbage", Hex("PC"), null, null);

        Assert.Equal(["Revoked", "Deactivated", "Unknown", "Revoked", "Unknown"], new[] { r1.Status, r2.Status, r3.Status, r4.Status, r5.Status });
        Assert.All(new[] { r1, r2, r3, r4, r5 }, r =>
        {
            Assert.False(r.Succeeded);
            Assert.Null(r.License);
        });
        Assert.Equal("This computer's license was deactivated. Activate again to continue.", r2.Error);
    }

    [Fact]
    public async Task A_machine_that_never_activated_cannot_check_in()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(maxActivations: 2));
        await h.Online.ActivateAsync(Display(license), Hex("PC-A"), null);

        var result = await h.Online.CheckInAsync(Display(license), Hex("PC-B"), null, null);

        Assert.Equal("Deactivated", result.Status);
    }

    // ---------------------------------------------------------------- deactivate ----

    [Fact]
    public async Task Deactivating_frees_the_slot_records_the_transfer_and_reopens_the_code()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(maxActivations: 1));
        await h.Online.ActivateAsync(Display(license), Hex("PC-A"), null);

        var result = await h.Online.DeactivateAsync(Display(license), Hex("PC-A"));

        Assert.True(result.Succeeded);
        Assert.Empty(license.ActiveActivations);
        Assert.Equal(LicenseCodeStatus.Available, license.Status);
        var transfer = Assert.Single(h.Activations.Transfers);
        Assert.Equal("Deactivated from InfoID", transfer.Reason);
        Assert.Contains("Online.Deactivated", h.AuditedActions);
        Assert.True((await h.Online.ActivateAsync(Display(license), Hex("PC-B"), null)).Succeeded); // the slot is free again
    }

    [Fact]
    public async Task Deactivating_one_machine_leaves_the_others_and_the_code_active()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense(maxActivations: 2));
        await h.Online.ActivateAsync(Display(license), Hex("PC-A"), null);
        await h.Online.ActivateAsync(Display(license), Hex("PC-B"), null);

        await h.Online.DeactivateAsync(Display(license), Hex("PC-A"));

        Assert.Equal(Hex("PC-B"), Assert.Single(license.ActiveActivations).MachineFingerprint);
        Assert.Equal(LicenseCodeStatus.Active, license.Status);
    }

    [Fact]
    public async Task Deactivating_an_unknown_license_or_with_garbage_input_fails_politely()
    {
        var h = new ServiceHarness();

        var unknown = await h.Online.DeactivateAsync(Display(TestData.NewLicense()), Hex("PC"));
        var garbage = await h.Online.DeactivateAsync("x", "y");

        Assert.Equal("This license is not known to the license server.", unknown.Errors[0]);
        Assert.Equal("The request from InfoID was not valid.", garbage.Errors[0]);
    }

    [Fact]
    public async Task Deactivating_does_not_reopen_a_revoked_code()
    {
        var h = new ServiceHarness();
        var license = h.Licenses.Add(TestData.NewLicense());
        await h.Online.ActivateAsync(Display(license), Hex("PC"), null);
        license.Revoke(TestData.NowUtc);

        await h.Online.DeactivateAsync(Display(license), Hex("PC"));

        Assert.Equal(LicenseCodeStatus.Revoked, license.Status);
    }

    // ---------------------------------------------------------------- phone ----

    private static string RequestCodeFor(LicenseCode license, string machine)
    {
        ActivationCode.TryParse(license.ActivationCode, out var code);
        return new SoftCode(code!, TestData.Fingerprint(machine), TestData.Today).Encode();
    }

    [Fact]
    public async Task The_phone_page_issues_a_key_the_desktop_accepts_for_offline_enabled_licenses_only()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var allowed = h.Licenses.Add(TestData.NewLicense(allowOffline: true, maxCards: 500));
        var online = h.Licenses.Add(TestData.NewLicense(allowOffline: false));

        var ok = await h.Activation.IssueForPhoneAsync(RequestCodeFor(allowed, "PC"));
        var refused = await h.Activation.IssueForPhoneAsync(RequestCodeFor(online, "PC"));

        Assert.True(ok.Succeeded, string.Join(" ", ok.Errors));
        ActivationCode.TryParse(allowed.ActivationCode, out var code);
        var validation = SerialKey.Validate(ok.Value!.SerialKey, code!, TestData.Fingerprint("PC"), [TestSecretProvider.V1]);
        Assert.True(validation.IsValid, validation.Error);
        Assert.Equal(500, validation.Terms!.MaxCardsPerPeriod);
        var record = Assert.Single(allowed.Activations);
        Assert.Equal("Phone", record.ActivationMode);
        Assert.Null(record.GeneratedByAdminUserId);
        Assert.False(refused.Succeeded);
        Assert.StartsWith("This license can only be activated online.", refused.Errors[0]);
        Assert.Empty(online.Activations);
    }

    [Fact]
    public async Task The_phone_page_applies_the_same_slot_rules_as_staff_keys()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var license = h.Licenses.Add(TestData.NewLicense(allowOffline: true, maxActivations: 1));

        await h.Activation.IssueForPhoneAsync(RequestCodeFor(license, "PC-A"));
        var second = await h.Activation.IssueForPhoneAsync(RequestCodeFor(license, "PC-B"));

        Assert.False(second.Succeeded);
        Assert.StartsWith("All 1 activation slot(s) are in use.", second.Errors[0]);
    }
}
