using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Dtos.Dashboard;
using InfoID.Application.Dtos.Licensing;
using InfoID.Application.Dtos.PrintingModule;
using InfoID.Application.Interfaces;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.BlankCard.ViewModels;
using InfoID.Desktop.Features.Dashboard.Services;
using InfoID.Desktop.Features.PrintHistory.ViewModels;
using InfoID.Desktop.Features.Printing.Services;
using InfoID.Desktop.Features.Printing.ViewModels;
using InfoID.Desktop.Features.Templates.ViewModels;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using InfoID.Domain.Common.Enums;
using InfoID.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Dashboard.ViewModels;

/// <summary>One bar of the "cards per day" trend.</summary>
public sealed record DailyBar(string DayLabel, string Tooltip, int Cards, double BarHeight, bool IsToday);

/// <summary>A card that expires soon (or already has) and is still in use.</summary>
public sealed class ExpiringRow
{
    public ExpiringRow(ExpiringCardDto dto)
    {
        Name = string.IsNullOrWhiteSpace(dto.FullName) ? "(no name)" : dto.FullName;
        Detail = dto.PersonKey is null ? dto.CardNumber : $"ID {dto.PersonKey} · {dto.CardNumber}";
        ExpiryText = dto.ExpiryDate.ToString("d");
        DaysText = dto.DaysLeft switch
        {
            0 => "today",
            1 => "tomorrow",
            _ => $"in {dto.DaysLeft} days",
        };
        IsUrgent = dto.DaysLeft <= 7;
    }

    public string Name { get; }
    public string Detail { get; }
    public string ExpiryText { get; }
    public string DaysText { get; }
    public bool IsUrgent { get; }
}

public enum HealthState { Ok, Warning, Error }

/// <summary>One system-health line (FR-DSH-5).</summary>
public sealed class HealthItem
{
    public HealthItem(string title, string detail, HealthState state)
    {
        Title = title;
        Detail = detail;
        State = state;
    }

    public string Title { get; }
    public string Detail { get; }
    public HealthState State { get; }
    public bool IsOk => State == HealthState.Ok;
    public bool IsWarning => State == HealthState.Warning;
    public bool IsError => State == HealthState.Error;
    public string StateText => State switch { HealthState.Ok => "OK", HealthState.Warning => "Check", _ => "Problem" };
}

/// <summary>
/// InfoID Dashboard (SRS 4.1): key metrics (FR-DSH-1) from Print history and the license,
/// shortcuts (FR-DSH-2), recent print activity (FR-DSH-3), show/hide widgets (FR-DSH-4)
/// and system health -- printer, database, storage, license countdown (FR-DSH-5).
/// Welcome stays the start screen; this page is opened from the header, the File menu
/// and the Home tile. Refreshes when opened, on F5 / Refresh, and every minute while shown.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<DashboardViewModel>();

    private const int ExpiringWithinDays = 30;
    private const int TrendDays = 14;
    private const double MaxBarHeight = 110;

    private readonly INavigationService _navigation;
    private readonly IServiceScopeFactory _scopes;
    private readonly IPrinterService _printerService;
    private readonly DispatcherTimer _autoRefresh;
    private bool _settingsLoaded;
    private bool _isLoading;

    public DashboardViewModel(INavigationService navigation, IServiceScopeFactory scopes, IPrinterService printerService,
        InfoID.Desktop.Features.Backup.Services.BackupSettingsStore backupSettings)
    {
        _navigation = navigation;
        _scopes = scopes;
        _printerService = printerService;
        _backupSettings = backupSettings;
        _autoRefresh = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _autoRefresh.Tick += (_, _) => _ = Refresh();

        // NavigationService calls OnNavigatedTo only for forward navigation and skips
        // OnNavigatedFrom on "go home", so follow the current page directly: refresh when
        // the dashboard comes back into view (Back) and stop the timer when it leaves.
        _navigation.CurrentViewModelChanged += (_, _) =>
        {
            if (ReferenceEquals(_navigation.CurrentViewModel, this))
            {
                if (!_autoRefresh.IsEnabled) { _autoRefresh.Start(); _ = Refresh(); }
            }
            else
            {
                _autoRefresh.Stop();
            }
        };
    }

    // ---------------------------------------------------------------- metrics ----

    [ObservableProperty] private int _cardsToday;
    [ObservableProperty] private string _cardsTodayDetail = "";
    [ObservableProperty] private int _cardsThisMonth;
    [ObservableProperty] private string _cardsThisMonthDetail = "";
    [ObservableProperty] private int _reprintsThisMonth;
    [ObservableProperty] private int _failedRunsThisMonth;
    [ObservableProperty] private int _expiringSoonCount;
    [ObservableProperty] private int _expiredCount;
    [ObservableProperty] private int _peopleWithCards;

    // ---------------------------------------------------------------- license ----

    [ObservableProperty] private string _licenseTitle = "Checking...";
    [ObservableProperty] private string _licenseDetail = "";
    [ObservableProperty] private string _licenseUsageText = "";
    [ObservableProperty] private double _licenseUsagePercent;
    [ObservableProperty] private bool _hasCardLimit;
    [ObservableProperty] private bool _licenseNeedsAttention;

    // ---------------------------------------------------------------- lists ----

    public ObservableCollection<DailyBar> DailyBars { get; } = new();
    public ObservableCollection<ExpiringRow> ExpiringCards { get; } = new();
    public ObservableCollection<PrintJobRow> RecentJobs { get; } = new();
    public ObservableCollection<HealthItem> Health { get; } = new();

    [ObservableProperty] private string _trendSummary = "";
    [ObservableProperty] private string _expiringHeader = "Expiring in the next 30 days";
    [ObservableProperty] private bool _hasExpiring;
    [ObservableProperty] private bool _hasRecent;
    [ObservableProperty] private string? _expiringMoreText;

    // ---------------------------------------------------------------- state ----

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _lastUpdatedText = "";

    public string Greeting => DateTime.Now.Hour switch
    {
        < 12 => "Good morning",
        < 17 => "Good afternoon",
        _ => "Good evening",
    };

    public string TodayText => DateTime.Today.ToString("dddd, d MMMM yyyy");

    // ---------------------------------------------------------------- widgets (FR-DSH-4) ----

    [ObservableProperty] private bool _showMetrics = true;
    [ObservableProperty] private bool _showTrend = true;
    [ObservableProperty] private bool _showExpiring = true;
    [ObservableProperty] private bool _showRecent = true;
    [ObservableProperty] private bool _showHealth = true;
    [ObservableProperty] private bool _showShortcuts = true;

    /// <summary>The trend and the expiring list share a row; hide the row when both are off.</summary>
    public bool ShowMiddleRow => ShowTrend || ShowExpiring;
    public bool ShowBottomRow => ShowRecent || ShowHealth;

    private static readonly HashSet<string> WidgetProperties = new()
    {
        nameof(ShowMetrics), nameof(ShowTrend), nameof(ShowExpiring), nameof(ShowRecent), nameof(ShowHealth), nameof(ShowShortcuts),
    };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is null || !WidgetProperties.Contains(e.PropertyName)) return;

        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowMiddleRow)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowBottomRow)));
        if (_settingsLoaded)
        {
            _ = DashboardSettingsStore.Shared.SaveAsync(new DashboardSettings
            {
                ShowMetrics = ShowMetrics, ShowTrend = ShowTrend, ShowExpiring = ShowExpiring,
                ShowRecent = ShowRecent, ShowHealth = ShowHealth, ShowShortcuts = ShowShortcuts,
            });
        }
    }

    [RelayCommand]
    private void ShowAllWidgets()
    {
        ShowMetrics = ShowTrend = ShowExpiring = ShowRecent = ShowHealth = ShowShortcuts = true;
    }

    // ---------------------------------------------------------------- navigation ----

    public override void OnNavigatedTo(object? parameter)
    {
        _autoRefresh.Start();
        _ = InitializeAsync();
    }

    public override void OnNavigatedFrom() => _autoRefresh.Stop();

    private async Task InitializeAsync()
    {
        if (!_settingsLoaded)
        {
            var saved = await DashboardSettingsStore.Shared.LoadAsync();
            ShowMetrics = saved.ShowMetrics;
            ShowTrend = saved.ShowTrend;
            ShowExpiring = saved.ShowExpiring;
            ShowRecent = saved.ShowRecent;
            ShowHealth = saved.ShowHealth;
            ShowShortcuts = saved.ShowShortcuts;
            _settingsLoaded = true;
        }
        await Refresh();
    }

    [RelayCommand] private void GoHome() => _navigation.NavigateToRoot<WelcomeViewModel>();
    [RelayCommand] private void NewCard() => _navigation.NavigateTo<BlankCardViewModel>();
    [RelayCommand] private void OpenTemplates() => _navigation.NavigateTo<TemplatesViewModel>();
    [RelayCommand] private void OpenRecentDesigns() => _navigation.NavigateTo<RecentDesignsViewModel>();
    [RelayCommand] private void OpenPrintHistory() => _navigation.NavigateTo<PrintHistoryViewModel>();
    [RelayCommand] private void OpenReports() => _navigation.NavigateTo<InfoID.Desktop.Features.Reports.ViewModels.ReportsViewModel>();
    [RelayCommand] private void OpenExpiringReport() => _navigation.NavigateTo<InfoID.Desktop.Features.Reports.ViewModels.ReportsViewModel>(InfoID.Application.Reporting.ReportKind.ExpiringCards);
    [RelayCommand] private void OpenAuditLog() => _navigation.NavigateTo<InfoID.Desktop.Features.Audit.ViewModels.AuditLogViewModel>();
    [RelayCommand] private void OpenBackup() => _navigation.NavigateTo<InfoID.Desktop.Features.Backup.ViewModels.BackupViewModel>();
    [RelayCommand] private void OpenLicense() => _navigation.NavigateTo<InfoID.Desktop.Features.Account.ViewModels.AccountLicenseViewModel>();

    /// <summary>Recent activity row click: opens that run in Print history.</summary>
    [RelayCommand]
    private void OpenJob(PrintJobRow? job)
    {
        if (job is not null) _navigation.NavigateTo<PrintHistoryViewModel>(job.Id);
    }

    // ---------------------------------------------------------------- loading ----

    [RelayCommand]
    private async Task Refresh()
    {
        if (_isLoading) return;
        _isLoading = true;
        IsBusy = true;
        ErrorMessage = null;
        var health = new List<HealthItem>();
        try
        {
            var databaseOk = await LoadPrintMetricsAsync();
            health.Add(BuildDatabaseHealth(databaseOk));
            health.Add(await BuildPrinterHealthAsync());
            health.Add(await LoadLicenseAsync());
            health.Add(BuildStorageHealth());
            health.Add(BuildBackupHealth());

            Health.Clear();
            foreach (var item in health) Health.Add(item);

            LastUpdatedText = $"Updated {DateTime.Now:t}";
            OnPropertyChanged(nameof(Greeting));
            OnPropertyChanged(nameof(TodayText));
        }
        finally
        {
            IsBusy = false;
            _isLoading = false;
        }
    }

    /// <summary>Cards, reprints, expiring cards, trend and recent runs. Returns false
    /// (and shows the reason) if the database couldn't be read.</summary>
    private async Task<bool> LoadPrintMetricsAsync()
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var query = new DashboardQuery
        {
            TodayLocalDate = today,
            TodayStartUtc = today.ToUniversalTime(),
            MonthStartUtc = monthStart.ToUniversalTime(),
            UtcOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now),
            ExpiringWithinDays = ExpiringWithinDays,
            TrendDays = TrendDays,
            MaxExpiringRows = 8,
        };

        try
        {
            DashboardSummaryDto summary;
            IReadOnlyList<PrintJobSummaryDto> recent;
            using (var scope = _scopes.CreateScope())
            {
                summary = await scope.ServiceProvider.GetRequiredService<IDashboardService>().GetSummaryAsync(query);
                recent = await scope.ServiceProvider.GetRequiredService<IPrintHistoryService>()
                    .GetJobsAsync(new PrintJobFilter { MaxResults = 8 });
            }

            CardsToday = summary.CardsToday;
            CardsTodayDetail = summary.RunsToday == 0 ? "No print runs yet today" : Plural(summary.RunsToday, "print run");
            CardsThisMonth = summary.CardsThisMonth;
            CardsThisMonthDetail = $"{Plural(summary.RunsThisMonth, "print run")} in {today:MMMM}";
            ReprintsThisMonth = summary.ReprintsThisMonth;
            FailedRunsThisMonth = summary.FailedRunsThisMonth;
            ExpiringSoonCount = summary.ExpiringSoonCount;
            ExpiredCount = summary.ExpiredCount;
            PeopleWithCards = summary.PeopleWithCards;

            // Trend bars, scaled to the busiest day (a quiet fortnight still shows bars).
            var max = Math.Max(1, summary.DailyCards.Count == 0 ? 1 : summary.DailyCards.Max(d => d.Cards));
            DailyBars.Clear();
            foreach (var day in summary.DailyCards)
            {
                var isToday = day.Date == today;
                DailyBars.Add(new DailyBar(
                    isToday ? "Today" : day.Date.ToString("dd"),
                    $"{day.Date:ddd d MMM}: {Plural(day.Cards, "card")}",
                    day.Cards,
                    day.Cards == 0 ? 2 : Math.Max(6, day.Cards / (double)max * MaxBarHeight),
                    isToday));
            }
            var trendTotal = summary.DailyCards.Sum(d => d.Cards);
            TrendSummary = trendTotal == 0
                ? $"No cards printed in the last {TrendDays} days"
                : $"{Plural(trendTotal, "card")} in the last {TrendDays} days · busiest day {max:N0}";

            ExpiringCards.Clear();
            foreach (var card in summary.ExpiringCards) ExpiringCards.Add(new ExpiringRow(card));
            HasExpiring = ExpiringCards.Count > 0;
            ExpiringMoreText = summary.ExpiringSoonCount > ExpiringCards.Count
                ? $"and {summary.ExpiringSoonCount - ExpiringCards.Count:N0} more"
                : null;

            RecentJobs.Clear();
            foreach (var job in recent) RecentJobs.Add(new PrintJobRow(job));
            HasRecent = RecentJobs.Count > 0;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Dashboard couldn't read card data", ex);
            ErrorMessage = "Couldn't read card data: " + ex.GetBaseException().Message;
            return false;
        }
    }

    private async Task<HealthItem> LoadLicenseAsync()
    {
        try
        {
            LicenseStatusDto status;
            using (var scope = _scopes.CreateScope())
            {
                status = await scope.ServiceProvider.GetRequiredService<ILicenseService>().GetCurrentStatusAsync();
            }

            HasCardLimit = status.IsValid && status.MaxCardsPerPeriod is > 0;
            if (HasCardLimit)
            {
                var max = status.MaxCardsPerPeriod!.Value;
                LicenseUsagePercent = Math.Clamp(status.CardsIssuedThisPeriod * 100.0 / max, 0, 100);
                LicenseUsageText = $"{status.CardsIssuedThisPeriod:N0} of {max:N0} cards this {(status.CardLimitPeriod == "Yearly" ? "year" : "month")}";
            }
            else
            {
                LicenseUsagePercent = 0;
                LicenseUsageText = status.IsValid ? $"{status.CardsIssuedThisPeriod:N0} cards this period · no limit" : "";
            }

            var tier = status.Tier?.ToString() ?? "";
            (string Title, string Detail, HealthState State) view = status switch
            {
                { HasLicense: false } => ("Not activated", "Activate InfoID to print cards", HealthState.Error),
                { IsValid: false } => ("Not valid", status.Problem ?? "The license isn't valid on this computer", HealthState.Error),
                { IsInGracePeriod: true } => ($"{tier} · expired",
                    $"Grace period: {status.GracePeriodDaysRemaining ?? 0} day(s) left before printing stops", HealthState.Warning),
                { ExpiryDate: null } => ($"{tier} · perpetual", "Never expires", HealthState.Ok),
                { DaysUntilExpiry: { } days } when days <= 30 => ($"{tier} · {days} day(s) left",
                    $"Expires {status.ExpiryDate!.Value.ToLocalTime():d}. Renew soon.", HealthState.Warning),
                { DaysUntilExpiry: { } days } => ($"{tier} · {days} days left",
                    $"Expires {status.ExpiryDate!.Value.ToLocalTime():d}", HealthState.Ok),
                _ => (tier, "", HealthState.Ok),
            };

            // A nearly used-up card allowance is worth flagging too.
            if (view.State == HealthState.Ok && HasCardLimit && LicenseUsagePercent >= 90)
                view = (view.Title, $"{LicenseUsageText}: almost used up", HealthState.Warning);

            LicenseTitle = view.Title;
            LicenseDetail = view.Detail;
            LicenseNeedsAttention = view.State != HealthState.Ok;
            return new HealthItem("License", $"{view.Title}. {view.Detail}".Trim().TrimEnd('.') , view.State);
        }
        catch (Exception ex)
        {
            LicenseTitle = "Unknown";
            LicenseDetail = "The license couldn't be read";
            LicenseNeedsAttention = true;
            return new HealthItem("License", "Couldn't read the license: " + ex.GetBaseException().Message, HealthState.Error);
        }
    }

    private async Task<HealthItem> BuildPrinterHealthAsync()
    {
        if (!_printerService.IsSupported)
            return new HealthItem("Printer", "Printing isn't available on this system", HealthState.Error);

        try
        {
            var printers = await _printerService.GetPrintersAsync();
            if (printers.Count == 0)
                return new HealthItem("Printer", "No printers are installed", HealthState.Error);

            // The printer InfoID last printed with (remembered by the Print window),
            // otherwise the system default.
            var remembered = (await PrintSettingsStore.Shared.LoadAsync()).PrinterName;
            var printer = printers.FirstOrDefault(p => string.Equals(p.Name, remembered, StringComparison.OrdinalIgnoreCase))
                          ?? printers.FirstOrDefault(p => p.IsDefault)
                          ?? printers[0];

            if (remembered is not null && !string.Equals(printer.Name, remembered, StringComparison.OrdinalIgnoreCase))
                return new HealthItem("Printer", $"\"{remembered}\" isn't connected. Default is {printer.Name}", HealthState.Warning);
            if (PrintDialogViewModel.IsVirtualPrinter(printer.Name))
                return new HealthItem("Printer", $"{printer.Name} saves files; no card printer selected", HealthState.Warning);
            return printer.IsOnline
                ? new HealthItem("Printer", $"{printer.Name} · ready", HealthState.Ok)
                : new HealthItem("Printer", $"{printer.Name} · offline", HealthState.Warning);
        }
        catch (Exception ex)
        {
            return new HealthItem("Printer", "Couldn't check printers: " + ex.GetBaseException().Message, HealthState.Warning);
        }
    }

    private static HealthItem BuildDatabaseHealth(bool readOk)
    {
        if (!readOk) return new HealthItem("Database", "Couldn't read the local database", HealthState.Error);
        try
        {
            var path = DatabaseLocation.GetDefaultDatabasePath();
            var size = File.Exists(path) ? new FileInfo(path).Length : 0;
            return new HealthItem("Database", $"Connected · {FormatBytes(size)}", HealthState.Ok);
        }
        catch
        {
            return new HealthItem("Database", "Connected", HealthState.Ok);
        }
    }

    private readonly InfoID.Desktop.Features.Backup.Services.BackupSettingsStore _backupSettings;

    /// <summary>Backup (FR-ENT-4): warns when there has never been a backup, the last one
    /// is old, or the last attempt failed.</summary>
    private HealthItem BuildBackupHealth()
    {
        try
        {
            var s = _backupSettings.Current;
            if (s.LastFailureUtc is { } failed && (s.LastBackupUtc is null || failed > s.LastBackupUtc))
                return new HealthItem("Backup", "The last backup failed: " + s.LastFailureMessage, HealthState.Error);
            if (s.LastBackupUtc is not { } last)
                return new HealthItem("Backup", s.AutoBackupEnabled
                    ? "No backup yet. The first automatic backup runs shortly"
                    : "No backup yet. Back up from File > Backup & Restore", HealthState.Warning);

            var age = DateTime.UtcNow - last;
            var text = InfoID.Desktop.Features.Backup.ViewModels.BackupViewModel.DescribeAge(last.ToLocalTime());
            var limit = TimeSpan.FromDays(Math.Max(7, s.IntervalDays * 2));
            if (age > limit)
                return new HealthItem("Backup", $"Last backup {text}. Back up soon", HealthState.Warning);
            return new HealthItem("Backup", s.AutoBackupEnabled ? $"Last backup {text} · automatic" : $"Last backup {text} · automatic backups off",
                s.AutoBackupEnabled ? HealthState.Ok : HealthState.Warning);
        }
        catch (Exception ex)
        {
            return new HealthItem("Backup", "Couldn't read the backup status: " + ex.GetBaseException().Message, HealthState.Warning);
        }
    }

    private static HealthItem BuildStorageHealth()
    {
        try
        {
            var root = Path.GetPathRoot(DatabaseLocation.GetApplicationRoot());
            if (string.IsNullOrEmpty(root)) return new HealthItem("Storage", "Unknown", HealthState.Warning);
            var drive = new DriveInfo(root);
            var free = drive.AvailableFreeSpace;
            var state = free < 200L * 1024 * 1024 ? HealthState.Error
                : free < 1024L * 1024 * 1024 ? HealthState.Warning
                : HealthState.Ok;
            return new HealthItem("Storage", $"{FormatBytes(free)} free on {drive.Name.TrimEnd('\\', '/')}", state);
        }
        catch (Exception ex)
        {
            return new HealthItem("Storage", "Couldn't check free space: " + ex.GetBaseException().Message, HealthState.Warning);
        }
    }

    private static string Plural(int n, string word) => $"{n:N0} {word}{(n == 1 ? "" : "s")}";

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };
}
