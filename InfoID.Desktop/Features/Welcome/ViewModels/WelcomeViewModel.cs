using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.BlankCard.ViewModels;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.Features.Templates.ViewModels;
using InfoID.Desktop.Features.Welcome.Models;
using InfoID.Desktop.Features.Welcome.Services;
using InfoID.Desktop.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.Welcome.ViewModels;

/// <summary>
/// The application's landing page. Hosts the hero/welcome section, the Recent Cards
/// strip and the "Go further" shortcut tiles. Recent cards are loaded through
/// <see cref="IRecentCardService"/> so this ViewModel never touches sample data itself.
/// </summary>
public sealed partial class WelcomeViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IRecentCardService _recentCardService;

    public WelcomeViewModel(INavigationService navigationService, IRecentCardService recentCardService)
    {
        _navigationService = navigationService;
        _recentCardService = recentCardService;
    }

    public ObservableCollection<RecentCardItem> RecentCards { get; } = new();

    [ObservableProperty]
    private bool _hasRecentCards;

    public override void OnNavigatedTo(object? parameter)
    {
        _ = LoadRecentCardsAsync();
    }

    private async Task LoadRecentCardsAsync()
    {
        var items = await _recentCardService.GetRecentCardsAsync();
        RecentCards.Clear();
        foreach (var item in items)
        {
            RecentCards.Add(item);
        }
        HasRecentCards = RecentCards.Count > 0;
    }

    [RelayCommand]
    private void OpenRecent(RecentCardItem item)
    {
        if (long.TryParse(item.Id, out var templateId))
        {
            _navigationService.NavigateTo<CardDesignerViewModel>(templateId);
        }
    }

    [RelayCommand]
    private void CreateBlankCard() => _navigationService.NavigateTo<BlankCardViewModel>();

    [RelayCommand]
    private void UseTemplate() => _navigationService.NavigateTo<TemplatesViewModel>();
}
