using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Dtos.Licensing;
using InfoID.Application.Interfaces;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.Licensing.Services;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using InfoID.Domain.Common.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Licensing.ViewModels;

/// <summary>License Status page (FR-LIC-9): tier, expiry countdown, card usage, masked
/// machine ID, organization, plus Renew/Activate and Deactivate/Transfer (FR-LIC-10).</summary>
public sealed partial class LicenseStatusViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IServiceScopeFactory _scopes;
    private readonly IDialogService _dialogs;
    private readonly LicenseBadgeState _badge;

    public LicenseStatusViewModel(INavigationService navigation, IServiceScopeFactory scopes, IDialogService dialogs, LicenseBadgeState badge)
    {
        _badge = badge;
        _navigation = navigation;
        _scopes = scopes;
        _dialogs = dialogs;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLicensed), nameof(IsNotLicensed), nameof(StatusText), nameof(TierText),
        nameof(ExpiryText), nameof(CardsText), nameof(ActivatedText), nameof(IsWarning), nameof(ConfirmedText), nameof(IsOnline))]
    private LicenseStatusDto? _status;

    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;
    [ObservableProperty] private bool _isChecking;

    public bool IsLicensed => Status is { IsValid: true };
    public bool IsNotLicensed => !IsLicensed;

    /// <summary>Grace period or near expiry: shown in the warning color.</summary>
    public bool IsWarning => Status is { IsInGracePeriod: true } || Status?.DaysUntilExpiry is >= 0 and <= 30;

    public string StatusText => Status switch
    {
        null => "Checking...",
        { HasLicense: false } => "Not activated",
        { IsValid: false } => "Not valid on this computer",
        { Status: LicenseStatus.GracePeriod } => $"Expired - grace period ({Status.GracePeriodDaysRemaining} day(s) left)",
        { Status: LicenseStatus.Expired } => "Expired - printing and issuing are locked",
        { Status: LicenseStatus.Deactivated } => "Deactivated",
        _ => "Active",
    };

    public string TierText => Status?.Tier is { } tier ? $"{tier} ({Status.Type})" : "-";

    public string ExpiryText => Status switch
    {
        { IsValid: false } or null => "-",
        { ExpiryDate: null } => "Never (perpetual)",
        { ExpiryDate: { } date, DaysUntilExpiry: { } days } when days >= 0 => $"{date.ToLocalTime():d} ({days} day(s) left)",
        { ExpiryDate: { } date } => $"{date.ToLocalTime():d}",
    };

    public string CardsText => Status switch
    {
        { IsValid: false } or null => "-",
        { MaxCardsPerPeriod: null } => $"Unlimited ({Status.CardsIssuedThisPeriod:N0} printed this period)",
        _ => $"{Status.CardsIssuedThisPeriod:N0} of {Status.MaxCardsPerPeriod:N0} this {(Status.CardLimitPeriod == "Yearly" ? "year" : "month")}",
    };

    public string ActivatedText => Status?.ActivationDate is { } date
        ? $"{date.ToLocalTime():d} ({Status.ActivationMode})"
        : "-";

    public bool IsOnline => Status?.ActivationMode == ActivationMode.Online;

    /// <summary>Online licenses: when the server last confirmed it, and the deadline.</summary>
    public string ConfirmedText => Status switch
    {
        { ActivationMode: not ActivationMode.Online } or null => "Offline license (no online checks)",
        { LastConfirmedOnline: { } last, CheckInDeadline: { } deadline } =>
            $"{last.ToLocalTime():g} (must confirm again before {deadline.ToLocalTime():d})",
        { LastConfirmedOnline: { } last } => $"{last.ToLocalTime():g}",
        _ => "Never",
    };

    public override void OnNavigatedTo(object? parameter) => _ = Refresh();

    /// <summary>Confirms the license with the server now (FR-LIC-13): picks up an
    /// extension or a revoke immediately instead of waiting for the automatic check.</summary>
    [RelayCommand]
    private async Task CheckNow()
    {
        IsChecking = true;
        try
        {
            using var scope = _scopes.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<ILicenseService>().CheckInAsync(force: true);
            Message = result.Succeeded ? result.Value : string.Join(" ", result.Errors);
            MessageIsError = !result.Succeeded;
            await Refresh();
        }
        catch (Exception ex)
        {
            Message = "Couldn't check the license: " + ex.GetBaseException().Message;
            MessageIsError = true;
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            Status = await scope.ServiceProvider.GetRequiredService<ILicenseService>().GetCurrentStatusAsync();
            // Keep the header badge in step (after activate, deactivate, Check now).
            _badge.Apply(Status);
        }
        catch (Exception ex)
        {
            Message = "Couldn't read the license: " + ex.GetBaseException().Message;
            MessageIsError = true;
        }
    }

    /// <summary>Activate (no license) or Renew (e.g. after the vendor extended it):
    /// same offline flow, with the current activation code pre-filled.</summary>
    [RelayCommand]
    private void ActivateOrRenew() => _navigation.NavigateTo<LicenseActivationViewModel>(Status?.ActivationCode);

    [RelayCommand]
    private async Task Deactivate()
    {
        if (Status is not { HasLicense: true }) return;

        var confirmed = await _dialogs.ShowDialogAsync<MessageDialogViewModel, bool>(MessageDialogViewModel.Confirmation(
            "Deactivate license",
            "This removes the license from this computer. Printing and card issuing will stop here until you activate again.\n\n" +
            "Use this before moving InfoID to another computer. Afterwards, send the details shown to your vendor so they can free the license for the new computer.",
            confirmText: "Deactivate"));
        if (!confirmed) return;

        try
        {
            using var scope = _scopes.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<ILicenseService>().DeactivateAsync();
            if (!result.Succeeded)
            {
                Message = string.Join(" ", result.Errors);
                MessageIsError = true;
                return;
            }

            var info = result.Value!;
            var wasOnline = IsOnline;
            await _dialogs.ShowDialogAsync<MessageDialogViewModel, bool>(new MessageDialogViewModel(
                "License deactivated",
                wasOnline
                    ? $"The license was released. You can now activate InfoID on another computer with the same activation code:\n\n{info.ActivationCode}"
                    : "Send these details to your vendor to free the license for another computer:\n\n" +
                      $"Activation code: {info.ActivationCode}\nMachine ID: {info.MachineId}"));
            await Refresh();
        }
        catch (Exception ex)
        {
            Message = "Deactivation failed: " + ex.GetBaseException().Message;
            MessageIsError = true;
        }
    }

    [RelayCommand]
    private void GoHome() => _navigation.NavigateToRoot<WelcomeViewModel>();
}
