using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Interfaces;
using InfoID.Application.Reporting;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.Reports.Services;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using InfoID.Domain.Common.Interfaces;
using InfoID.Domain.Entities.ApprovalAuditModule;
using InfoID.Infrastructure.Auditing;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Audit.ViewModels;

public enum AuditPeriod { Today, Last7Days, Last30Days, ThisMonth, AllDates, Custom }

public sealed record AuditPeriodOption(AuditPeriod Period, string Label);

/// <summary>An area filter entry; Module null = all areas.</summary>
public sealed record AuditModuleOption(string? Module, string Label);

public enum IntegrityState { Unknown, Checking, Intact, Warning, Broken }

/// <summary>One audit entry in the list.</summary>
public sealed class AuditRow
{
    public AuditRow(AuditLog entry)
    {
        Entry = entry;
        Local = DateTime.SpecifyKind(entry.ActionDate, DateTimeKind.Utc).ToLocalTime();
        Changes = AuditFormatting.Changes(entry.BeforeValueJson, entry.AfterValueJson);
    }

    public AuditLog Entry { get; }
    public DateTime Local { get; }
    public long Id => Entry.Id;
    public string When => Local.ToString("dd MMM yyyy HH:mm:ss");
    public string WhenShort => Local.Date == DateTime.Today ? Local.ToString("HH:mm:ss") : Local.ToString("dd MMM, HH:mm");
    public string Module => Entry.Module;
    public string Description => Entry.Description ?? Entry.Action;
    public string User => Entry.CreatedBy ?? "";
    public string Machine => Entry.MachineName ?? "";
    public string Action => Entry.Action;
    public string Item => Entry.EntityName is null ? "" : Entry.EntityId is { } id ? $"{Entry.EntityName} #{id}" : Entry.EntityName;
    public IReadOnlyList<AuditChange> Changes { get; }
    public bool HasChanges => Changes.Count > 0;
    public bool HasBeforeValues => Changes.Any(c => c.Before.Length > 0);
    public MaterialIconKind Icon => Entry.Module switch
    {
        AuditModules.Application => MaterialIconKind.Power,
        AuditModules.Account => MaterialIconKind.AccountCircleOutline,
        AuditModules.Designs => MaterialIconKind.PaletteOutline,
        AuditModules.Printing => MaterialIconKind.Printer,
        AuditModules.Data => MaterialIconKind.DatabaseOutline,
        AuditModules.License => MaterialIconKind.ShieldKeyOutline,
        AuditModules.Backup => MaterialIconKind.BackupRestore,
        AuditModules.Reports => MaterialIconKind.FileChartOutline,
        AuditModules.Visitors => MaterialIconKind.BadgeAccountHorizontalOutline,
        AuditModules.Audit => MaterialIconKind.ShieldCheckOutline,
        _ => MaterialIconKind.InformationOutline,
    };
}

/// <summary>
/// Audit log page (FR-REP-4, FR-REP-7): browse who did what, with before/after values,
/// check the hash chain ("Verify integrity"), and export the filtered log to Excel, PDF
/// or CSV. Singleton so filters are kept between visits.
/// </summary>
public sealed partial class AuditLogViewModel : ViewModelBase
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<AuditLogViewModel>();
    private const int PageSize = 300;

    private readonly INavigationService _navigation;
    private readonly IServiceScopeFactory _scopes;
    private readonly IFilePickerService _filePicker;
    private readonly DispatcherTimer _searchDelay;
    private bool _suppressReload;
    private int _loadVersion;
    private bool _verifiedThisSession;

    public AuditLogViewModel(INavigationService navigation, IServiceScopeFactory scopes, IFilePickerService filePicker)
    {
        _navigation = navigation;
        _scopes = scopes;
        _filePicker = filePicker;
        _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); _ = ReloadAsync(); };
#pragma warning disable MVVMTK0034 // defaults without triggering a reload from the constructor
        _selectedPeriod = Periods.First(p => p.Period == AuditPeriod.Last30Days);
        _selectedModule = Modules[0];
#pragma warning restore MVVMTK0034
        ApplyPeriod(AuditPeriod.Last30Days);
    }

    public override void OnNavigatedTo(object? parameter)
    {
        RefreshRollingPeriod();
        _ = ReloadAsync();
        if (!_verifiedThisSession) _ = VerifyAsync(recordInAudit: false);
    }

    [RelayCommand]
    private void GoHome() => _navigation.NavigateToRoot<WelcomeViewModel>();

    // ---------------------------------------------------------------- filters ----

    public IReadOnlyList<AuditPeriodOption> Periods { get; } = new[]
    {
        new AuditPeriodOption(AuditPeriod.Today, "Today"),
        new AuditPeriodOption(AuditPeriod.Last7Days, "Last 7 days"),
        new AuditPeriodOption(AuditPeriod.Last30Days, "Last 30 days"),
        new AuditPeriodOption(AuditPeriod.ThisMonth, "This month"),
        new AuditPeriodOption(AuditPeriod.AllDates, "All dates"),
        new AuditPeriodOption(AuditPeriod.Custom, "Custom dates"),
    };

    public IReadOnlyList<AuditModuleOption> Modules { get; } =
        new[] { new AuditModuleOption(null, "All areas") }
            .Concat(AuditModules.All.Select(m => new AuditModuleOption(m, m)))
            .ToList();

    [ObservableProperty] private AuditPeriodOption _selectedPeriod;
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;
    [ObservableProperty] private AuditModuleOption _selectedModule;
    [ObservableProperty] private string _searchText = string.Empty;

    partial void OnSelectedPeriodChanged(AuditPeriodOption value)
    {
        if (value.Period != AuditPeriod.Custom) ApplyPeriod(value.Period);
        if (!_suppressReload) _ = ReloadAsync();
    }

    partial void OnFromDateChanged(DateTime? value) => DateEdited();
    partial void OnToDateChanged(DateTime? value) => DateEdited();
    partial void OnSelectedModuleChanged(AuditModuleOption value) { if (!_suppressReload) _ = ReloadAsync(); }

    partial void OnSearchTextChanged(string value)
    {
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    private void DateEdited()
    {
        if (_suppressReload) return;
        if (SelectedPeriod.Period != AuditPeriod.Custom)
        {
            _suppressReload = true;
            SelectedPeriod = Periods.First(p => p.Period == AuditPeriod.Custom);
            _suppressReload = false;
        }
        _ = ReloadAsync();
    }

    private void ApplyPeriod(AuditPeriod period)
    {
        var today = DateTime.Today;
        var (from, to) = period switch
        {
            AuditPeriod.Today => ((DateTime?)today, (DateTime?)today),
            AuditPeriod.Last7Days => (today.AddDays(-6), today),
            AuditPeriod.Last30Days => (today.AddDays(-29), today),
            AuditPeriod.ThisMonth => (new DateTime(today.Year, today.Month, 1), today),
            _ => (null, null),
        };
        var previous = _suppressReload;
        _suppressReload = true;
        FromDate = from;
        ToDate = to;
        _suppressReload = previous;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressReload = true;
        SelectedPeriod = Periods.First(p => p.Period == AuditPeriod.AllDates);
        ApplyPeriod(AuditPeriod.AllDates);
        SelectedModule = Modules[0];
        _suppressReload = false;
        SearchText = string.Empty;
        _searchDelay.Stop();
        _ = ReloadAsync();
    }

    // ----------------------------------------------------------------- list ----

    public ObservableCollection<AuditRow> Entries { get; } = new();
    [ObservableProperty] private AuditRow? _selectedEntry;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private string _countText = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;

    public bool HasSelection => SelectedEntry is not null;
    public bool IsEmpty => !IsLoading && Entries.Count == 0;
    partial void OnSelectedEntryChanged(AuditRow? value) => OnPropertyChanged(nameof(HasSelection));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    private AuditQuery BuildQuery(long? beforeId) => new()
    {
        FromUtc = FromDate?.Date is { } f ? f.ToUniversalTime() : null,
        ToUtc = ToDate?.Date is { } t ? t.AddDays(1).ToUniversalTime() : null,
        Module = SelectedModule.Module,
        Search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
        BeforeId = beforeId,
        Take = PageSize,
    };

    [RelayCommand]
    private Task Refresh()
    {
        RefreshRollingPeriod();
        return ReloadAsync();
    }

    /// <summary>"Today", "Last 7 days" etc. are relative to the current date: recompute
    /// them on Refresh and when the page is shown, so a page left open overnight (or
    /// opened again the next day) doesn't keep yesterday's date range.</summary>
    private void RefreshRollingPeriod()
    {
        if (SelectedPeriod.Period is AuditPeriod.Custom or AuditPeriod.AllDates) return;
        ApplyPeriod(SelectedPeriod.Period);
    }

    private async Task ReloadAsync(long? selectId = null)
    {
        if (FromDate is { } f && ToDate is { } t && f.Date > t.Date)
        {
            ShowMessage("The 'From' date is after the 'To' date.", isError: true);
            return;
        }
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            using var scope = _scopes.CreateScope();
            var rows = await scope.ServiceProvider.GetRequiredService<IAuditLogReader>().QueryAsync(BuildQuery(selectId is { } id ? id + 1 : null));
            if (version != _loadVersion) return;

            Entries.Clear();
            foreach (var row in rows) Entries.Add(new AuditRow(row));
            HasMore = rows.Count == PageSize;
            UpdateCount();
            SelectedEntry = selectId is { } wanted ? Entries.FirstOrDefault(e => e.Id == wanted) : Entries.FirstOrDefault();
            if (Message is not null && !MessageIsError) Message = null;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't load the audit log", ex);
            ShowMessage("Couldn't load the audit log: " + ex.GetBaseException().Message, isError: true);
        }
        finally
        {
            if (version == _loadVersion) IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        if (!HasMore || Entries.Count == 0 || IsLoading) return;
        IsLoading = true;
        try
        {
            using var scope = _scopes.CreateScope();
            var rows = await scope.ServiceProvider.GetRequiredService<IAuditLogReader>().QueryAsync(BuildQuery(Entries[^1].Id));
            foreach (var row in rows) Entries.Add(new AuditRow(row));
            HasMore = rows.Count == PageSize;
            UpdateCount();
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't load more audit entries", ex);
            ShowMessage("Couldn't load more entries: " + ex.GetBaseException().Message, isError: true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateCount() =>
        CountText = Entries.Count == 0 ? "No entries" : $"{Entries.Count:N0} entr{(Entries.Count == 1 ? "y" : "ies")}{(HasMore ? " shown (newest first)" : "")}";

    // ------------------------------------------------------------ integrity ----

    [ObservableProperty] private IntegrityState _integrity = IntegrityState.Unknown;
    [ObservableProperty] private string _integrityText = "Not checked yet.";
    [ObservableProperty] private long? _problemEntryId;

    public bool IntegrityOk => Integrity == IntegrityState.Intact;
    public bool IntegrityWarning => Integrity is IntegrityState.Warning or IntegrityState.Unknown;
    public bool IntegrityBroken => Integrity == IntegrityState.Broken;
    public bool IsVerifying => Integrity == IntegrityState.Checking;
    public bool HasProblemEntry => ProblemEntryId is not null;

    partial void OnIntegrityChanged(IntegrityState value)
    {
        OnPropertyChanged(nameof(IntegrityOk));
        OnPropertyChanged(nameof(IntegrityWarning));
        OnPropertyChanged(nameof(IntegrityBroken));
        OnPropertyChanged(nameof(IsVerifying));
    }

    partial void OnProblemEntryIdChanged(long? value) => OnPropertyChanged(nameof(HasProblemEntry));

    [RelayCommand]
    private Task VerifyNow() => VerifyAsync(recordInAudit: true);

    private async Task VerifyAsync(bool recordInAudit)
    {
        if (Integrity == IntegrityState.Checking) return;
        Integrity = IntegrityState.Checking;
        IntegrityText = "Checking every entry...";
        try
        {
            AuditVerification result;
            using (var scope = _scopes.CreateScope())
                result = await scope.ServiceProvider.GetRequiredService<IAuditLogReader>().VerifyAsync();

            _verifiedThisSession = true;
            ProblemEntryId = result.ProblemEntryId;
            Integrity = !result.Intact ? IntegrityState.Broken : result.AnchorMissing ? IntegrityState.Warning : IntegrityState.Intact;
            IntegrityText = $"{result.Message} (checked {DateTime.Now:HH:mm})";
            if (!result.Intact) Log.Warn($"Audit log integrity check failed: {result.Message}");

            // A manual check, or any detected problem, is itself recorded.
            if (recordInAudit || !result.Intact)
                InfoID.Desktop.Core.Services.Audit.Record(AuditModules.Audit, result.Intact ? "IntegrityVerified" : "IntegrityProblem",
                    result.Intact ? $"Audit log integrity verified ({result.EntriesChecked:N0} entries)" : $"Audit log integrity problem: {result.Message}",
                    after: new { result.Intact, result.EntriesChecked, result.ProblemEntryId, result.Message });
        }
        catch (Exception ex)
        {
            Log.Error("Audit integrity check failed to run", ex);
            Integrity = IntegrityState.Warning;
            IntegrityText = "The check couldn't run: " + ex.GetBaseException().Message;
        }
    }

    /// <summary>Shows the entry where the problem was found.</summary>
    [RelayCommand]
    private async Task ShowProblemEntry()
    {
        if (ProblemEntryId is not { } id) return;
        _suppressReload = true;
        SelectedPeriod = Periods.First(p => p.Period == AuditPeriod.AllDates);
        ApplyPeriod(AuditPeriod.AllDates);
        SelectedModule = Modules[0];
        SearchText = string.Empty;
        _suppressReload = false;
        _searchDelay.Stop();
        await ReloadAsync(id);
    }

    // ---------------------------------------------------------------- export ----

    [RelayCommand] private Task ExportExcel() => ExportAsync("Excel workbook", "xlsx", XlsxReportWriter.Write);
    [RelayCommand] private Task ExportPdf() => ExportAsync("PDF document", "pdf", ReportPdfWriter.Write);
    [RelayCommand] private Task ExportCsv() => ExportAsync("CSV (comma separated)", "csv", CsvReportWriter.Write);

    private async Task ExportAsync(string kindName, string extension, Action<ReportTable, Stream> write)
    {
        var suggested = $"Audit log {DateTime.Now:yyyy-MM-dd}.{extension}";
        var path = await _filePicker.PickSaveFileAsync("Export audit log", kindName, new[] { "*." + extension }, suggested, extension);
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!path.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase)) path += "." + extension;

        IsLoading = true;
        try
        {
            ReportTable table;
            using (var scope = _scopes.CreateScope())
            {
                table = await scope.ServiceProvider.GetRequiredService<IReportService>().GenerateAsync(new ReportRequest
                {
                    Kind = ReportKind.AuditLog,
                    FromDate = FromDate,
                    ToDate = ToDate,
                    UtcOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now),
                    TodayLocalDate = DateTime.Today,
                    AuditModule = SelectedModule.Module,
                    SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                    AuditIntegrityText = Integrity is IntegrityState.Unknown or IntegrityState.Checking ? null : IntegrityText,
                });
            }

            var temp = path + ".tmp";
            await Task.Run(() =>
            {
                using (var stream = File.Create(temp)) write(table, stream);
                File.Move(temp, path, overwrite: true);
            });
            ShowMessage($"Saved {Path.GetFileName(path)} ({table.Rows.Count:N0} entries).");
            InfoID.Desktop.Core.Services.Audit.Record(AuditModules.Audit, "AuditExported",
                $"Audit log exported to {Path.GetFileName(path)} ({table.Rows.Count:N0} entries)",
                after: new { Filters = table.Subtitle, Format = extension.ToUpperInvariant(), Entries = table.Rows.Count, File = path });
            LastExportPath = path;
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020))
        {
            ShowMessage("The file is open in another program (for example Excel). Close it and export again.", isError: true);
        }
        catch (Exception ex)
        {
            Log.Error($"Audit export failed ({extension})", ex);
            ShowMessage("Export failed: " + ex.GetBaseException().Message, isError: true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [ObservableProperty] private string? _lastExportPath;
    public bool HasLastExport => LastExportPath is not null;
    partial void OnLastExportPathChanged(string? value) => OnPropertyChanged(nameof(HasLastExport));

    [RelayCommand]
    private void OpenLastExport()
    {
        if (LastExportPath is null || !File.Exists(LastExportPath)) return;
        try
        {
            Process.Start(new ProcessStartInfo(LastExportPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowMessage("Couldn't open the file: " + ex.GetBaseException().Message, isError: true);
        }
    }

    private void ShowMessage(string? message, bool isError = false)
    {
        Message = message;
        MessageIsError = isError;
    }
}
