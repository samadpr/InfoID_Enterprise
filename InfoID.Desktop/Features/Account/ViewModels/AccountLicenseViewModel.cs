using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Interfaces;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.Account.Services;
using InfoID.Desktop.Features.Licensing.Services;
using InfoID.Desktop.Features.Licensing.ViewModels;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using InfoID.Shared.Contracts.Accounts;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Account.ViewModels;

/// <summary>Which panel of the Account &amp; License page is showing.</summary>
public enum AccountPanel
{
    Start,
    LogIn,
    SignUp,
    Verify,
    Forgot,
    Reset,
    SignedIn,
}

/// <summary>One license in "My licenses".</summary>
public sealed class AccountLicenseItem
{
    public AccountLicenseItem(AccountLicenseDto dto) => Dto = dto;

    public AccountLicenseDto Dto { get; }
    public string ActivationCode => Dto.ActivationCode;
    public string Title => $"InfoID {Dto.Tier}";
    public string Details =>
        (Dto.ExpiryDate is { } e ? $"Valid until {e:d}" : "Perpetual") +
        $"  ·  {Dto.ActiveMachines} of {Dto.MaxActivations} computer(s) in use" +
        (Dto.Status == "Revoked" ? "  ·  Revoked" : "");
    public bool CanActivate => Dto.Status != "Revoked";
}

/// <summary>One request in "My requests".</summary>
public sealed class AccountRequestItem
{
    public AccountRequestItem(AccountRequestDto dto) => Dto = dto;

    public AccountRequestDto Dto { get; }
    public string Title => $"#{Dto.Id}  {Dto.PlanName}  ·  {Dto.Machines} computer(s)";
    public string StatusText => Dto.Status switch
    {
        "New" => "Received - our team will contact you",
        "InProgress" => "In progress",
        "Approved" => "Approved" + (Dto.ActivationCode is { } c ? $" - code {c}" : ""),
        "Rejected" => "Not approved" + (Dto.StatusReason is { } r ? $": {r}" : ""),
        _ => Dto.Status,
    };
    public string DateText => Dto.CreatedDate.ToLocalTime().ToString("d");
}

/// <summary>
/// Account &amp; License page (like the reference app's "Account and license"):
///   - Activation code box: works WITHOUT an account (FR-SEC-4: login is never required).
///   - Log in / Create account (email verified with a 6-digit code) / Forgot password.
///   - Signed in: My licenses (one-click "Activate on this computer") and My requests.
///   - Editions and Request a plan.
/// Accounts live on the License API server (one account for all products); the session
/// is remembered (AccountSession, DPAPI-protected).
/// </summary>
public sealed partial class AccountLicenseViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IServiceScopeFactory _scopes;
    private readonly CustomerPortalClient _portal;
    private readonly LicenseBadgeState _license;

    public AccountLicenseViewModel(INavigationService navigation, IServiceScopeFactory scopes, CustomerPortalClient portal,
        AccountSession session, LicenseBadgeState license)
    {
        _navigation = navigation;
        _scopes = scopes;
        _portal = portal;
        _license = license;
        Session = session;

        // Signing out from the header menu while this page is open.
        Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AccountSession.IsSignedIn) || Session.IsSignedIn) return;
            Licenses.Clear();
            Requests.Clear();
            LicenseCount = 0;
            RequestCount = 0;
            if (Panel == AccountPanel.SignedIn) Panel = AccountPanel.Start;
        };
    }

    public AccountSession Session { get; }

    // ---------------------------------------------------------------- panel state ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStart), nameof(IsLogIn), nameof(IsSignUp), nameof(IsVerify), nameof(IsForgot), nameof(IsReset), nameof(IsSignedIn), nameof(ShowAccountCards))]
    private AccountPanel _panel = AccountPanel.Start;

    public bool IsStart => Panel == AccountPanel.Start;
    public bool IsLogIn => Panel == AccountPanel.LogIn;
    public bool IsSignUp => Panel == AccountPanel.SignUp;
    public bool IsVerify => Panel == AccountPanel.Verify;
    public bool IsForgot => Panel == AccountPanel.Forgot;
    public bool IsReset => Panel == AccountPanel.Reset;
    public bool IsSignedIn => Panel == AccountPanel.SignedIn;
    /// <summary>"Already have an account? / Create an account" cards: only signed out.</summary>
    public bool ShowAccountCards => Panel == AccountPanel.Start;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    /// <summary>This computer's license, e.g. "InfoID Professional" or "Not activated".</summary>
    public string LicenseText => _license.WindowTitle;

    // ---------------------------------------------------------------- activation code ----

    [ObservableProperty] private string _activationCode = "";

    // ---------------------------------------------------------------- forms ----

    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _confirmPassword = "";
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string _companyName = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _country = "";
    [ObservableProperty] private bool _acceptTerms;
    [ObservableProperty] private string _code = "";

    // ---------------------------------------------------------------- signed in ----

    public ObservableCollection<AccountLicenseItem> Licenses { get; } = new();
    public ObservableCollection<AccountRequestItem> Requests { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoLicenses))]
    private int _licenseCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoRequests))]
    private int _requestCount;

    public bool HasNoLicenses => LicenseCount == 0;
    public bool HasNoRequests => RequestCount == 0;

    public string AccountName => Session.Profile?.FullName ?? "";
    public string AccountEmail => Session.Profile?.Email ?? "";

    // ---------------------------------------------------------------- lifecycle ----

    public override void OnNavigatedTo(object? parameter)
    {
        _ = _license.RefreshAsync().ContinueWith(_ => OnPropertyChanged(nameof(LicenseText)),
            TaskScheduler.FromCurrentSynchronizationContext());

        if (Session.IsSignedIn)
        {
            Panel = AccountPanel.SignedIn;
            _ = LoadAccountAsync();
        }
        else
        {
            // e.g. the header's "Log in" button opens the Log in form directly.
            Clear();
            Panel = parameter is AccountPanel requested && requested != AccountPanel.SignedIn ? requested : AccountPanel.Start;
        }
    }

    private void Clear()
    {
        ErrorMessage = null;
        InfoMessage = null;
    }

    // ---------------------------------------------------------------- navigation between panels ----

    [RelayCommand] private void ShowStart() { Clear(); Panel = Session.IsSignedIn ? AccountPanel.SignedIn : AccountPanel.Start; }
    [RelayCommand] private void ShowLogIn() { Clear(); Password = ""; Panel = AccountPanel.LogIn; }
    [RelayCommand] private void ShowSignUp() { Clear(); Password = ""; ConfirmPassword = ""; Panel = AccountPanel.SignUp; }
    [RelayCommand] private void ShowForgot() { Clear(); Panel = AccountPanel.Forgot; }

    [RelayCommand] private void OpenEditions() => _navigation.NavigateTo<EditionsViewModel>();
    [RelayCommand] private void RequestPlan() => _navigation.NavigateTo<RequestPlanViewModel>();
    [RelayCommand] private void OpenLicenseDetails() => _navigation.NavigateTo<LicenseStatusViewModel>();
    [RelayCommand] private void GoHome() => _navigation.NavigateToRoot<WelcomeViewModel>();

    // ---------------------------------------------------------------- activation (no account needed) ----

    [RelayCommand]
    private Task ActivateCode() => ActivateAsync(ActivationCode);

    [RelayCommand]
    private Task ActivateLicense(AccountLicenseItem? item) => item is null ? Task.CompletedTask : ActivateAsync(item.ActivationCode);

    /// <summary>No internet: the existing offline screen (QR/phone or request code).</summary>
    [RelayCommand]
    private void ActivateOffline() =>
        _navigation.NavigateTo<LicenseActivationViewModel>(new LicenseActivationRequest(ActivationCode, ShowOffline: true));

    private async Task ActivateAsync(string code)
    {
        Clear();
        if (string.IsNullOrWhiteSpace(code))
        {
            ErrorMessage = "Enter the activation code from your purchase.";
            return;
        }

        IsBusy = true;
        try
        {
            using var scope = _scopes.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<ILicenseService>().ActivateOnlineAsync(code);
            if (!result.Succeeded)
            {
                ErrorMessage = result.IsServerUnreachable
                    ? result.Error + " No internet on this computer? Use \"Activate offline\"."
                    : result.Error;
                return;
            }

            _navigation.NavigateToRoot<LicenseStatusViewModel>();
        }
        catch (Exception ex)
        {
            ErrorMessage = "Activation failed: " + ex.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------------------------------------------------------- account ----

    [RelayCommand]
    private Task LogIn() => RunAsync(async () =>
    {
        var response = await _portal.LogInAsync(new LogInRequest { Email = Email, Password = Password, AppVersion = CustomerPortalClient.AppVersion });
        await HandleAccountResponseAsync(response);
    });

    [RelayCommand]
    private Task SignUp() => RunAsync(async () =>
    {
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "The passwords don't match.";
            return;
        }

        var response = await _portal.SignUpAsync(new SignUpRequest
        {
            FullName = FullName,
            CompanyName = CompanyName,
            Email = Email,
            Phone = Phone,
            Country = Country,
            Password = Password,
            AcceptTerms = AcceptTerms,
        });
        await HandleAccountResponseAsync(response);
    });

    [RelayCommand]
    private Task Verify() => RunAsync(async () =>
    {
        var response = await _portal.VerifyAsync(new VerifyEmailRequest { Email = Email, Code = Code, AppVersion = CustomerPortalClient.AppVersion });
        await HandleAccountResponseAsync(response);
    });

    [RelayCommand]
    private Task ResendCode() => RunAsync(async () =>
    {
        var response = await _portal.ResendCodeAsync(Email);
        if (response.Success) InfoMessage = $"A new code was sent to {Email}. Check your inbox (and spam folder).";
        else ErrorMessage = response.Message;
    });

    [RelayCommand]
    private Task SendResetCode() => RunAsync(async () =>
    {
        var response = await _portal.ForgotPasswordAsync(Email);
        if (!response.Success)
        {
            ErrorMessage = response.Message;
            return;
        }
        Code = "";
        Password = "";
        ConfirmPassword = "";
        Panel = AccountPanel.Reset;
        InfoMessage = $"If an account exists for {Email}, we sent it a code. Enter it below with your new password.";
    });

    [RelayCommand]
    private Task ResetPassword() => RunAsync(async () =>
    {
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "The passwords don't match.";
            return;
        }
        var response = await _portal.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = Email,
            Code = Code,
            NewPassword = Password,
            AppVersion = CustomerPortalClient.AppVersion,
        });
        await HandleAccountResponseAsync(response);
    });

    [RelayCommand]
    private async Task SignOut()
    {
        if (Session.Token is { } token) await _portal.LogOutAsync(token);
        Session.SignOut();
        Licenses.Clear();
        Requests.Clear();
        LicenseCount = 0;
        RequestCount = 0;
        Clear();
        Panel = AccountPanel.Start;
    }

    [RelayCommand]
    private Task RefreshAccount() => LoadAccountAsync();

    /// <summary>Common handling for login / sign-up / verify / reset answers.</summary>
    private async Task HandleAccountResponseAsync(AccountResponse response)
    {
        if (!response.Success)
        {
            ErrorMessage = response.Message;
            return;
        }

        if (response.NeedsVerification)
        {
            Code = "";
            Panel = AccountPanel.Verify;
            InfoMessage = response.Message ?? $"We sent a 6-digit code to {Email}. Enter it below to verify your email.";
            return;
        }

        if (response.Token is { } token)
        {
            Session.SignIn(token, response.Account);
            Password = "";
            ConfirmPassword = "";
            Code = "";
            OnPropertyChanged(nameof(AccountName));
            OnPropertyChanged(nameof(AccountEmail));
            Panel = AccountPanel.SignedIn;
            await LoadAccountAsync();
        }
    }

    private async Task LoadAccountAsync()
    {
        if (Session.Token is not { } token) return;
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(AccountEmail));
        try
        {
            var licenses = await _portal.GetLicensesAsync(token);
            var requests = await _portal.GetRequestsAsync(token);

            Licenses.Clear();
            foreach (var license in licenses.Where(l => l.ProductCode == "INFOID")) Licenses.Add(new AccountLicenseItem(license));
            Requests.Clear();
            foreach (var request in requests) Requests.Add(new AccountRequestItem(request));
            LicenseCount = Licenses.Count;
            RequestCount = Requests.Count;
        }
        catch (SessionExpiredException ex)
        {
            Session.SignOut();
            Panel = AccountPanel.LogIn;
            InfoMessage = ex.Message;
        }
        catch (LicenseServerUnavailableException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        Clear();
        IsBusy = true;
        try
        {
            await action();
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
}
