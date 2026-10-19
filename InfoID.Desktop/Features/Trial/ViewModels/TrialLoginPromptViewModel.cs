using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Interfaces;
using InfoID.Desktop.Features.Account.Services;
using InfoID.Desktop.Features.Trial.Services;
using InfoID.Shared.Contracts.Accounts;

namespace InfoID.Desktop.Features.Trial.ViewModels;

public enum TrialLoginResult { Cancelled, SignedIn, OpenAccountPage }

/// <summary>
/// "Please log in to start your trial and print up to 10 cards." -- shown inside the
/// Print window (not as a second window, so it can never open behind it) when someone
/// without a license or account tries to print. First the message with Log in / Cancel;
/// Log in then shows the email and password fields.
/// </summary>
public sealed partial class TrialLoginPromptViewModel : ObservableObject
{
    private readonly AccountSession _session;
    private readonly CustomerPortalClient _portal;
    private readonly TaskCompletionSource<TrialLoginResult> _completion = new();

    public TrialLoginPromptViewModel(AccountSession session, CustomerPortalClient portal, int cardLimit, string? message = null)
    {
        _session = session;
        _portal = portal;
        Message = message ?? PrintAuthorizationService.SignInMessage(cardLimit);
        Details = $"With a free InfoID account you can print up to {cardLimit} cards with an \"{TrialWatermark.Text}\" watermark. " +
                  "Designing and previewing never need an account. Activate InfoID to print without the watermark or the limit.";
    }

    public Task<TrialLoginResult> Completion => _completion.Task;

    public string Message { get; }
    public string Details { get; }

    /// <summary>False: the message with Log in / Cancel. True: the login form.</summary>
    [ObservableProperty]
    private bool _showForm;

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _needsVerification;

    public bool IsNotBusy => !IsBusy;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    [RelayCommand]
    private async Task LogIn()
    {
        if (!ShowForm)
        {
            ShowForm = true;
            return;
        }
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Enter your email and password.";
            return;
        }

        ErrorMessage = null;
        NeedsVerification = false;
        IsBusy = true;
        try
        {
            var response = await _portal.LogInAsync(new LogInRequest
            {
                Email = Email.Trim(),
                Password = Password,
                AppVersion = CustomerPortalClient.AppVersion,
            });

            if (!response.Success)
            {
                ErrorMessage = response.Message ?? "The email or password isn't right.";
                return;
            }
            if (response.NeedsVerification)
            {
                NeedsVerification = true;
                ErrorMessage = "Verify your email address first: open the Account page to enter the code we sent.";
                return;
            }
            if (response.Token is not { } token)
            {
                ErrorMessage = "The server didn't sign you in. Please try again.";
                return;
            }

            _session.SignIn(token, response.Account);
            Password = string.Empty;
            _completion.TrySetResult(TrialLoginResult.SignedIn);
        }
        catch (LicenseServerUnavailableException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = "Something went wrong: " + ex.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Sign up, forgot password, email verification: the full Account page.</summary>
    [RelayCommand]
    private void OpenAccountPage() => _completion.TrySetResult(TrialLoginResult.OpenAccountPage);

    [RelayCommand]
    private void Cancel() => _completion.TrySetResult(TrialLoginResult.Cancelled);
}
