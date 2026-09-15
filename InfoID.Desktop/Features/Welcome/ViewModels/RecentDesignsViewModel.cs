using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.Welcome.Services;

namespace InfoID.Desktop.Features.Welcome.ViewModels;

/// <summary>
/// The full Recent Designs page ("View More" from Home). Shows every recent design and
/// file, not just the small preview row Home shows -- Open/Delete/Pin all come from
/// RecentCardListViewModelBase, same as Home, so behaviour is identical between the two
/// pages by construction rather than by keeping two implementations in sync by hand.
/// </summary>
public sealed partial class RecentDesignsViewModel : RecentCardListViewModelBase
{
    private const int FetchCount = 200;

    public RecentDesignsViewModel(INavigationService navigationService, IRecentCardService recentCardService, IDialogService dialogService)
        : base(navigationService, recentCardService, dialogService)
    {
    }

    protected override int CurrentMaxCount => FetchCount;

    public override void OnNavigatedTo(object? parameter)
    {
        _ = ReloadAsync(FetchCount);
    }

    [RelayCommand]
    private void GoBack() => NavigationService.GoBack();
}
