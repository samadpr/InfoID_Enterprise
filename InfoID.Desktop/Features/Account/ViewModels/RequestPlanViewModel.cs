using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Interfaces;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.Account.Services;
using InfoID.Desktop.ViewModels.Base;
using InfoID.Shared.Contracts.Accounts;

namespace InfoID.Desktop.Features.Account.ViewModels;

/// <summary>
/// "Request a plan": choose a plan and the number of computers, send contact details.
/// Signed in: details pre-filled and the request appears under My requests; the code
/// later appears under My licenses. Not signed in: the code is emailed. Our team handles
/// payment outside the system and approves the request in License Manager.
/// </summary>
public sealed partial class RequestPlanViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly CustomerPortalClient _portal;
    private readonly AccountSession _session;
    private readonly IMachineFingerprintProvider _fingerprint;

    public RequestPlanViewModel(INavigationService navigation, CustomerPortalClient portal, AccountSession session,
        IMachineFingerprintProvider fingerprint)
    {
        _navigation = navigation;
        _portal = portal;
        _session = session;
        _fingerprint = fingerprint;
    }

    public ObservableCollection<PlanDto> Plans { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPlanDetails))]
    private PlanDto? _selectedPlan;

    [ObservableProperty] private decimal? _machines = 1;
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string _companyName = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _country = "";
    [ObservableProperty] private string _stateProvince = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _consent;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForm))]
    private string? _sentMessage;

    public bool IsSignedIn => _session.IsSignedIn;
    public bool IsForm => SentMessage is null;

    public string SelectedPlanDetails => SelectedPlan is not { } p ? "" :
        (p.Tagline is { } t ? t + "\n" : "") + string.Join("\n", p.Features.Select(f => "•  " + f));

    partial void OnSelectedPlanChanged(PlanDto? value)
    {
        if (value is not null && (Machines is null || Machines < value.DefaultMachines)) Machines = value.DefaultMachines;
    }

    public override void OnNavigatedTo(object? parameter)
    {
        SentMessage = null;
        ErrorMessage = null;
        OnPropertyChanged(nameof(IsSignedIn));

        if (_session.Profile is { } profile)
        {
            FullName = profile.FullName;
            CompanyName = profile.CompanyName ?? "";
            Email = profile.Email;
            Phone = profile.Phone ?? "";
            Country = profile.Country ?? "";
        }

        _ = LoadPlansAsync(parameter as long?);
    }

    private async Task LoadPlansAsync(long? preselectId)
    {
        IsBusy = true;
        try
        {
            var plans = await _portal.GetPlansAsync();
            Plans.Clear();
            foreach (var plan in plans) Plans.Add(plan);
            SelectedPlan = Plans.FirstOrDefault(p => p.Id == preselectId) ?? Plans.FirstOrDefault();
        }
        catch (LicenseServerUnavailableException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Send()
    {
        ErrorMessage = null;
        if (SelectedPlan is null) { ErrorMessage = "Choose a plan."; return; }
        if (string.IsNullOrWhiteSpace(FullName)) { ErrorMessage = "Enter your name."; return; }
        if (!IsSignedIn && string.IsNullOrWhiteSpace(Email)) { ErrorMessage = "Enter your email address."; return; }
        if (!Consent) { ErrorMessage = "Please agree to be contacted about this request."; return; }

        string? fingerprint = null;
        try { fingerprint = _fingerprint.GetFingerprint().Hex; } catch { /* optional, support only */ }

        IsBusy = true;
        try
        {
            var response = await _portal.SubmitRequestAsync(new PlanRequestSubmission
            {
                PlanId = SelectedPlan.Id,
                Machines = (int)(Machines ?? 1),
                FullName = FullName,
                CompanyName = CompanyName,
                Email = Email,
                Phone = Phone,
                Country = Country,
                StateProvince = StateProvince,
                Message = Message,
                MachineFingerprint = fingerprint,
                AppVersion = CustomerPortalClient.AppVersion,
            }, _session.Token);

            if (!response.Success)
            {
                ErrorMessage = response.Message;
                return;
            }

            SentMessage = $"Thank you! Your request #{response.RequestId} for {SelectedPlan.Name} was sent. " +
                          "Our team will contact you about payment. Once it's confirmed, your activation code will be emailed to you" +
                          (IsSignedIn ? " and will appear under My licenses." : ".");
        }
        catch (LicenseServerUnavailableException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void GoToAccount() => _navigation.NavigateTo<AccountLicenseViewModel>();

    [RelayCommand]
    private void GoBack()
    {
        if (_navigation.CanGoBack) _navigation.GoBack();
    }
}
