using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Application.Interfaces;
using InfoID.Shared.Contracts.Licensing;

namespace InfoID.Desktop.Features.Licensing.Services;

/// <summary>
/// Where InfoID finds the License API.
///
/// Set <see cref="ProductionUrl"/> to the real server (e.g. "https://license.yourcompany.com")
/// before building a release for customers. DEBUG builds default to the API running on
/// this PC (InfoID.LicenseManager.Web's launch profile, port 7143), and any build can be pointed elsewhere
/// with the INFOID_LICENSE_SERVER_URL environment variable (useful for testing).
/// </summary>
public static class LicenseServerSettings
{
    public const string ProductionUrl = "";

#if DEBUG
    private const string DefaultUrl = "https://localhost:7143";
#else
    private const string DefaultUrl = ProductionUrl;
#endif

    public static string? BaseUrl
    {
        get
        {
            var url = Environment.GetEnvironmentVariable("INFOID_LICENSE_SERVER_URL");
            if (string.IsNullOrWhiteSpace(url)) url = DefaultUrl;
            return string.IsNullOrWhiteSpace(url) ? null : url.TrimEnd('/');
        }
    }
}

/// <summary>HTTPS client for InfoID.LicenseApi (see the Shared contracts). Network
/// problems become LicenseServerUnavailableException with a plain-language message so
/// the Activation screen can offer offline activation.</summary>
public sealed class LicenseServerClient : ILicenseServerClient
{
    private readonly HttpClient? _http;

    public LicenseServerClient()
    {
        if (LicenseServerSettings.BaseUrl is { } url)
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(url + "/"),
                Timeout = TimeSpan.FromSeconds(20),
            };
        }
    }

    public bool IsConfigured => _http is not null;

    public Task<OnlineActivationResponse> ActivateAsync(OnlineActivationRequest request, CancellationToken ct = default) =>
        PostAsync<OnlineActivationRequest, OnlineActivationResponse>("api/v1/activate", request, ct);

    public Task<LicenseCheckInResponse> CheckInAsync(LicenseCheckInRequest request, CancellationToken ct = default) =>
        PostAsync<LicenseCheckInRequest, LicenseCheckInResponse>("api/v1/checkin", request, ct);

    public Task<LicenseDeactivationResponse> DeactivateAsync(LicenseDeactivationRequest request, CancellationToken ct = default) =>
        PostAsync<LicenseDeactivationRequest, LicenseDeactivationResponse>("api/v1/deactivate", request, ct);

    public string? GetPhoneActivationUrl(string requestCode) =>
        _http is null ? null : new Uri(_http.BaseAddress!, "a?c=" + Uri.EscapeDataString(requestCode.Replace("-", ""))).ToString();

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken ct)
    {
        if (_http is null)
            throw new LicenseServerUnavailableException("No license server is configured in this version of InfoID.");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(path, request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LicenseServerUnavailableException("InfoID couldn't reach the license server. Check the internet connection.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LicenseServerUnavailableException("The license server didn't answer in time. Check the internet connection and try again.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new LicenseServerUnavailableException("Too many attempts. Please wait a minute and try again.");
            if (!response.IsSuccessStatusCode)
                throw new LicenseServerUnavailableException($"The license server had a problem ({(int)response.StatusCode}). Please try again later.");

            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct)
                   ?? throw new LicenseServerUnavailableException("The license server sent an empty reply.");
        }
    }
}
