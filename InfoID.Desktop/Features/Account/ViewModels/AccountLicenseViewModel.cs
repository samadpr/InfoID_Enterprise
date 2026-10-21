using System;
using System.Collections.ObjectModel;
using System.Globalization;
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
using InfoID.Domain.Common.Enums;
using InfoID.Shared.Contracts.Accounts;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Account.ViewModels;

/// <summary>Which panel of the Account &amp; License page is showing.</summary>
public enum AccountPanel
{
    /// <summary>Kept for callers that pass it; signed out it now shows the Sign in form.</summary>
    Start,
    LogIn,
    SignUp,
    Verify,
    Forgot,
    Reset,
    SignedIn,
}

/// <summary>Dates on this page read "3 Aug 2027": unambiguous in every regional format.</summary>
internal static class AccountDates
{
    public static string Format(DateTime date) => date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    public static string Format(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
}

/// <summary>One license in "My licenses".</summary>
public sealed partial class AccountLicenseItem : ObservableObject
{
    /// <param name="thisComputerCode">Activation code of the license active on this
    /// computer (null when none), to mark "Active on this computer".</param>
    public AccountLicenseItem(AccountLicenseDto dto, string? thisComputerCode = null)
    {
        Dto = dto;
        IsOnThisComputer = thisComputerCode is not null && Normalize(thisComputerCode) == Normalize(dto.ActivationCode);
    }

    public AccountLicenseDto Dto { get; }
    public string ActivationCode => Dto.ActivationCode;
    public string Title => $"InfoID {Dto.Tier}";

    /// <summary>Short edition mark for the tile: STD, PRO, ENT.</summary>
    public string TierMark => Dto.Tier.Length >= 3 ? Dto.Tier[..3].ToUpperInvariant() : Dto.Tier.ToUpperInvariant();

    public bool IsOnThisComputer { get; }
    public bool IsRevoked => Dto.Status == "Revoked";
    public int? DaysLeft => Dto.ExpiryDate is { } e ? e.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber : null;
    public bool IsExpired => DaysLeft is < 0;
    public bool AllComputersInUse => Dto.MaxActivations > 0 && Dto.ActiveMachines >= Dto.MaxActivations;

    // ---- key: hidden by default, "Show" reveals it ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayCode), nameof(RevealToolTip))]
    private bool _isKeyRevealed;

    /// <summary>"FXAW-••••-••••-XEZD" until revealed.</summary>
    public string DisplayCode => IsKeyRevealed ? ActivationCode : Mask(ActivationCode);
    public string RevealToolTip => IsKeyRevealed ? "Hide license key" : "Show license key";

    [RelayCommand]
    private void ToggleReveal() => IsKeyRevealed = !IsKeyRevealed;

    // ---- usage and expiry ----

    public string ComputersText => $"{Dto.ActiveMachines} of {Dto.MaxActivations} in use";
    public double ComputersPercent => Dto.MaxActivations <= 0 ? 0 : Math.Clamp(100.0 * Dto.ActiveMachines / Dto.MaxActivations, 0, 100);
    public string ExpiryText => Dto.ExpiryDate is { } e ? AccountDates.Format(e) : "Never (perpetual)";
    public string DaysLeftText => DaysLeft switch
    {
        null => "No expiry date",
        < 0 => "Expired",
        0 => "Expires today",
        1 => "1 day left",
        { } d => $"{d} days left",
    };
    public bool IsDaysLeftWarning => DaysLeft is >= 0 and <= LicenseBadgeState.ExpiringSoonDays;
    public bool IsDaysLeftOk => DaysLeft is null or > LicenseBadgeState.ExpiringSoonDays;

    // ---- status badge ----

    public string BadgeText =>
        IsRevoked ? "Revoked"
        : IsExpired ? "Expired"
        : IsOnThisComputer ? "Active on this computer"
        : AllComputersInUse ? "All computers in use"
        : "Not used on this computer";
    public bool IsBadgeOk => !IsRevoked && !IsExpired && IsOnThisComputer;
    public bool IsBadgeBad => IsRevoked || IsExpired;
    public bool IsBadgeNeutral => !IsBadgeOk && !IsBadgeBad;

    /// <summary>"Activate on this computer" is offered when the server can accept it. A
    /// full license is still offered: the server explains and the user can free a slot.</summary>
    public bool CanActivate => !IsRevoked && !IsExpired && !IsOnThisComputer;

    private static string Normalize(string code) => code.Replace("-", "").Replace(" ", "").ToUpperInvariant();

    private static string Mask(string code)
    {
        var parts = code.Split('-');
        if (parts.Length < 3) return new string('•', Math.Max(4, code.Length));
        for (var i = 1; i < parts.Length - 1; i++) parts[i] = new string('•', parts[i].Length);
        return string.Join("-", parts);
    }
}

/// <summary>One request in "My requests".</summary>
public sealed class AccountRequestItem
{
    public AccountRequestItem(AccountRequestDto dto) => Dto = dto;

    public AccountRequestDto Dto { get; }
    public string Title => $"#{Dto.Id} · {Dto.PlanName} · {Dto.Machines} computer{(Dto.Machines == 1 ? "" : "s")}";

    public string BadgeText => Dto.Status switch
    {
        "New" => "Received",
        "InProgress" => "In progress",
        "Approved" => "Approved",
        "Rejected" => "Not approved",
        _ => Dto.Status,
    };
    public bool IsBadgeOk => Dto.Status == "Approved";
    public bool IsBadgeBad => Dto.Status == "Rejected";
    public bool IsBadgeInfo => !IsBadgeOk && !IsBadgeBad;

    /// <summary>"16 Oct 2026 · Key added to My licenses".</summary>
    public string DetailText => DateText + " · " + Dto.Status switch
    {
        "New" => "Our team will contact you",
        "InProgress" => "Being prepared",
        "Approved" => Dto.ActivationCode is null ? "Approved" : "Key added to My licenses",
        "Rejected" => Dto.StatusReason ?? "Contact us for details",
        _ => Dto.Status,
    };

    public string DateText => AccountDates.Format(Dto.CreatedDate.ToLocalTime());
}

/// <summary>
/// Account &amp; License page (Oct 2026 redesign):
///   - Signed out: Sign in / Create account (email verified with a 6-digit code), Forgot
///     password, editions, and "Activate offline" for computers without internet (the
///     offline screen takes a code without an account).
///   - Signed in: a status card for THIS computer (green active, amber needs action, red
///     expired / not valid) with the one main action, My licenses (one-click "Activate on
///     this computer", hidden key, computers in use, days left), the account, and My
///     requests. The activation code box is shown after sign-in: open when there are no
///     licenses, otherwise behind "Enter an activation code".
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
    }

    /// <summary>Signing out elsewhere while this page is open (e.g. the session expired).
    /// Subscribed only while the page is showing: AccountSession is a singleton and this
    /// view model is transient, so a constructor subscription would keep every visit alive.</summary>
    private void OnSessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AccountSession.IsSignedIn) || Session.IsSignedIn) return;
        Licenses.Clear();
        Requests.Clear();
        LicenseCount = 0;
        RequestCount = 0;
        if (Panel == AccountPanel.SignedIn) Panel = AccountPanel.LogIn;
    }

    public override void OnNavigatedFrom()
    {
        Session.PropertyChanged -= OnSessionChanged;
        base.OnNavigatedFrom();
    }

    public AccountSession Session { get; }

    // ---------------------------------------------------------------- panel state ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLogIn), nameof(IsSignUp), nameof(IsVerify), nameof(IsForgot), nameof(IsReset),
        nameof(IsSignedIn), nameof(IsSignedOut), nameof(ShowSignInTabs), nameof(IsCodeEntryVisible))]
    private AccountPanel _panel = AccountPanel.LogIn;

    public bool IsLogIn => Panel is AccountPanel.LogIn or AccountPanel.Start;
    public bool IsSignUp => Panel == AccountPanel.SignUp;
    public bool IsVerify => Panel == AccountPanel.Verify;
    public bool IsForgot => Panel == AccountPanel.Forgot;
    public bool IsReset => Panel == AccountPanel.Reset;
    public bool IsSignedIn => Panel == AccountPanel.SignedIn;
    public bool IsSignedOut => !IsSignedIn;
    /// <summary>The "Sign in | Create account" switch (hidden on verify / password reset).</summary>
    public bool ShowSignInTabs => IsLogIn || IsSignUp;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;

    /// <summary>This computer's license, e.g. "InfoID Professional" or "Not activated".</summary>
    public string LicenseText => _license.WindowTitle;

    // ---------------------------------------------------------------- this computer (status card) ----

    private bool IsThisComputerActive =>
        _license.Status is { IsValid: true, Status: LicenseStatus.Active or LicenseStatus.GracePeriod };

    /// <summary>Green card: InfoID is active here.</summary>
    public bool IsStatusActive => IsThisComputerActive && !_license.IsCritical;
    public bool IsStatusWarning => !IsStatusActive && !_license.IsCritical;
    public bool IsStatusCritical => !IsStatusActive && _license.IsCritical;

    public string StatusBadgeText => IsStatusActive ? "Active" : _license.StatusSummary;

    public string StatusTitle
    {
        get
        {
            if (IsStatusActive) return $"{LicenseText} is active on this computer";
            if (_license.IsCritical) return _license.BannerTitle;
            if (SuggestedLicense is not null) return "This computer isn't activated";
            return HasNoLicenses ? "Activate InfoID on this computer" : _license.BannerTitle;
        }
    }

    public string StatusText
    {
        get
        {
            var status = _license.Status;
            if (IsStatusActive && status is not null)
            {
                var parts = new System.Collections.Generic.List<string>();
                parts.Add(status.ExpiryDate is { } e ? $"Valid until {LicenseBadgeState.FormatDate(e)}" : "Perpetual license");
                if (status.DaysUntilExpiry is { } d and >= 0) parts.Add(d == 1 ? "1 day left" : $"{d} days left");
                if (status.ActivationDate is { } a) parts.Add($"Activated {LicenseBadgeState.FormatDate(a)}");
                return string.Join(" · ", parts);
            }
            if (!_license.IsCritical && SuggestedLicense is { } suggested)
                return $"You own {suggested.Title}" +
                       (suggested.Dto.ExpiryDate is { } exp ? $", valid until {AccountDates.Format(exp)}" : "") +
                       ". Activate it here to start designing and printing.";
            if (HasNoLicenses && !_license.IsCritical)
                return "Enter the activation code from your purchase, or request a plan. Approved licenses appear below, ready to activate.";
            return _license.BannerText;
        }
    }

    /// <summary>The license the main button activates: the first one that can be used here.</summary>
    public AccountLicenseItem? SuggestedLicense =>
        IsThisComputerActive ? null : Licenses.FirstOrDefault(l => l.CanActivate && !l.AllComputersInUse) ?? Licenses.FirstOrDefault(l => l.CanActivate);

    public bool ShowActivateSuggested => IsSignedIn && SuggestedLicense is not null;
    /// <summary>Nothing to activate and InfoID isn't active: point to Request a plan.</summary>
    public bool ShowRequestPlanAction => IsSignedIn && !IsStatusActive && SuggestedLicense is null && !HasNoLicenses;

    /// <summary>Set after a successful activation: the green card offers "Start designing".</summary>
    [ObservableProperty] private bool _justActivated;

    private void RaiseStatusChanged()
    {
        OnPropertyChanged(nameof(LicenseText));
        OnPropertyChanged(nameof(IsStatusActive));
        OnPropertyChanged(nameof(IsStatusWarning));
        OnPropertyChanged(nameof(IsStatusCritical));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SuggestedLicense));
        OnPropertyChanged(nameof(ShowActivateSuggested));
        OnPropertyChanged(nameof(ShowRequestPlanAction));
        OnPropertyChanged(nameof(IsCodeEntryVisible));
    }

    // ---------------------------------------------------------------- activation code ----

    [ObservableProperty] private string _activationCode = "";

    /// <summary>"Enter an activation code" link: opens the code box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCodeEntryVisible))]
    private bool _showCodeEntry;

    /// <summary>The code box appears only after sign-in: always when there are no
    /// licenses yet, otherwise when the user asks for it.</summary>
    public bool IsCodeEntryVisible => IsSignedIn && !IsStatusActive && (ShowCodeEntry || HasNoLicenses);

    [RelayCommand]
    private void ToggleCodeEntry() => ShowCodeEntry = !ShowCodeEntry;

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
    [NotifyPropertyChangedFor(nameof(HasNoLicenses), nameof(HasLicenses), nameof(LicenseCountText))]
    private int _licenseCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoRequests))]
    private int _requestCount;

    public bool HasNoLicenses => LicenseCount == 0;
    public bool HasLicenses => LicenseCount > 0;
    public bool HasNoRequests => RequestCount == 0;
    public string LicenseCountText => LicenseCount.ToString(CultureInfo.CurrentCulture);

    public string AccountName => Session.Profile?.FullName ?? "";
    public string AccountEmail => Session.Profile?.Email ?? "";

    public string AccountInitials
    {
        get
        {
            var parts = AccountName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
            };
        }
    }

    /// <summary>"Synced 10:42" after the licenses were loaded from the server.</summary>
    [ObservableProperty] private string _syncedText = "";

    // ---------------------------------------------------------------- lifecycle ----

    public override void OnNavigatedTo(object? parameter)
    {
        Session.PropertyChanged -= OnSessionChanged;
        Session.PropertyChanged += OnSessionChanged;
        JustActivated = false;
        _ = RefreshLicenseStatusAsync();

        if (Session.IsSignedIn)
        {
            Panel = AccountPanel.SignedIn;
            _ = LoadAccountAsync();
        }
        else
        {
            // e.g. a "Create account" link can open the Sign up form directly.
            Clear();
            Panel = parameter is AccountPanel requested && requested is not (AccountPanel.SignedIn or AccountPanel.Start)
                ? requested
                : AccountPanel.LogIn;
        }
    }

    private async Task RefreshLicenseStatusAsync()
    {
        await _license.RefreshAsync();
        RaiseStatusChanged();
    }

    private void Clear()
    {
        ErrorMessage = null;
        InfoMessage = null;
    }

    // ---------------------------------------------------------------- navigation between panels ----

    [RelayCommand] private void ShowStart() { Clear(); Panel = Session.IsSignedIn ? AccountPanel.SignedIn : AccountPanel.LogIn; }
    [RelayCommand] private void ShowLogIn() { Clear(); Password = ""; Panel = AccountPanel.LogIn; }
    [RelayCommand] private void ShowSignUp() { Clear(); Password = ""; ConfirmPassword = ""; Panel = AccountPanel.SignUp; }
    [RelayCommand] private void ShowForgot() { Clear(); Panel = AccountPanel.Forgot; }

    [RelayCommand] private void OpenEditions() => _navigation.NavigateTo<EditionsViewModel>();
    [RelayCommand] private void RequestPlan() => _navigation.NavigateTo<RequestPlanViewModel>();
    [RelayCommand] private void OpenLicenseDetails() => _navigation.NavigateTo<LicenseStatusViewModel>();
    [RelayCommand] private void GoHome() => _navigation.NavigateToRoot<WelcomeViewModel>();

    // ---------------------------------------------------------------- activation ----

    [RelayCommand]
    private Task ActivateCode() => ActivateAsync(ActivationCode);

    [RelayCommand]
    private Task ActivateLicense(AccountLicenseItem? item) => item is null ? Task.CompletedTask : ActivateAsync(item.ActivationCode);

    /// <summary>Status card main button: activates the suggested license.</summary>
    [RelayCommand]
    private Task ActivateSuggested() => ActivateLicense(SuggestedLicense);

    /// <summary>No internet: the existing offline screen (QR/phone or request code). It
    /// accepts a code without an account, so offline customers are never locked out.</summary>
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

            // Stay here: the status card turns green and offers "Start designing".
            ActivationCode = "";
            ShowCodeEntry = false;
            await _license.RefreshAsync();
            _license.IsBannerDismissed = false;
            JustActivated = true;
            await LoadAccountAsync();
            RaiseStatusChanged();
            InfoMessage = $"{LicenseText} is now active on this computer.";
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

    /// <summary>The view copied a license key to the clipboard.</summary>
    public void NotifyKeyCopied()
    {
        ErrorMessage = null;
        InfoMessage = "License key copied.";
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
        ShowCodeEntry = false;
        Clear();
        Panel = AccountPanel.LogIn;
        RaiseStatusChanged();
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
            Panel = AccountPanel.SignedIn;
            await LoadAccountAsync();
        }
    }

    private async Task LoadAccountAsync()
    {
        if (Session.Token is not { } token) return;
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(AccountEmail));
        OnPropertyChanged(nameof(AccountInitials));
        try
        {
            var licenses = await _portal.GetLicensesAsync(token);
            var requests = await _portal.GetRequestsAsync(token);

            // Which license is active here, to mark it "Active on this computer".
            var thisCode = _license.Status is { HasLicense: true, IsValid: true } s && s.Status != LicenseStatus.Deactivated
                ? s.ActivationCode
                : null;

            Licenses.Clear();
            foreach (var license in licenses.Where(l => l.ProductCode == "INFOID")) Licenses.Add(new AccountLicenseItem(license, thisCode));
            Requests.Clear();
            foreach (var request in requests.OrderByDescending(r => r.CreatedDate)) Requests.Add(new AccountRequestItem(request));
            LicenseCount = Licenses.Count;
            RequestCount = Requests.Count;
            SyncedText = "Synced " + DateTime.Now.ToString("t", CultureInfo.CurrentCulture);
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
        finally
        {
            RaiseStatusChanged();
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
