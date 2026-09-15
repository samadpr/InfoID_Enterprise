using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.BlankCard.ViewModels;
using InfoID.Desktop.Features.Templates.ViewModels;
using InfoID.Desktop.Features.Welcome.Models;
using InfoID.Desktop.Features.Welcome.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace InfoID.Desktop.Features.Welcome.ViewModels;

/// <summary>
/// The application's landing page. Hosts the hero/welcome section, the Recent Cards
/// strip and the "Go further" shortcut tiles. Recent cards are loaded through
/// <see cref="IRecentCardService"/> so this ViewModel never touches sample data itself.
/// Open/Delete/Pin actions on individual cards live in the shared
/// <see cref="RecentCardListViewModelBase"/> -- this class only adds what's specific to
/// the Home page: a small capped preview row plus a "View More" tile once there are more
/// recent designs than fit in that row.
/// </summary>
public sealed partial class WelcomeViewModel : RecentCardListViewModelBase
{
    /// <summary>How many recent cards to fetch/hold at once (RecentCardListViewModelBase.
    /// Items). The Home page only ever *displays* PreviewCount of these; fetching more
    /// up front means "View More" can navigate straight to a fully-populated page
    /// without a second load.</summary>
    private const int FetchCount = 50;
    private const int PreviewCount = 5;

    public WelcomeViewModel(INavigationService navigationService, IRecentCardService recentCardService, IDialogService dialogService)
        : base(navigationService, recentCardService, dialogService)
    {
    }

    /// <summary>First PreviewCount of Items, refreshed whenever the base class reloads
    /// (initial load, or after a Delete/Pin action). Home's WrapPanel binds to this, not
    /// Items directly, so it never shows more than the intended preview row.</summary>
    public ObservableCollection<RecentCardItem> RecentCards { get; } = new();

    [ObservableProperty]
    private bool _hasRecentCards;

    [ObservableProperty]
    private bool _hasMoreRecentCards;

    protected override int CurrentMaxCount => FetchCount;

    public override void OnNavigatedTo(object? parameter)
    {
        _ = ReloadAsync(FetchCount);
    }

    protected override void OnItemsReloaded()
    {
        RecentCards.Clear();
        foreach (var item in Items.Take(PreviewCount)) RecentCards.Add(item);
        HasRecentCards = Items.Count > 0;
        HasMoreRecentCards = Items.Count > PreviewCount;
    }

    [RelayCommand]
    private void ViewMoreRecent() => NavigationService.NavigateTo<RecentDesignsViewModel>();

    [RelayCommand]
    private void CreateBlankCard() => NavigationService.NavigateTo<BlankCardViewModel>();

    [RelayCommand]
    private void UseTemplate() => NavigationService.NavigateTo<TemplatesViewModel>();
}
