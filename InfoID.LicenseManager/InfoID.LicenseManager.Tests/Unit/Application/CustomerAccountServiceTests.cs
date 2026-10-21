using System.Reflection;
using InfoID.LicenseManager.Application.DTOs.Accounts;
using InfoID.LicenseManager.Application.Services;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;
using LegacyAccountService = InfoID.Licensing.Server.Services.CustomerAccountService;

namespace InfoID.LicenseManager.Tests.Unit.Application;

public class CustomerAccountServiceTests
{
    private const string Email = "cathy@customer.test";
    private const string Password = "Passw0rd-ok";

    private static SignUpInputDto SignUp(string email = Email, string password = Password, bool terms = true, string name = "Cathy Customer") =>
        new(name, "Customer Inc", email, "123", "Norway", password, terms);

    /// <summary>The 6-digit code from the e-mail the service "sent".</summary>
    private static string LastCode(ServiceHarness h)
    {
        var body = h.Email.Sent.Last().Body;
        var start = body.IndexOf(": ", body.IndexOf("code is", StringComparison.Ordinal), StringComparison.Ordinal) + 2;
        return body.Substring(start, 6);
    }

    private static async Task<string> SignedUpAndVerifiedAsync(ServiceHarness h, string email = Email)
    {
        await h.AccountService.SignUpAsync(SignUp(email));
        var result = await h.AccountService.VerifyEmailAsync(email, LastCode(h), "1.0");
        return result.Token!;
    }

    // ---------------------------------------------------------------- sign-up / verify ----

    [Fact]
    public async Task Sign_up_creates_an_unverified_account_and_e_mails_a_six_digit_code()
    {
        var h = new ServiceHarness(role: "Anonymous");

        var result = await h.AccountService.SignUpAsync(SignUp(" Cathy@Customer.TEST "));

        Assert.True(result.Succeeded);
        Assert.True(result.NeedsVerification);
        Assert.Null(result.Token);
        var account = Assert.Single(h.Accounts.Accounts);
        Assert.Equal(Email, account.Email);
        Assert.False(account.EmailVerified);
        Assert.Equal("fake$" + Password, account.PasswordHash);
        Assert.NotNull(account.TermsAcceptedDate);
        var mail = Assert.Single(h.Email.Sent);
        Assert.Equal(Email, mail.To);
        Assert.Matches("code is: [0-9]{6}", mail.Body);
        Assert.Contains("Customer.SignedUp", h.AuditedActions);
        Assert.Equal("CustomerSignUp", h.UnitOfWork.Actor);
    }

    [Fact]
    public async Task Only_a_hash_of_the_code_is_stored_and_it_matches_the_original_servers_format()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await h.AccountService.SignUpAsync(SignUp());
        var code = LastCode(h);

        var stored = Assert.Single(h.EmailCodes.Codes);

        Assert.DoesNotContain(code, stored.CodeHash);
        Assert.Equal(VerificationCodeService.HashCode(stored.CustomerAccountId, EmailCodePurpose.VerifyEmail, code), stored.CodeHash);
        var legacyHash = typeof(LegacyAccountService).GetMethod("HashCode", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [stored.CustomerAccountId, EmailCodePurpose.VerifyEmail, code]);
        Assert.Equal(legacyHash, stored.CodeHash);
    }

    [Fact]
    public async Task Session_tokens_are_stored_only_as_the_same_sha256_hash_the_original_server_uses()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var token = await SignedUpAndVerifiedAsync(h);

        var session = Assert.Single(h.Accounts.Sessions);

        Assert.DoesNotContain(token, session.TokenHash);
        Assert.Equal(CustomerAccountService.HashToken(token), session.TokenHash);
        var legacyHash = typeof(LegacyAccountService).GetMethod("HashToken", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [token]);
        Assert.Equal(legacyHash, session.TokenHash);
    }

    [Theory]
    [InlineData("", "a@b.com", "Passw0rd1", true, "Enter your full name.")]
    [InlineData("N", "not-an-email", "Passw0rd1", true, "Enter a valid email address.")]
    [InlineData("N", "a@b.com", "short1", true, "Use a password of at least 8 characters with letters and numbers.")]
    [InlineData("N", "a@b.com", "onlyletters", true, "Use a password of at least 8 characters with letters and numbers.")]
    [InlineData("N", "a@b.com", "12345678", true, "Use a password of at least 8 characters with letters and numbers.")]
    [InlineData("N", "a@b.com", "Passw0rd1", false, "Please accept the terms and privacy policy to create an account.")]
    public async Task Sign_up_validates_its_input(string name, string email, string password, bool terms, string expected)
    {
        var h = new ServiceHarness(role: "Anonymous");

        var result = await h.AccountService.SignUpAsync(SignUp(email, password, terms, name));

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Message);
        Assert.Empty(h.Accounts.Accounts);
        Assert.Empty(h.Email.Sent);
    }

    [Fact]
    public async Task A_verified_e_mail_cannot_be_signed_up_again_and_a_disabled_one_stays_reserved()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await SignedUpAndVerifiedAsync(h);
        var disabled = h.Accounts.Seed("gone@customer.test", disabled: true);

        var again = await h.AccountService.SignUpAsync(SignUp());
        var reserved = await h.AccountService.SignUpAsync(SignUp(disabled.Email));

        Assert.Contains("already exists", again.Message);
        Assert.Equal("This account has been disabled. Please contact support.", reserved.Message);
        Assert.Equal(2, h.Accounts.Accounts.Count);
    }

    [Fact]
    public async Task Signing_up_again_before_verifying_takes_the_new_details_and_sends_a_fresh_code()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await h.AccountService.SignUpAsync(SignUp(name: "Typo Name"));
        var firstCode = LastCode(h);

        await h.AccountService.SignUpAsync(SignUp(name: "Right Name", password: "NewPassw0rd"));

        var account = Assert.Single(h.Accounts.Accounts);
        Assert.Equal("Right Name", account.FullName);
        Assert.Equal("fake$NewPassw0rd", account.PasswordHash);
        Assert.Equal(2, h.Email.Sent.Count);
        Assert.Equal(1, h.EmailCodes.Codes.Count(c => c.UsedUtc is null)); // the older code stopped working
        Assert.NotNull(firstCode);
    }

    [Fact]
    public async Task The_right_code_verifies_the_account_and_signs_it_in()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await h.AccountService.SignUpAsync(SignUp());

        var result = await h.AccountService.VerifyEmailAsync(Email, LastCode(h), "1.0");

        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrEmpty(result.Token));
        Assert.Equal(Email, result.Profile!.Email);
        var account = Assert.Single(h.Accounts.Accounts);
        Assert.True(account.EmailVerified);
        Assert.NotNull(account.EmailVerifiedDate);
        Assert.Contains("Customer.EmailVerified", h.AuditedActions);
    }

    [Fact]
    public async Task A_wrong_code_is_refused_counted_and_the_fifth_wrong_try_locks_the_code()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await h.AccountService.SignUpAsync(SignUp());
        var good = LastCode(h);
        var wrong = good == "000000" ? "111111" : "000000";

        for (var i = 0; i < 4; i++)
            Assert.Equal("That code is not valid. Check it, or request a new one.", (await h.AccountService.VerifyEmailAsync(Email, wrong, null)).Message);
        var fifth = await h.AccountService.VerifyEmailAsync(Email, wrong, null);
        var rightButTooLate = await h.AccountService.VerifyEmailAsync(Email, good, null);

        Assert.Equal("Too many wrong attempts. Request a new code.", fifth.Message);
        Assert.False(rightButTooLate.Succeeded);
        Assert.False(h.Accounts.Accounts[0].EmailVerified);
    }

    [Fact]
    public async Task A_code_expires_after_fifteen_minutes()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await h.AccountService.SignUpAsync(SignUp());
        var code = LastCode(h);

        h.Clock.Advance(TimeSpan.FromMinutes(16));
        var result = await h.AccountService.VerifyEmailAsync(Email, code, null);

        Assert.Equal("That code has expired. Request a new one.", result.Message);
    }

    [Fact]
    public async Task A_code_can_be_typed_with_spaces_or_dashes_but_not_reused()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await h.AccountService.SignUpAsync(SignUp());
        var code = LastCode(h);

        var first = await h.AccountService.VerifyEmailAsync(Email, $"{code[..3]} {code[3..]}", null);
        var second = await h.AccountService.VerifyEmailAsync(Email, code, null);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
    }

    [Fact]
    public async Task Resending_is_limited_to_once_a_minute_and_never_reveals_whether_an_account_exists()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await h.AccountService.SignUpAsync(SignUp());

        var tooSoon = await h.AccountService.ResendVerificationAsync(Email);
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        var later = await h.AccountService.ResendVerificationAsync(Email);
        var stranger = await h.AccountService.ResendVerificationAsync("nobody@customer.test");

        Assert.Equal("Please wait a minute before requesting another code.", tooSoon.Message);
        Assert.True(later.Succeeded);
        Assert.True(stranger.Succeeded);
        Assert.Equal(2, h.Email.Sent.Count);
    }

    // ---------------------------------------------------------------- login ----

    [Fact]
    public async Task Login_returns_a_token_and_the_profile()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await SignedUpAndVerifiedAsync(h);

        var result = await h.AccountService.LogInAsync(Email, Password, "2.0");

        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrEmpty(result.Token));
        Assert.Equal("Cathy Customer", result.Profile!.FullName);
        Assert.Equal(2, h.Accounts.Sessions.Count);
        Assert.Equal("2.0", h.Accounts.Sessions[1].AppVersion);
    }

    [Fact]
    public async Task Unknown_e_mail_wrong_password_and_disabled_accounts_all_get_the_same_answer()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await SignedUpAndVerifiedAsync(h);
        h.Accounts.Seed("off@customer.test", disabled: true).PasswordHash = "fake$" + Password;

        var unknown = await h.AccountService.LogInAsync("nobody@customer.test", Password, null);
        var wrong = await h.AccountService.LogInAsync(Email, "wrong-password1", null);
        var disabled = await h.AccountService.LogInAsync("off@customer.test", Password, null);

        Assert.All(new[] { unknown, wrong, disabled }, r => Assert.Equal("Email or password is incorrect.", r.Message));
        Assert.Equal(2, h.Hasher.SimulatedVerifications); // the unknown and the disabled one still spend verification time
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_for_fifteen_minutes()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await SignedUpAndVerifiedAsync(h);

        for (var i = 0; i < CustomerAccount.MaxFailedLogins; i++) await h.AccountService.LogInAsync(Email, "wrong-password1", null);
        var blocked = await h.AccountService.LogInAsync(Email, Password, null);
        h.Clock.Advance(TimeSpan.FromMinutes(15));
        var later = await h.AccountService.LogInAsync(Email, Password, null);

        Assert.False(blocked.Succeeded);
        Assert.Contains("Too many failed attempts", blocked.Message);
        Assert.Contains("15 minutes", blocked.Message);
        Assert.True(later.Succeeded);
    }

    [Fact]
    public async Task Logging_in_before_verifying_sends_a_code_instead_of_a_session()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await h.AccountService.SignUpAsync(SignUp());
        h.Clock.Advance(TimeSpan.FromMinutes(2));

        var result = await h.AccountService.LogInAsync(Email, Password, null);

        Assert.True(result.Succeeded);
        Assert.True(result.NeedsVerification);
        Assert.Null(result.Token);
        Assert.Equal("Please verify your email first. We sent you a code.", result.Message);
        Assert.Equal(2, h.Email.Sent.Count);
    }

    // ---------------------------------------------------------------- sessions ----

    [Fact]
    public async Task A_token_identifies_its_account_until_it_is_logged_out_expires_or_the_account_is_disabled()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var token = await SignedUpAndVerifiedAsync(h);

        Assert.Equal(Email, (await h.AccountService.GetSessionAsync(token))!.Profile.Email);
        Assert.Null(await h.AccountService.GetSessionAsync("not-a-token"));
        Assert.Null(await h.AccountService.GetSessionAsync(null));
        Assert.Null(await h.AccountService.GetSessionAsync("  "));

        h.Accounts.Accounts[0].Disable();
        Assert.Null(await h.AccountService.GetSessionAsync(token));
        h.Accounts.Accounts[0].Enable();
        Assert.NotNull(await h.AccountService.GetSessionAsync(token));

        await h.AccountService.LogOutAsync(token);
        Assert.Null(await h.AccountService.GetSessionAsync(token));
    }

    [Fact]
    public async Task A_session_expires_after_thirty_days_and_active_ones_are_extended()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var token = await SignedUpAndVerifiedAsync(h);
        var session = h.Accounts.Sessions[0];
        var originalExpiry = session.ExpiresUtc;

        h.Clock.Advance(TimeSpan.FromDays(20));
        Assert.NotNull(await h.AccountService.GetSessionAsync(token));
        Assert.True(session.ExpiresUtc > originalExpiry); // less than half the lifetime left: extended

        h.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Null(await h.AccountService.GetSessionAsync(token));
    }

    // ---------------------------------------------------------------- password reset ----

    [Fact]
    public async Task Forgot_and_reset_change_the_password_sign_out_every_other_session_and_verify_the_mailbox()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var oldToken = await SignedUpAndVerifiedAsync(h);
        h.Clock.Advance(TimeSpan.FromMinutes(2));

        var forgot = await h.AccountService.ForgotPasswordAsync(Email);
        var code = LastCode(h);
        var reset = await h.AccountService.ResetPasswordAsync(Email, code, "BrandNewPassw0rd", "3.0");

        Assert.True(forgot.Succeeded);
        Assert.True(reset.Succeeded);
        Assert.Null(await h.AccountService.GetSessionAsync(oldToken));
        Assert.NotNull(await h.AccountService.GetSessionAsync(reset.Token));
        Assert.False((await h.AccountService.LogInAsync(Email, Password, null)).Succeeded);
        Assert.True((await h.AccountService.LogInAsync(Email, "BrandNewPassw0rd", null)).Succeeded);
        Assert.Contains("Customer.PasswordReset", h.AuditedActions);
    }

    [Fact]
    public async Task Forgot_password_gives_the_same_answer_for_unknown_and_disabled_accounts_and_sends_nothing()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var disabled = h.Accounts.Seed("off@customer.test", disabled: true);

        var unknown = await h.AccountService.ForgotPasswordAsync("nobody@customer.test");
        var off = await h.AccountService.ForgotPasswordAsync(disabled.Email);

        Assert.True(unknown.Succeeded);
        Assert.True(off.Succeeded);
        Assert.Empty(h.Email.Sent);
        Assert.False((await h.AccountService.ForgotPasswordAsync("bad")).Succeeded);
    }

    [Fact]
    public async Task Reset_with_a_weak_password_or_a_wrong_code_changes_nothing()
    {
        var h = new ServiceHarness(role: "Anonymous");
        await SignedUpAndVerifiedAsync(h);
        h.Clock.Advance(TimeSpan.FromMinutes(2));
        await h.AccountService.ForgotPasswordAsync(Email);
        var code = LastCode(h);

        var weak = await h.AccountService.ResetPasswordAsync(Email, code, "weak", null);
        var wrong = await h.AccountService.ResetPasswordAsync(Email, code == "000000" ? "111111" : "000000", "BrandNewPassw0rd", null);

        Assert.False(weak.Succeeded);
        Assert.False(wrong.Succeeded);
        Assert.Equal("fake$" + Password, h.Accounts.Accounts[0].PasswordHash);
    }

    // ---------------------------------------------------------------- linking ----

    [Fact]
    public async Task Requests_sent_before_signing_up_are_linked_to_the_account_once_its_e_mail_is_verified()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var plan = h.Plans.Seed();
        var early = h.Requests.Seed(plan, email: Email);
        var licenseCode = h.Licenses.Add(TestData.NewLicense());
        early.LicenseCode = licenseCode;
        var someoneElses = h.Requests.Seed(plan, email: "other@customer.test");

        await SignedUpAndVerifiedAsync(h);

        var account = Assert.Single(h.Accounts.Accounts);
        Assert.Equal(account.Id, early.CustomerAccountId);
        Assert.Equal(account.Id, licenseCode.CustomerAccountId);
        Assert.Null(someoneElses.CustomerAccountId);
    }

    [Fact]
    public async Task Linking_never_takes_over_something_that_already_belongs_to_another_account()
    {
        var h = new ServiceHarness(role: "Anonymous");
        var plan = h.Plans.Seed();
        var owner = h.Accounts.Seed("owner@customer.test");
        var request = h.Requests.Seed(plan, email: Email, accountId: owner.Id);

        await SignedUpAndVerifiedAsync(h);

        Assert.Equal(owner.Id, request.CustomerAccountId);
    }
}
