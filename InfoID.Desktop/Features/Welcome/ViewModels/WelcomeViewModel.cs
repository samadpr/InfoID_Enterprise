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

    public WelcomeViewModel(INavigationService navigationService, IRecentCardService recentCardService, IDialogService dialogService,
        InfoID.Desktop.Features.Licensing.Services.LicenseBadgeState license,
        InfoID.Desktop.Features.Account.Services.AccountSession account)
        : base(navigationService, recentCardService, dialogService)
    {
        License = license;
        _account = account;
    }

    private readonly InfoID.Desktop.Features.Account.Services.AccountSession _account;

    /// <summary>Hero title: "Welcome back, Samad" when signed in, otherwise "Welcome to InfoID".</summary>
    public string Greeting
    {
        get
        {
            var first = (_account.Profile?.FullName ?? "").Trim()
                .Split(' ', System.StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return _account.IsSignedIn && !string.IsNullOrEmpty(first) ? $"Welcome back, {first}" : "Welcome to InfoID";
        }
    }

    /// <summary>License state for the banner at the top of the page: shown only when the
    /// license needs action (not activated, expiring within 30 days, expired, not valid).</summary>
    public InfoID.Desktop.Features.Licensing.Services.LicenseBadgeState License { get; }

    /// <summary>Banner "Activate" / "Renew": Account &amp; license opens in the right state
    /// (sign in when signed out, My licenses when signed in).</summary>
    [RelayCommand]
    private void OpenAccountLicense() =>
        NavigationService.NavigateTo<InfoID.Desktop.Features.Account.ViewModels.AccountLicenseViewModel>();

    /// <summary>Banner close button: hidden until InfoID starts again.</summary>
    [RelayCommand]
    private void DismissLicenseBanner() => License.IsBannerDismissed = true;

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
        // Signing in or out happens on the Account page, so coming back here is when the
        // greeting can change. (Not subscribed to AccountSession: that singleton would
        // keep every transient Welcome page alive.)
        OnPropertyChanged(nameof(Greeting));
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

    [RelayCommand]
    private void OpenEditions() => NavigationService.NavigateTo<InfoID.Desktop.Features.Account.ViewModels.EditionsViewModel>();

    [RelayCommand]
    private void OpenReports() => NavigationService.NavigateTo<InfoID.Desktop.Features.Reports.ViewModels.ReportsViewModel>();

    /// <summary>Home tile: Backup &amp; Restore (FR-ENT-4).</summary>
    [RelayCommand]
    private void OpenBackup() => NavigationService.NavigateTo<InfoID.Desktop.Features.Backup.ViewModels.BackupViewModel>();

    [RelayCommand]
    private void OpenDashboard() => NavigationService.NavigateTo<InfoID.Desktop.Features.Dashboard.ViewModels.DashboardViewModel>();

    [RelayCommand]
    private void OpenPrintHistory() => NavigationService.NavigateTo<InfoID.Desktop.Features.PrintHistory.ViewModels.PrintHistoryViewModel>();
}
