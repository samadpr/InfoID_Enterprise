using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.Features.Welcome.Models;
using InfoID.Desktop.Features.Welcome.Services;
using InfoID.Desktop.ViewModels.Base;

namespace InfoID.Desktop.Features.Welcome.ViewModels;

/// <summary>
/// Shared Open/Delete/Pin behaviour for any page that shows a list of RecentCardItems
/// (Part: right-click actions on recent cards, "enterprise level"). Both WelcomeViewModel
/// (the Home page's small preview row) and RecentDesignsViewModel (the full "View More"
/// page) derive from this instead of each having their own copy -- this codebase has
/// already had more than one bug from the same logic existing twice and drifting apart
/// (autosave vs. manual save being the most recent), so this one is deliberately
/// factored out up front instead of duplicated and fixed later.
/// </summary>
public abstract partial class RecentCardListViewModelBase : ViewModelBase
{
    protected readonly INavigationService NavigationService;
    private readonly IRecentCardService _recentCardService;
    private readonly IDialogService _dialogService;

    protected RecentCardListViewModelBase(INavigationService navigationService, IRecentCardService recentCardService, IDialogService dialogService)
    {
        NavigationService = navigationService;
        _recentCardService = recentCardService;
        _dialogService = dialogService;
    }

    public ObservableCollection<RecentCardItem> Items { get; } = new();

    protected async Task ReloadAsync(int maxCount)
    {
        var items = await _recentCardService.GetRecentCardsAsync(maxCount);
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnItemsReloaded();
    }

    /// <summary>Hook for a derived page to update its own computed state (e.g. Home's
    /// "how many are there in total, do I need a View More tile") after a reload -- the
    /// base class doesn't know about that, it just owns Open/Delete/Pin.</summary>
    protected virtual void OnItemsReloaded()
    {
    }

    [RelayCommand]
    private void OpenRecent(RecentCardItem item)
    {
        // "file:" prefix distinguishes a file-backed design (no database Template row)
        // from a normal templateId -- see RecentCardService.GetRecentCardsAsync.
        if (item.Id.StartsWith("file:", System.StringComparison.Ordinal))
        {
            var filePath = item.Id["file:".Length..];
            NavigationService.NavigateTo<CardDesignerViewModel>(new OpenFileDesignRequest(filePath));
        }
        else if (long.TryParse(item.Id, out var templateId))
        {
            NavigationService.NavigateTo<CardDesignerViewModel>(templateId);
        }
    }

    /// <summary>Always confirms first -- deleting a database design is permanent from
    /// the user's point of view (soft-delete under the hood, but there's no "undo" UI
    /// for it), and even "remove from recent" for a file-backed entry is worth a beat
    /// of friction since a fat-fingered right-click shouldn't silently drop something.</summary>
    [RelayCommand]
    private async Task DeleteRecent(RecentCardItem item)
    {
        var confirmed = await _dialogService.ShowDialogAsync<MessageDialogViewModel, bool>(
            MessageDialogViewModel.Confirmation("Delete design?", $"Remove \"{item.Name}\" from InfoID? This can't be undone from here."));

        if (!confirmed) return;

        await _recentCardService.DeleteAsync(item);
        await ReloadAsync(CurrentMaxCount);
    }

    [RelayCommand]
    private async Task TogglePinRecent(RecentCardItem item)
    {
        await _recentCardService.TogglePinAsync(item);
        await ReloadAsync(CurrentMaxCount);
    }

    /// <summary>How many items the derived page currently wants (Home's small preview
    /// vs. the full Recent Designs page) -- Delete/TogglePin above reload with this same
    /// count afterward instead of a hardcoded number.</summary>
    protected abstract int CurrentMaxCount { get; }
}
