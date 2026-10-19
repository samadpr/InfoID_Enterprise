using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Application.Interfaces;
using InfoID.Desktop.Features.Licensing.Services;
using InfoID.Shared.Contracts.Accounts;

namespace InfoID.Desktop.Features.Account.Services;

/// <summary>The signed-in session is no longer valid (expired, signed out elsewhere,
/// password reset): sign in again.</summary>
public sealed class SessionExpiredException : Exception
{
    public SessionExpiredException() : base("Your session has expired. Please sign in again.") { }
}

/// <summary>
/// HTTPS client for the customer platform on the License API: accounts (sign-up with
/// email verification, login, password reset), plans and plan requests. Same server
/// address as license activation (LicenseServerSettings). Network problems become
/// LicenseServerUnavailableException with a plain-language message.
/// </summary>
public sealed class CustomerPortalClient
{
    private readonly HttpClient? _http;

    public CustomerPortalClient()
    {
        if (LicenseServerSettings.BaseUrl is { } url)
            _http = new HttpClient { BaseAddress = new Uri(url + "/"), Timeout = TimeSpan.FromSeconds(20) };
    }

    public bool IsConfigured => _http is not null;

    public static string AppVersion =>
        typeof(CustomerPortalClient).Assembly.GetName().Version?.ToString() ?? "unknown";

    public Task<AccountResponse> SignUpAsync(SignUpRequest request, CancellationToken ct = default) =>
        PostAsync<SignUpRequest, AccountResponse>("api/v1/account/signup", request, null, ct);

    public Task<AccountResponse> VerifyAsync(VerifyEmailRequest request, CancellationToken ct = default) =>
        PostAsync<VerifyEmailRequest, AccountResponse>("api/v1/account/verify", request, null, ct);

    public Task<AccountResponse> ResendCodeAsync(string email, CancellationToken ct = default) =>
        PostAsync<EmailOnlyRequest, AccountResponse>("api/v1/account/resend", new EmailOnlyRequest { Email = email }, null, ct);

    public Task<AccountResponse> LogInAsync(LogInRequest request, CancellationToken ct = default) =>
        PostAsync<LogInRequest, AccountResponse>("api/v1/account/login", request, null, ct);

    public Task<AccountResponse> ForgotPasswordAsync(string email, CancellationToken ct = default) =>
        PostAsync<EmailOnlyRequest, AccountResponse>("api/v1/account/forgot", new EmailOnlyRequest { Email = email }, null, ct);

    public Task<AccountResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default) =>
        PostAsync<ResetPasswordRequest, AccountResponse>("api/v1/account/reset", request, null, ct);

    public async Task LogOutAsync(string token, CancellationToken ct = default)
    {
        try
        {
            await PostAsync<object, AccountResponse>("api/v1/account/logout", new { }, token, ct);
        }
        catch (Exception)
        {
            // Signing out locally always works; the server session expires on its own.
        }
    }

    public Task<List<AccountLicenseDto>> GetLicensesAsync(string token, CancellationToken ct = default) =>
        GetAsync<List<AccountLicenseDto>>("api/v1/account/licenses", token, ct);

    public Task<List<AccountRequestDto>> GetRequestsAsync(string token, CancellationToken ct = default) =>
        GetAsync<List<AccountRequestDto>>("api/v1/account/requests", token, ct);

    public Task<List<PlanDto>> GetPlansAsync(CancellationToken ct = default) =>
        GetAsync<List<PlanDto>>("api/v1/plans?product=INFOID", null, ct);

    public Task<PlanRequestResponse> SubmitRequestAsync(PlanRequestSubmission request, string? token, CancellationToken ct = default) =>
        PostAsync<PlanRequestSubmission, PlanRequestResponse>("api/v1/requests", request, token, ct);

    // ------------------------------------------------------------------ trial ----
    // Trial printing (signed in, no activation code). The server owns the count:
    // reserve before printing, then commit (printed) or release (failed / cancelled).

    public Task<TrialStatusDto> GetTrialStatusAsync(string token, CancellationToken ct = default) =>
        GetAsync<TrialStatusDto>("api/v1/trial", token, ct);

    public Task<TrialReserveResponse> ReserveTrialCardsAsync(string token, TrialReserveRequest request, CancellationToken ct = default) =>
        PostAsync<TrialReserveRequest, TrialReserveResponse>("api/v1/trial/reserve", request, token, ct);

    public Task<TrialStatusDto> CommitTrialCardsAsync(string token, Guid reservationId, int cardsPrinted, CancellationToken ct = default) =>
        PostAsync<TrialCompleteRequest, TrialStatusDto>("api/v1/trial/commit",
            new TrialCompleteRequest { ReservationId = reservationId, CardsPrinted = cardsPrinted }, token, ct);

    public Task<TrialStatusDto> ReleaseTrialCardsAsync(string token, Guid reservationId, CancellationToken ct = default) =>
        PostAsync<TrialCompleteRequest, TrialStatusDto>("api/v1/trial/release",
            new TrialCompleteRequest { ReservationId = reservationId }, token, ct);

    // ------------------------------------------------------------------ http ----

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, string? token, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        return await SendAsync<TResponse>(message, token, ct);
    }

    private async Task<TResponse> GetAsync<TResponse>(string path, string? token, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync<TResponse>(message, token, ct);
    }

    private async Task<TResponse> SendAsync<TResponse>(HttpRequestMessage message, string? token, CancellationToken ct)
    {
        if (_http is null)
            throw new LicenseServerUnavailableException("No server is configured in this version of InfoID.");
        if (token is not null)
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LicenseServerUnavailableException("InfoID couldn't reach the server. Check the internet connection.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LicenseServerUnavailableException("The server didn't answer in time. Check the internet connection and try again.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new SessionExpiredException();
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new LicenseServerUnavailableException("Too many attempts. Please wait a minute and try again.");
            if (!response.IsSuccessStatusCode)
                throw new LicenseServerUnavailableException($"The server had a problem ({(int)response.StatusCode}). Please try again later.");

            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct)
                   ?? throw new LicenseServerUnavailableException("The server sent an empty reply.");
        }
    }
}
