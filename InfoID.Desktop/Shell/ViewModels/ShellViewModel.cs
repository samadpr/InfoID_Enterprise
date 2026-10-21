using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Core.Services;
using InfoID.Desktop.Features.BlankCard.ViewModels;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.Features.Licensing.Services;
using InfoID.Desktop.Features.Licensing.ViewModels;
using InfoID.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using InfoID.Desktop.Features.Templates.ViewModels;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace InfoID.Desktop.Shell.ViewModels;

/// <summary>
/// Root ViewModel for the application window. Owns the header state (theme, back
/// navigation) and hosts whatever page the <see cref="INavigationService"/> currently
/// points at in its content area. This is the single always-alive ViewModel for the
/// app's lifetime; every page underneath it is created/disposed by navigation.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<ShellViewModel>();

    private readonly INavigationService _navigationService;
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogService;
    private readonly IServiceScopeFactory _scopes;

    /// <summary>The one, singleton Card Designer instance (see App.axaml.cs) --
    /// injected directly rather than resolved through navigation, so its Tabs are
    /// reachable (for the header's "Windows" menu) even while some other page is
    /// currently shown. Unlike ActiveDesigner below, this is never null.</summary>
    public CardDesignerViewModel Designer { get; }

    public ShellViewModel(INavigationService navigationService, IThemeService themeService, IDialogService dialogService,
        CardDesignerViewModel designer, IServiceScopeFactory scopes, LicenseBadgeState licenseBadge,
        InfoID.Desktop.Features.Account.Services.AccountSession account,
        InfoID.Desktop.Features.Account.Services.CustomerPortalClient portal,
        InfoID.Desktop.Features.Trial.Services.TrialModeState trial)
    {
        Trial = trial;
        LicenseBadge = licenseBadge;
        Account = account;
        _portal = portal;
        Account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Account.Profile) or nameof(Account.IsSignedIn))
            {
                OnPropertyChanged(nameof(AccountInitials));
                OnPropertyChanged(nameof(AccountFirstName));
                OnPropertyChanged(nameof(AccountToolTip));
            }
        };
        LicenseBadge.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LicenseBadgeState.StatusSummary))
                OnPropertyChanged(nameof(AccountToolTip));
        };
        _navigationService = navigationService;
        _themeService = themeService;
        _dialogService = dialogService;
        _scopes = scopes;
        Designer = designer;

        _navigationService.CurrentViewModelChanged += (_, _) =>
        {
            CurrentPage = _navigationService.CurrentViewModel;
            GoBackCommand.NotifyCanExecuteChanged();
        };

        _themeService.ThemeChanged += (_, theme) => IsDarkTheme = theme == AppTheme.Dark;
        IsDarkTheme = _themeService.CurrentTheme == AppTheme.Dark;

        _navigationService.NavigateToRoot<WelcomeViewModel>();
        CurrentPage = _navigationService.CurrentViewModel;

        _ = CheckLicenseAtStartupAsync();
    }

    /// <summary>Start-up license check. InfoID always opens on the Welcome page (Oct 2026
    /// redesign); with no valid license on this computer (FR-LIC-1) the Welcome banner and
    /// the header account icon's status dot point the user to Account &amp; license instead
    /// of forcing that page open. Licensing never blocks opening the app (FR-SEC-7).</summary>
    private async Task CheckLicenseAtStartupAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var license = scope.ServiceProvider.GetRequiredService<ILicenseService>();

            // Online licenses: quiet background check-in (every few days) so renewals
            // and revocations arrive without the user doing anything (FR-LIC-13).
            // Never blocks or throws for network problems.
            await license.CheckInAsync();

            var status = await license.GetCurrentStatusAsync();
            LicenseBadge.Apply(status);
            await Trial.RefreshAsync();
        }
        catch (Exception ex)
        {
            // Never stop the app from opening because of a license check problem;
            // the License page shows the details.
            Log.Error("License startup check failed", ex);
        }
    }

    /// <summary>Trial mode: header "Trial · 3/10 cards" while signed in without a license.</summary>
    public InfoID.Desktop.Features.Trial.Services.TrialModeState Trial { get; }

    /// <summary>License edition (window title, header Editions/Upgrade button).</summary>
    public LicenseBadgeState LicenseBadge { get; }

    private readonly InfoID.Desktop.Features.Account.Services.CustomerPortalClient _portal;

    /// <summary>Optional customer account (header top-right: Log in / avatar menu).</summary>
    public InfoID.Desktop.Features.Account.Services.AccountSession Account { get; }

    public string AccountFirstName =>
        (Account.Profile?.FullName ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Account";

    public string AccountInitials
    {
        get
        {
            var parts = (Account.Profile?.FullName ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
            };
        }
    }

    /// <summary>Header account icon tooltip: who is signed in and the license status,
    /// e.g. "Account &amp; license · Not activated (Ctrl+Shift+L)".</summary>
    public string AccountToolTip =>
        "Account & license" +
        (Account.IsSignedIn && !string.IsNullOrWhiteSpace(Account.Profile?.Email) ? $" · {Account.Profile!.Email}" : " · Signed out") +
        $" · {LicenseBadge.StatusSummary} (Ctrl+Shift+L)";

    /// <summary>Header "Log in": the Account page, straight on the Log in form.</summary>
    [RelayCommand]
    private void AccountLogIn() =>
        _navigationService.NavigateTo<InfoID.Desktop.Features.Account.ViewModels.AccountLicenseViewModel>(
            InfoID.Desktop.Features.Account.ViewModels.AccountPanel.LogIn);

    /// <summary>Header avatar menu: Sign out (server session revoked, local one removed).</summary>
    [RelayCommand]
    private async Task AccountSignOut()
    {
        if (Account.Token is { } token) await _portal.LogOutAsync(token);
        Account.SignOut();
    }

    /// <summary>Help > Account and license: activation code, optional account, my
    /// licenses, editions and plan requests. License details (FR-LIC-9) are one click
    /// away from there.</summary>
    [RelayCommand]
    private void OpenLicense() => _navigationService.NavigateTo<InfoID.Desktop.Features.Account.ViewModels.AccountLicenseViewModel>();

    /// <summary>Help > Editions.</summary>
    [RelayCommand]
    private void OpenEditions() => _navigationService.NavigateTo<InfoID.Desktop.Features.Account.ViewModels.EditionsViewModel>();

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private bool _isDarkTheme;

    /// <summary>Non-null only while the Card Designer is the active page. The header's
    /// File/Edit/View/Windows menus bind through this so designer-specific commands
    /// (Save, Undo, Zoom, ...) are only visible/enabled when there's actually a designer
    /// to act on, instead of the previous always-on-but-does-nothing menu (Part 5/6/22).</summary>
    public CardDesignerViewModel? ActiveDesigner => CurrentPage as CardDesignerViewModel;

    partial void OnCurrentPageChanged(ViewModelBase? value) => OnPropertyChanged(nameof(ActiveDesigner));

    /// <summary>Windows menu: jumps to an already-open design tab from anywhere in the
    /// app (e.g. from Home), without disturbing any other open tabs. Sets the tab
    /// active directly on the persistent Designer instance, then navigates to it --
    /// navigating with no parameter is a safe no-op on an already-populated designer
    /// (see CardDesignerViewModel.OpenFromParameterAsync), so this never adds a
    /// spurious blank tab on top of the one being switched to.</summary>
    [RelayCommand]
    private void ActivateDesignTab(CardDesignTabViewModel tab)
    {
        Designer.ActiveTab = tab;

        // Only actually navigate if some other page is currently shown -- if the
        // designer is already the active page (just switching between its own open
        // tabs), setting ActiveTab above is all that's needed, and skipping
        // NavigateTo here avoids pushing a redundant same-instance back-stack entry.
        if (CurrentPage is not CardDesignerViewModel)
        {
            _navigationService.NavigateTo<CardDesignerViewModel>();
        }
    }

    public bool CanGoBack => _navigationService.CanGoBack;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => _navigationService.GoBack();

    [RelayCommand]
    private void ToggleTheme() => _themeService.Toggle();

    [RelayCommand]
    private void GoHome() => _navigationService.NavigateToRoot<WelcomeViewModel>();

    /// <summary>File > New Card: same entry point as the Home screen's own "New Card"
    /// action (Part 6) -- the format-picker flow, not a bare blank tab, so a user
    /// starting from the header menu gets the same experience either way.</summary>
    [RelayCommand]
    private void NewCard() => _navigationService.NavigateTo<BlankCardViewModel>();

    /// <summary>File > Open: the closest real equivalent to "open a design" this
    /// codebase has today is the Template gallery/list of saved designs (Part 6/62).
    /// There is no separate file-open dialog -- InfoID designs aren't loose files, they
    /// live in the local database.</summary>
    [RelayCommand]
    private void OpenTemplates() => _navigationService.NavigateTo<TemplatesViewModel>();

    /// <summary>Header button / File > Dashboard (SRS 4.1).</summary>
    [RelayCommand]
    private void OpenDashboard() =>
        _navigationService.NavigateTo<InfoID.Desktop.Features.Dashboard.ViewModels.DashboardViewModel>();

    /// <summary>Help > Open log folder: where InfoID-yyyy-MM-dd.log files are, for support.</summary>
    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            var folder = InfoID.Infrastructure.Persistence.DatabaseLocation.GetLogsDirectory();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            InfoID.Shared.Logging.AppLog.For<ShellViewModel>().Warn("Couldn't open the log folder", ex);
        }
    }

    /// <summary>File > Reports (SRS 4.11).</summary>
    [RelayCommand]
    private void OpenReports() =>
        _navigationService.NavigateTo<InfoID.Desktop.Features.Reports.ViewModels.ReportsViewModel>();

    /// <summary>File > Audit log (FR-REP-4, FR-REP-7).</summary>
    [RelayCommand]
    private void OpenAuditLog() =>
        _navigationService.NavigateTo<InfoID.Desktop.Features.Audit.ViewModels.AuditLogViewModel>();

    /// <summary>File > Visitors (Visitor Management, SRS 4.12: import visitors, print badges).</summary>
    [RelayCommand]
    private void OpenVisitors() =>
        _navigationService.NavigateTo<InfoID.Desktop.Features.Visitors.ViewModels.VisitorsViewModel>();

    /// <summary>File > Backup &amp; Restore (FR-ENT-4).</summary>
    [RelayCommand]
    private void OpenBackup() =>
        _navigationService.NavigateTo<InfoID.Desktop.Features.Backup.ViewModels.BackupViewModel>();

    /// <summary>File > Print History (FR-PRN-6).</summary>
    [RelayCommand]
    private void OpenPrintHistory() =>
        _navigationService.NavigateTo<InfoID.Desktop.Features.PrintHistory.ViewModels.PrintHistoryViewModel>();

    /// <summary>File > Exit. Goes through the classic desktop lifetime's own Shutdown
    /// rather than Environment.Exit, so any registered OnExit/shutdown hooks still run
    /// normally instead of being skipped.</summary>
    [RelayCommand]
    private void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>Full Screen (Part 24) is a plain window-state toggle, not something
    /// specific to any one page, so the ViewModel can't reach the Window directly (Views
    /// own Windows, not ViewModels). ShellView.axaml.cs subscribes to this event once
    /// and flips its own WindowState -- same request/react shape already used elsewhere
    /// in this codebase (e.g. CardCanvasView.TextEditRequested).</summary>
    public event EventHandler? ToggleFullScreenRequested;

    [RelayCommand]
    private void ToggleFullScreen() => ToggleFullScreenRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task ShowAbout() =>
        await _dialogService.ShowDialogAsync<AboutViewModel, bool>(new AboutViewModel());
}

