using System;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Interfaces;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Licensing.ViewModels;

/// <summary>Navigation parameter: open the activation screen with a code, optionally
/// straight on the offline (QR/phone) section -- used by Account &amp; License.</summary>
public sealed record LicenseActivationRequest(string? ActivationCode, bool ShowOffline);

/// <summary>
/// Activation screen (FR-LIC-1/5/6), kept as short as possible:
///   Online (default):  activation code -> Activate. One step.
///   Offline (link):    activation code -> QR code appears -> scan with a phone -> type
///                      the serial key shown on the phone -> Activate. No staff needed.
///                      The request code is also shown as text for the manual path.
/// No organization/user form: online licenses bring the organization name registered
/// with the vendor; details can be edited later.
///
/// "Skip for now" is allowed on purpose: licensing never blocks opening the app or
/// viewing data (FR-SEC-7) -- only printing/issuing/encoding wait for activation.
/// </summary>
public sealed partial class LicenseActivationViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IServiceScopeFactory _scopes;

    public LicenseActivationViewModel(INavigationService navigation, IServiceScopeFactory scopes)
    {
        _navigation = navigation;
        _scopes = scopes;
    }

    [ObservableProperty] private string _activationCode = "";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _successMessage;

    /// <summary>Why this screen was opened (e.g. license from another computer).</summary>
    [ObservableProperty] private string? _reason;

    // ---------------------------------------------------------------- offline ----

    [ObservableProperty] private bool _showOffline;
    [ObservableProperty] private string? _requestCode;
    [ObservableProperty] private string? _phoneUrl;
    [ObservableProperty] private Bitmap? _qrImage;
    [ObservableProperty] private string _serialKey = "";

    /// <summary>Why the offline section can't show a request code yet (e.g. code
    /// incomplete or mistyped). Shown inside the offline section itself.</summary>
    [ObservableProperty] private string? _offlineMessage;

    public bool HasPhoneUrl => !string.IsNullOrEmpty(PhoneUrl);

    partial void OnPhoneUrlChanged(string? value) => OnPropertyChanged(nameof(HasPhoneUrl));

    public bool HasRequestCode => !string.IsNullOrEmpty(RequestCode);
    public bool HasQr => QrImage is not null;

    partial void OnRequestCodeChanged(string? value) => OnPropertyChanged(nameof(HasRequestCode));
    partial void OnQrImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasQr));

    public override void OnNavigatedTo(object? parameter)
    {
        if (parameter is string code && !string.IsNullOrWhiteSpace(code))
            ActivationCode = code;
        if (parameter is LicenseActivationRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.ActivationCode)) ActivationCode = request.ActivationCode;
            if (request.ShowOffline && !ShowOffline) _ = ToggleOffline();
        }
        _ = LoadReasonAsync();
    }

    private async Task LoadReasonAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var status = await scope.ServiceProvider.GetRequiredService<ILicenseService>().GetCurrentStatusAsync();
            if (status.HasLicense && !status.IsValid) Reason = status.Problem;
        }
        catch
        {
            // Informational only.
        }
    }

    /// <summary>A different code invalidates the offline request code / QR.</summary>
    partial void OnActivationCodeChanged(string value)
    {
        RequestCode = null;
        PhoneUrl = null;
        QrImage = null;
        if (ShowOffline) _ = PrepareOfflineAsync();
    }

    // ---------------------------------------------------------------- online ----

    [RelayCommand]
    private async Task Activate()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        if (string.IsNullOrWhiteSpace(ActivationCode))
        {
            ErrorMessage = "Enter the activation code from your purchase.";
            return;
        }

        IsBusy = true;
        try
        {
            using var scope = _scopes.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<ILicenseService>().ActivateOnlineAsync(ActivationCode);
            if (!result.Succeeded)
            {
                ErrorMessage = result.IsServerUnreachable
                    ? result.Error + " If this computer has no internet, use \"Activate offline\" below."
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

    // ---------------------------------------------------------------- offline ----

    [RelayCommand]
    private async Task ToggleOffline()
    {
        ShowOffline = !ShowOffline;
        if (ShowOffline) await PrepareOfflineAsync();
    }

    /// <summary>Builds the request code + QR as soon as the activation code is valid --
    /// no extra "Get request code" click.</summary>
    private async Task PrepareOfflineAsync()
    {
        if (string.IsNullOrWhiteSpace(ActivationCode))
        {
            OfflineMessage = "Enter your activation code above first.";
            return;
        }
        try
        {
            using var scope = _scopes.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<ILicenseService>().CreateRequestCodeAsync(ActivationCode);
            if (!result.Succeeded)
            {
                // Shown in the offline section, so the user always sees why no QR appears.
                OfflineMessage = string.Join(" ", result.Errors);
                return;
            }

            OfflineMessage = null;
            ErrorMessage = null;
            RequestCode = result.Value!.RequestCode;
            PhoneUrl = result.Value.PhoneActivationUrl;
            QrImage = PhoneUrl is null ? null : BarcodeRenderer.TryGenerate(new QrCodeElement { Value = PhoneUrl, MarginMm = 4 });
        }
        catch (Exception ex)
        {
            OfflineMessage = "Couldn't prepare offline activation: " + ex.GetBaseException().Message;
        }
    }

    /// <summary>Opens the phone page in this PC's browser -- the same page the QR code
    /// opens. Useful when the phone can't scan, and for testing while the license
    /// server runs on this PC (a phone can't reach "localhost").</summary>
    [RelayCommand]
    private void OpenPhonePage()
    {
        if (string.IsNullOrEmpty(PhoneUrl)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PhoneUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            OfflineMessage = "Couldn't open the browser: " + ex.GetBaseException().Message;
        }
    }

    [RelayCommand]
    private async Task ActivateOffline()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        if (string.IsNullOrWhiteSpace(SerialKey))
        {
            ErrorMessage = "Enter the serial key shown on your phone (or sent by your vendor).";
            return;
        }

        IsBusy = true;
        try
        {
            using var scope = _scopes.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<ILicenseService>().ActivateOfflineAsync(ActivationCode, SerialKey);
            if (!result.Succeeded)
            {
                ErrorMessage = string.Join(" ", result.Errors);
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

    [RelayCommand]
    private void SkipForNow() => _navigation.NavigateToRoot<WelcomeViewModel>();
}
