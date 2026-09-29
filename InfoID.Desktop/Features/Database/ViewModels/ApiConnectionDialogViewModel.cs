using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Database.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.Database.ViewModels;

public partial class ApiConnectionDialogViewModel
    : DialogViewModelBase<DatabaseConnection?>
{
    private readonly ApiDatabaseService _apiDatabaseService;

    public IReadOnlyList<string> AuthenticationOptions { get; } =
        new[]
        {
            "None",
            "API Key",
            "Bearer Token",
            "Basic Authentication"
        };

    [ObservableProperty]
    private string _apiUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsApiKeyAuth))]
    [NotifyPropertyChangedFor(nameof(IsBearerAuth))]
    [NotifyPropertyChangedFor(nameof(IsBasicAuth))]
    [NotifyPropertyChangedFor(nameof(UsesCredentials))]
    private string _authenticationType = "None";

    // Which fields the dialog shows for the selected authentication type.
    public bool IsApiKeyAuth => AuthenticationType == "API Key";
    public bool IsBearerAuth => AuthenticationType == "Bearer Token";
    public bool IsBasicAuth => AuthenticationType == "Basic Authentication";
    public bool UsesCredentials => AuthenticationType != "None";

    // A result from the previous authentication type no longer applies.
    partial void OnAuthenticationTypeChanged(string value) => StatusMessage = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _bearerToken = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _keepCredentials;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ApiConnectionDialogViewModel()
    {
        _apiDatabaseService = new ApiDatabaseService();
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (string.IsNullOrWhiteSpace(ApiUrl))
        {
            StatusMessage = "API URL is required.";
            return;
        }

        try
        {
            var json =await _apiDatabaseService.GetAsync(ApiUrl, AuthenticationType,ApiKey, BearerToken,Username,Password);

            if (string.IsNullOrWhiteSpace(json))
            {
                StatusMessage = "API returned an empty response.";
                return;
            }

            StatusMessage = "API connection successful.";
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"API connection failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Next()
    {
        if (string.IsNullOrWhiteSpace(ApiUrl))
        {
            StatusMessage = "API URL is required.";
            return;
        }

        try
        {
            var json =
              await _apiDatabaseService.GetAsync(
    ApiUrl,
    AuthenticationType,
    ApiKey,
    BearerToken,
    Username,
    Password);

            if (string.IsNullOrWhiteSpace(json))
            {
                StatusMessage = "API returned an empty response.";
                return;
            }

            var connection = new DatabaseConnection
            {
                Name = ApiUrl,
                Type = DatabaseType.API,
                ConnectionString = ApiUrl,
                AuthenticationType = AuthenticationType,

                // Keep only the values the selected authentication type uses.
                ApiKey = IsApiKeyAuth ? ApiKey : string.Empty,
                BearerToken = IsBearerAuth ? BearerToken : string.Empty,
                Username = IsBasicAuth ? Username : string.Empty,
                Password = IsBasicAuth ? Password : string.Empty,

                KeepCredentials = UsesCredentials && KeepCredentials,
                IsConnected = true
            };

            RequestClose(connection);
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"API connection failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose(null);
    }
}
