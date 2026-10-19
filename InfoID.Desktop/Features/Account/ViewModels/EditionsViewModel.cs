using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Account.ViewModels;

/// <summary>One plan card on the Editions page.</summary>
public sealed class PlanCard
{
    public PlanCard(PlanDto dto, bool isCurrent)
    {
        Dto = dto;
        IsCurrent = isCurrent;
    }

    public PlanDto Dto { get; }
    public bool IsCurrent { get; }
    public string Name => Dto.Name;
    public string? Tagline => Dto.Tagline;
    public IReadOnlyList<string> Features => Dto.Features;
    public string Terms =>
        (Dto.DurationMonths is { } m ? (m % 12 == 0 ? $"{m / 12} year(s)" : $"{m} month(s)") : "Perpetual") +
        $"  ·  {Dto.DefaultMachines} computer(s)" +
        (Dto.MaxCardsPerPeriod is { } c ? $"  ·  {c:N0} cards/{(Dto.CardLimitPeriod == "Yearly" ? "year" : "month")}" : "  ·  Unlimited cards");
    public string PriceText => string.IsNullOrWhiteSpace(Dto.PriceText) ? "Contact sales" : Dto.PriceText!;
}

/// <summary>
/// Editions page: the plans from the server (managed in License Manager, so they change
/// without an app update), with the computer's current edition marked, and "Request"
/// leading to the request form. Payment happens outside the system.
/// </summary>
public sealed partial class EditionsViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly CustomerPortalClient _portal;
    private readonly IServiceScopeFactory _scopes;

    public EditionsViewModel(INavigationService navigation, CustomerPortalClient portal, IServiceScopeFactory scopes)
    {
        _navigation = navigation;
        _portal = portal;
        _scopes = scopes;
    }

    public ObservableCollection<PlanCard> Plans { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public override void OnNavigatedTo(object? parameter) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            string? currentTier = null;
            try
            {
                using var scope = _scopes.CreateScope();
                var status = await scope.ServiceProvider.GetRequiredService<ILicenseService>().GetCurrentStatusAsync();
                if (status.IsValid) currentTier = status.Tier?.ToString();
            }
            catch
            {
                // Marking the current edition is a nicety.
            }

            var plans = await _portal.GetPlansAsync();
            Plans.Clear();
            foreach (var plan in plans)
                Plans.Add(new PlanCard(plan, string.Equals(plan.Tier, currentTier, StringComparison.OrdinalIgnoreCase)));
            if (Plans.Count == 0) ErrorMessage = "No plans are available right now. Please contact us.";
        }
        catch (LicenseServerUnavailableException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = "Couldn't load the editions: " + ex.GetBaseException().Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Request(PlanCard? card) => _navigation.NavigateTo<RequestPlanViewModel>(card?.Dto.Id);

    [RelayCommand]
    private void GoBack()
    {
        if (_navigation.CanGoBack) _navigation.GoBack();
    }
}
