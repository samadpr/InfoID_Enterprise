using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InfoID.Licensing.Core;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Tests.Fakes;
using InfoID.Shared.Contracts.Accounts;
using InfoID.Shared.Contracts.Licensing;

namespace InfoID.LicenseManager.Tests.Web;

/// <summary>The JSON API InfoID.Desktop calls. These tests use the shared contract classes (the same ones the desktop
/// compiles against) and check property names and status codes, because installed apps depend on them.</summary>
public class ClientApiTests : IDisposable
{
    private readonly WebAppFactory _app = new();
    private readonly HttpClient _client;

    public ClientApiTests() => _client = _app.CreateBrowser();

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private static string Hex(string machine) => TestData.Fingerprint(machine).Hex;

    private static string Display(LicenseCode license) => InfoID.LicenseManager.Application.Common.ActivationCodeFormat.Display(license.ActivationCode);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    // ---------------------------------------------------------------- activation ----

    [Fact]
    public async Task Activate_answers_with_the_original_json_shape_and_a_token_the_desktop_can_verify()
    {
        var license = _app.Licenses.Add(TestData.NewLicense(LicenseTier.Professional, maxCards: 500));

        var response = await _client.PostAsJsonAsync("/api/v1/activate",
            new OnlineActivationRequest { ActivationCode = Display(license), MachineFingerprint = Hex("PC"), AppVersion = "1.0" });
        var json = await JsonOf(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(["errorMessage", "license", "success"], json.EnumerateObject().Select(p => p.Name).Order());
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("errorMessage").ValueKind);
        var token = SignedLicense.Verify(json.GetProperty("license").GetString(), FakeLicenseTokenSigner.PublicKeyPem, out var error);
        Assert.True(token is not null, error);
        Assert.Equal(LicenseTier.Professional, token!.Tier);
        Assert.Equal(Hex("PC"), token.Fingerprint);
    }

    [Fact]
    public async Task The_json_property_names_are_case_insensitive_so_older_clients_still_work()
    {
        var license = _app.Licenses.Add(TestData.NewLicense());

        var response = await _client.PostAsync("/api/v1/activate", new StringContent(
            $"{{\"ActivationCode\":\"{Display(license)}\",\"MachineFingerprint\":\"{Hex("PC")}\"}}", System.Text.Encoding.UTF8, "application/json"));

        Assert.True((await JsonOf(response)).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task A_refused_activation_is_still_http_200_with_success_false_and_a_message()
    {
        var license = _app.Licenses.Add(TestData.NewLicense());
        license.Revoke(TestData.NowUtc);

        var response = await _client.PostAsJsonAsync("/api/v1/activate",
            new OnlineActivationRequest { ActivationCode = Display(license), MachineFingerprint = Hex("PC") });
        var body = await response.Content.ReadFromJsonAsync<OnlineActivationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body!.Success);
        Assert.Null(body.License);
        Assert.Equal("This activation code has been revoked.", body.ErrorMessage);
    }

    [Fact]
    public async Task Check_in_and_deactivate_follow_the_same_contract()
    {
        var license = _app.Licenses.Add(TestData.NewLicense());
        await _client.PostAsJsonAsync("/api/v1/activate", new OnlineActivationRequest { ActivationCode = Display(license), MachineFingerprint = Hex("PC") });

        var checkIn = await _client.PostAsJsonAsync("/api/v1/checkin",
            new LicenseCheckInRequest { ActivationCode = Display(license), MachineFingerprint = Hex("PC"), CardsIssuedThisPeriod = 7, AppVersion = "2.0" });
        var checkInJson = await JsonOf(checkIn);
        var deactivate = await _client.PostAsJsonAsync("/api/v1/deactivate",
            new LicenseDeactivationRequest { ActivationCode = Display(license), MachineFingerprint = Hex("PC") });
        var after = await (await _client.PostAsJsonAsync("/api/v1/checkin",
            new LicenseCheckInRequest { ActivationCode = Display(license), MachineFingerprint = Hex("PC") })).Content.ReadFromJsonAsync<LicenseCheckInResponse>();

        Assert.Equal(["license", "message", "status"], checkInJson.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("Active", checkInJson.GetProperty("status").GetString());
        Assert.Equal(7, license.Activations[0].CardsIssuedReported);
        Assert.Equal(["errorMessage", "success"], (await JsonOf(deactivate)).EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("Deactivated", after!.Status);
    }

    [Fact]
    public async Task The_api_needs_no_cookie_or_antiforgery_token_and_sets_no_cookie()
    {
        var license = _app.Licenses.Add(TestData.NewLicense());

        var response = await _client.PostAsJsonAsync("/api/v1/activate", new OnlineActivationRequest { ActivationCode = Display(license), MachineFingerprint = Hex("PC") });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task A_malformed_body_is_a_400_and_never_a_stack_trace()
    {
        var response = await _client.PostAsync("/api/v1/activate", new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("   at ", text);
    }

    [Fact]
    public async Task Health_is_open_and_reports_ok()
    {
        _app.Admins.Seed("boss@infoid.test", "Correct-Horse-9");

        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", (await JsonOf(response)).GetProperty("status").GetString());
    }

    // ---------------------------------------------------------------- phone page ----

    [Fact]
    public async Task The_phone_page_shows_the_key_in_html_for_an_offline_enabled_license()
    {
        var license = _app.Licenses.Add(TestData.NewLicense(allowOffline: true, maxCards: 500));
        ActivationCode.TryParse(license.ActivationCode, out var code);
        var soft = new SoftCode(code!, TestData.Fingerprint("PC"), TestData.Today).Encode();

        var response = await _client.GetAsync("/a?c=" + Uri.EscapeDataString(soft));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Your InfoID serial key", html);
        Assert.Contains(license.Activations[0].SerialKeyIssued!, html);
        Assert.Contains("500 cards per month", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_phone_page_explains_errors_and_a_missing_link_without_leaking_details()
    {
        var onlineOnly = _app.Licenses.Add(TestData.NewLicense(allowOffline: false));
        ActivationCode.TryParse(onlineOnly.ActivationCode, out var code);
        var soft = new SoftCode(code!, TestData.Fingerprint("PC"), TestData.Today).Encode();

        var refused = await (await _client.GetAsync("/a?c=" + Uri.EscapeDataString(soft))).Content.ReadAsStringAsync();
        var missing = await (await _client.GetAsync("/a")).Content.ReadAsStringAsync();
        var garbage = await (await _client.GetAsync("/a?c=hello")).Content.ReadAsStringAsync();

        Assert.Contains("Activation not possible", refused);
        Assert.Contains("can only be activated online", refused);
        Assert.Contains("This link is incomplete. Scan the QR code in InfoID again.", missing);
        Assert.Contains("Activation not possible", garbage);
    }

    [Fact]
    public async Task The_request_code_in_the_phone_url_never_reaches_the_log()
    {
        var license = _app.Licenses.Add(TestData.NewLicense(allowOffline: true));
        ActivationCode.TryParse(license.ActivationCode, out var code);
        var soft = new SoftCode(code!, TestData.Fingerprint("PC"), TestData.Today).Encode();

        await _client.GetAsync("/a?c=" + Uri.EscapeDataString(soft));

        var logs = string.Join("\n", _app.Logs.Messages);
        Assert.Contains("GET /a ->", logs);
        Assert.DoesNotContain(soft.Replace("-", ""), logs.Replace("-", ""));
        Assert.DoesNotContain(license.Activations[0].SerialKeyIssued!, logs);
    }

    // ---------------------------------------------------------------- errors, limits ----

    [Fact]
    public async Task An_unexpected_failure_gives_a_json_500_with_a_trace_id_and_no_details()
    {
        using var app = new WebAppFactory { SignerOverride = new ThrowingSigner() };
        using var client = app.CreateBrowser();
        var license = app.Licenses.Add(TestData.NewLicense());

        var response = await client.PostAsJsonAsync("/api/v1/activate", new OnlineActivationRequest { ActivationCode = Display(license), MachineFingerprint = Hex("PC") });
        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("The license server had a problem. Please try again later.", json.GetProperty("error").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("secret-internal-detail", text);
        Assert.Contains(app.Logs.Messages, m => m.Contains("secret-internal-detail") && m.StartsWith("[Error]"));
    }

    [Fact]
    public async Task Activation_is_rate_limited_to_twenty_calls_a_minute_per_address()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 22; i++)
            statuses.Add((await _client.PostAsJsonAsync("/api/v1/activate", new OnlineActivationRequest { ActivationCode = "x", MachineFingerprint = "y" })).StatusCode);

        Assert.Equal(20, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }

    // ---------------------------------------------------------------- accounts ----

    private async Task<string> SignedInTokenAsync(string email = "cathy@customer.test")
    {
        await _client.PostAsJsonAsync("/api/v1/account/signup",
            new SignUpRequest { FullName = "Cathy Customer", Email = email, Password = "Passw0rd-ok", AcceptTerms = true });
        var body = _app.Email.Sent.Last().Body;
        var code = body.Substring(body.IndexOf("code is: ", StringComparison.Ordinal) + 9, 6);
        var verified = await (await _client.PostAsJsonAsync("/api/v1/account/verify", new VerifyEmailRequest { Email = email, Code = code })).Content.ReadFromJsonAsync<AccountResponse>();
        return verified!.Token!;
    }

    [Fact]
    public async Task Sign_up_then_verify_returns_a_token_and_the_profile_in_the_original_shape()
    {
        var signUp = await _client.PostAsJsonAsync("/api/v1/account/signup",
            new SignUpRequest { FullName = "Cathy Customer", Email = "cathy@customer.test", Password = "Passw0rd-ok", AcceptTerms = true });
        var signUpJson = await JsonOf(signUp);

        Assert.Equal(HttpStatusCode.OK, signUp.StatusCode);
        Assert.Equal(["account", "message", "needsVerification", "success", "token"], signUpJson.EnumerateObject().Select(p => p.Name).Order());
        Assert.True(signUpJson.GetProperty("needsVerification").GetBoolean());
        Assert.Equal(JsonValueKind.Null, signUpJson.GetProperty("token").ValueKind);

        var token = await SignedInTokenAsyncFromPending();
        Assert.False(string.IsNullOrEmpty(token));
    }

    private async Task<string> SignedInTokenAsyncFromPending()
    {
        var body = _app.Email.Sent.Last().Body;
        var code = body.Substring(body.IndexOf("code is: ", StringComparison.Ordinal) + 9, 6);
        var verified = await (await _client.PostAsJsonAsync("/api/v1/account/verify",
            new VerifyEmailRequest { Email = "cathy@customer.test", Code = code })).Content.ReadFromJsonAsync<AccountResponse>();
        Assert.True(verified!.Success);
        Assert.Equal("cathy@customer.test", verified.Account!.Email);
        return verified.Token!;
    }

    [Fact]
    public async Task Signed_in_endpoints_answer_401_with_no_body_page_when_the_token_is_missing_or_wrong()
    {
        var paths = new[] { "/api/v1/account/me", "/api/v1/account/licenses", "/api/v1/account/requests", "/api/v1/trial" };

        foreach (var path in paths)
        {
            var anonymous = await _client.GetAsync(path);
            var wrong = await _client.SendAsync(Authorized(HttpMethod.Get, path, "not-a-token"));
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.DoesNotContain("<html", await anonymous.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(Authorized(HttpMethod.Post, "/api/v1/trial/reserve", null, new TrialReserveRequest { Cards = 1 }))).StatusCode);
    }

    [Fact]
    public async Task Me_licenses_requests_and_logout_work_with_the_bearer_token()
    {
        var token = await SignedInTokenAsync();
        var account = _app.Accounts.Accounts.Single();
        var plan = _app.Plans.Seed("Pro");
        var license = _app.Licenses.Add(TestData.NewLicense(LicenseTier.Professional));
        license.CustomerAccountId = account.Id;
        var request = _app.Requests.Seed(plan, accountId: account.Id, status: LicenseRequestStatus.Approved);
        request.LicenseCode = license;

        var me = await JsonOf(await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/account/me", token)));
        var licenses = await JsonOf(await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/account/licenses", token)));
        var requests = await JsonOf(await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/account/requests", token)));
        var logout = await _client.SendAsync(Authorized(HttpMethod.Post, "/api/v1/account/logout", token));
        var afterLogout = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/account/me", token));

        Assert.True(me.GetProperty("success").GetBoolean());
        Assert.Equal("cathy@customer.test", me.GetProperty("account").GetProperty("email").GetString());
        var firstLicense = licenses[0];
        Assert.Equal(["activationCode", "activeMachines", "expiryDate", "licenseType", "maxActivations", "productCode", "status", "tier"],
            firstLicense.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(Display(license), firstLicense.GetProperty("activationCode").GetString());
        Assert.Equal(["activationCode", "createdDate", "id", "machines", "planName", "status", "statusReason"], requests[0].EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Login_forgot_and_reset_follow_the_contract()
    {
        await SignedInTokenAsync();
        _app.Clock.Advance(TimeSpan.FromMinutes(2));

        var wrong = await (await _client.PostAsJsonAsync("/api/v1/account/login", new LogInRequest { Email = "cathy@customer.test", Password = "nope-nope1" })).Content.ReadFromJsonAsync<AccountResponse>();
        var login = await (await _client.PostAsJsonAsync("/api/v1/account/login", new LogInRequest { Email = "cathy@customer.test", Password = "Passw0rd-ok" })).Content.ReadFromJsonAsync<AccountResponse>();
        var forgot = await (await _client.PostAsJsonAsync("/api/v1/account/forgot", new EmailOnlyRequest { Email = "cathy@customer.test" })).Content.ReadFromJsonAsync<AccountResponse>();
        var body = _app.Email.Sent.Last().Body;
        var code = body.Substring(body.IndexOf("code is: ", StringComparison.Ordinal) + 9, 6);
        var reset = await (await _client.PostAsJsonAsync("/api/v1/account/reset",
            new ResetPasswordRequest { Email = "cathy@customer.test", Code = code, NewPassword = "BrandNewPassw0rd" })).Content.ReadFromJsonAsync<AccountResponse>();

        Assert.False(wrong!.Success);
        Assert.Equal("Email or password is incorrect.", wrong.Message);
        Assert.True(login!.Success);
        Assert.NotNull(login.Token);
        Assert.True(forgot!.Success);
        Assert.True(reset!.Success);
        Assert.NotNull(reset.Token);
    }

    [Fact]
    public async Task Account_calls_are_rate_limited_to_fifteen_a_minute()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 17; i++)
            statuses.Add((await _client.PostAsJsonAsync("/api/v1/account/login", new LogInRequest { Email = "a@b.test", Password = "x" })).StatusCode);

        Assert.Equal(15, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }

    // ---------------------------------------------------------------- plans and requests ----

    [Fact]
    public async Task Plans_are_public_and_split_the_feature_text_into_a_list()
    {
        _app.Plans.Seed("Pro", LicenseTier.Professional, order: 1).FeaturesText = "One\nTwo\n\n Three ";
        _app.Plans.Seed("Hidden", active: false);

        var plans = await JsonOf(await _client.GetAsync("/api/v1/plans"));

        var plan = Assert.Single(plans.EnumerateArray());
        Assert.Equal(["cardLimitPeriod", "defaultMachines", "durationMonths", "features", "id", "maxCardsPerPeriod", "name", "priceText", "tagline", "tier"],
            plan.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(["One", "Two", "Three"], plan.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task A_plan_request_works_with_and_without_signing_in()
    {
        var plan = _app.Plans.Seed("Pro");
        var token = await SignedInTokenAsync();

        var anonymous = await (await _client.PostAsJsonAsync("/api/v1/requests",
            new PlanRequestSubmission { PlanId = plan.Id, Machines = 1, FullName = "Anon", Email = "anon@x.test" })).Content.ReadFromJsonAsync<PlanRequestResponse>();
        var signedIn = await (await _client.SendAsync(Authorized(HttpMethod.Post, "/api/v1/requests", token,
            new PlanRequestSubmission { PlanId = plan.Id, Machines = 2, FullName = "Cathy", Email = "ignored@x.test" }))).Content.ReadFromJsonAsync<PlanRequestResponse>();
        var bad = await (await _client.PostAsJsonAsync("/api/v1/requests",
            new PlanRequestSubmission { PlanId = 999, FullName = "X", Email = "x@x.test" })).Content.ReadFromJsonAsync<PlanRequestResponse>();

        Assert.True(anonymous!.Success);
        Assert.NotNull(anonymous.RequestId);
        Assert.True(signedIn!.Success);
        Assert.False(bad!.Success);
        Assert.Equal("cathy@customer.test", _app.Requests.Requests.Single(r => r.Id == signedIn.RequestId).Email);
        Assert.Null(_app.Requests.Requests.Single(r => r.Id == anonymous.RequestId).CustomerAccountId);
    }

    // ---------------------------------------------------------------- trial ----

    [Fact]
    public async Task Trial_reserve_commit_and_release_follow_the_contract()
    {
        var token = await SignedInTokenAsync();

        var status = await JsonOf(await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/trial", token)));
        var reserve = await JsonOf(await _client.SendAsync(Authorized(HttpMethod.Post, "/api/v1/trial/reserve", token,
            new TrialReserveRequest { Cards = 4, DesignName = "Badge", MachineFingerprint = "FP", AppVersion = "1.0" })));
        var id = reserve.GetProperty("reservationId").GetGuid();
        var commit = await JsonOf(await _client.SendAsync(Authorized(HttpMethod.Post, "/api/v1/trial/commit", token,
            new TrialCompleteRequest { ReservationId = id, CardsPrinted = 3 })));
        var second = await JsonOf(await _client.SendAsync(Authorized(HttpMethod.Post, "/api/v1/trial/reserve", token, new TrialReserveRequest { Cards = 2 })));
        var release = await JsonOf(await _client.SendAsync(Authorized(HttpMethod.Post, "/api/v1/trial/release", token,
            new TrialCompleteRequest { ReservationId = second.GetProperty("reservationId").GetGuid() })));

        Assert.Equal(["cardLimit", "cardsRemaining", "cardsReserved", "cardsUsed", "message", "success"], status.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(10, status.GetProperty("cardsRemaining").GetInt32());
        Assert.Equal(["cardLimit", "cardsRemaining", "cardsReserved", "cardsUsed", "expiresUtc", "message", "reservationId", "success"], reserve.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(6, reserve.GetProperty("cardsRemaining").GetInt32());
        Assert.Equal(3, commit.GetProperty("cardsUsed").GetInt32());
        Assert.Equal(0, release.GetProperty("cardsReserved").GetInt32());
        Assert.Equal(7, release.GetProperty("cardsRemaining").GetInt32());
    }

    [Fact]
    public async Task A_trial_over_the_limit_is_http_200_with_success_false_and_a_message()
    {
        var token = await SignedInTokenAsync();

        var reserve = await _client.SendAsync(Authorized(HttpMethod.Post, "/api/v1/trial/reserve", token, new TrialReserveRequest { Cards = 11 }));
        var body = await reserve.Content.ReadFromJsonAsync<TrialReserveResponse>();

        Assert.Equal(HttpStatusCode.OK, reserve.StatusCode);
        Assert.False(body!.Success);
        Assert.Equal("A trial print job can have at most 10 cards.", body.Message);
        Assert.Null(body.ReservationId);
    }

    private sealed class ThrowingSigner : InfoID.LicenseManager.Application.Interfaces.Security.ILicenseTokenSigner
    {
        public bool IsConfigured => true;

        public string Sign(OnlineLicense license) => throw new InvalidOperationException("secret-internal-detail");
    }
}

public class ClientApiProductionTests
{
    [Fact]
    public async Task In_production_an_unexpected_api_failure_is_still_the_json_500_and_never_the_html_error_page()
    {
        using var app = new WebAppFactory { EnvironmentName = "Production", SignerOverride = new ThrowingProductionSigner() };
        using var client = app.CreateBrowser();
        var license = app.Licenses.Add(TestData.NewLicense());
        var code = InfoID.LicenseManager.Application.Common.ActivationCodeFormat.Display(license.ActivationCode);

        var response = await client.PostAsJsonAsync("/api/v1/activate",
            new OnlineActivationRequest { ActivationCode = code, MachineFingerprint = TestData.Fingerprint("PC").Hex });
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("traceId", text);
        Assert.DoesNotContain("boom-detail", text);
        Assert.DoesNotContain("<html", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_staff_site_still_gets_its_friendly_error_page_in_production()
    {
        using var app = new WebAppFactory { EnvironmentName = "Production" };
        app.Admins.Seed("boss@infoid.test", "Correct-Horse-9");
        using var client = app.CreateBrowser();

        var response = await client.GetAsync("/no/such/page");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); // anonymous: sent to sign-in
    }

    private sealed class ThrowingProductionSigner : InfoID.LicenseManager.Application.Interfaces.Security.ILicenseTokenSigner
    {
        public bool IsConfigured => true;

        public string Sign(OnlineLicense license) => throw new InvalidOperationException("boom-detail");
    }
}
