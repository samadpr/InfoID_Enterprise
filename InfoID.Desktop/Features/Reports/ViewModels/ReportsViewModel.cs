using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Interfaces;
using InfoID.Application.Reporting;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.Reports.Services;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Reports.ViewModels;

/// <summary>A report in the list on the left.</summary>
public sealed record ReportOption(ReportKind Kind, string Title, string Description, Material.Icons.MaterialIconKind Icon);

public enum ReportPeriod { Today, Yesterday, ThisWeek, Last7Days, ThisMonth, LastMonth, Last30Days, ThisYear, AllDates, Custom }

public sealed record PeriodOption(ReportPeriod Period, string Label);
public sealed record DesignOption(string? Name, string Label);
public sealed record PrintLogFilterOption(PrintLogFilter Filter, string Label);
public sealed record LeadTimeOption(int Days, string Label);
public sealed record SummaryItem(string Label, string Value);

/// <summary>One grid row: the report's cells formatted for display.</summary>
public sealed class ReportGridRow
{
    public ReportGridRow(string[] cells) => Cells = cells;
    public string[] Cells { get; }
}

/// <summary>
/// Reports page (SRS 4.11): Card issuance (FR-REP-1), Print log with failures and reprints
/// (FR-REP-2), Expiring cards and Replacement cards (FR-REP-3), each with period / design
/// filters, an on-screen preview and export to Excel, PDF and CSV (FR-REP-5).
/// The report re-runs automatically whenever a filter changes.
/// </summary>
public sealed partial class ReportsViewModel : ViewModelBase
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<ReportsViewModel>();

    private readonly INavigationService _navigation;
    private readonly IServiceScopeFactory _scopes;
    private readonly IFilePickerService _filePicker;
    private bool _initialized;
    private bool _suppressRun;
    private int _runVersion;
    private string? _organizationName;

    public ReportsViewModel(INavigationService navigation, IServiceScopeFactory scopes, IFilePickerService filePicker)
    {
        _navigation = navigation;
        _scopes = scopes;
        _filePicker = filePicker;
#pragma warning disable MVVMTK0034 // defaults set without triggering a run from the constructor
        _selectedReport = Reports[0];
        _selectedPeriod = Periods.First(p => p.Period == ReportPeriod.ThisMonth);
        _selectedDesign = Designs[0];
        _selectedPrintLogFilter = PrintLogFilters[0];
        _selectedLeadTime = LeadTimes.First(l => l.Days == 30);
#pragma warning restore MVVMTK0034
        ApplyPeriod(ReportPeriod.ThisMonth);
    }

    // ---------------------------------------------------------------- choices ----

    public IReadOnlyList<ReportOption> Reports { get; } = new[]
    {
        new ReportOption(ReportKind.CardIssuance, "Card issuance", "Every card issued in a period, new and reprinted", Material.Icons.MaterialIconKind.CardAccountDetailsOutline),
        new ReportOption(ReportKind.PrintLog, "Print log", "Every print run, with failures and reprints", Material.Icons.MaterialIconKind.Printer),
        new ReportOption(ReportKind.ExpiringCards, "Expiring cards", "Cards in use that expire soon or already have", Material.Icons.MaterialIconKind.CalendarAlert),
        new ReportOption(ReportKind.ReplacementCards, "Replacement cards", "Reprints with reason: lost, damaged, renewed", Material.Icons.MaterialIconKind.Replay),
        new ReportOption(ReportKind.AuditLog, "Audit log", "Who did what and when, with before and after values", Material.Icons.MaterialIconKind.ShieldCheckOutline),
    };

    public IReadOnlyList<PeriodOption> Periods { get; } = new[]
    {
        new PeriodOption(ReportPeriod.Today, "Today"),
        new PeriodOption(ReportPeriod.Yesterday, "Yesterday"),
        new PeriodOption(ReportPeriod.ThisWeek, "This week"),
        new PeriodOption(ReportPeriod.Last7Days, "Last 7 days"),
        new PeriodOption(ReportPeriod.ThisMonth, "This month"),
        new PeriodOption(ReportPeriod.LastMonth, "Last month"),
        new PeriodOption(ReportPeriod.Last30Days, "Last 30 days"),
        new PeriodOption(ReportPeriod.ThisYear, "This year"),
        new PeriodOption(ReportPeriod.AllDates, "All dates"),
        new PeriodOption(ReportPeriod.Custom, "Custom dates"),
    };

    public ObservableCollection<DesignOption> Designs { get; } = new() { new DesignOption(null, "All designs") };

    public IReadOnlyList<PrintLogFilterOption> PrintLogFilters { get; } = new[]
    {
        new PrintLogFilterOption(PrintLogFilter.All, "All print runs"),
        new PrintLogFilterOption(PrintLogFilter.FailedOnly, "Failed runs only"),
        new PrintLogFilterOption(PrintLogFilter.ReprintsOnly, "Runs with reprints only"),
    };

    public IReadOnlyList<LeadTimeOption> LeadTimes { get; } = new[]
    {
        new LeadTimeOption(7, "Next 7 days"),
        new LeadTimeOption(14, "Next 14 days"),
        new LeadTimeOption(30, "Next 30 days"),
        new LeadTimeOption(60, "Next 60 days"),
        new LeadTimeOption(90, "Next 90 days"),
        new LeadTimeOption(365, "Next 12 months"),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesPeriod), nameof(IsPrintLog), nameof(IsExpiring), nameof(UsesDesign))]
    private ReportOption _selectedReport;

    [ObservableProperty] private PeriodOption _selectedPeriod;
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;
    [ObservableProperty] private DesignOption _selectedDesign;
    [ObservableProperty] private PrintLogFilterOption _selectedPrintLogFilter;
    [ObservableProperty] private LeadTimeOption _selectedLeadTime;
    [ObservableProperty] private bool _includeExpired = true;

    /// <summary>The expiring report is "as of today"; the others use the period.</summary>
    public bool UsesPeriod => SelectedReport.Kind != ReportKind.ExpiringCards;
    public bool IsPrintLog => SelectedReport.Kind == ReportKind.PrintLog;
    public bool IsExpiring => SelectedReport.Kind == ReportKind.ExpiringCards;
    /// <summary>The audit log isn't about one design.</summary>
    public bool UsesDesign => SelectedReport.Kind != ReportKind.AuditLog;

    // ---------------------------------------------------------------- result ----

    /// <summary>The last generated report; the View rebuilds the grid columns when it changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport), nameof(HasRows), nameof(IsEmpty), nameof(ReportTitle), nameof(ReportSubtitle))]
    private ReportTable? _table;

    public ObservableCollection<ReportGridRow> Rows { get; } = new();
    public ObservableCollection<SummaryItem> Summary { get; } = new();

    public bool HasReport => Table is not null;
    public bool HasRows => Table is { Rows.Count: > 0 };
    public bool IsEmpty => Table is { Rows.Count: 0 };
    public string ReportTitle => Table?.Title ?? SelectedReport.Title;
    public string ReportSubtitle => Table?.Subtitle ?? "";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;

    /// <summary>The last exported file, for "Open file" / "Show in folder".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastExport))]
    private string? _lastExportPath;

    public bool HasLastExport => LastExportPath is not null;

    // ---------------------------------------------------------------- navigation ----

    /// <summary>A <see cref="ReportKind"/> parameter opens that report (e.g. from the Dashboard).</summary>
    public override void OnNavigatedTo(object? parameter)
    {
        if (parameter is ReportKind kind)
        {
            _suppressRun = true;
            SelectedReport = Reports.First(r => r.Kind == kind);
            _suppressRun = false;
        }
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _initialized = true;
        try
        {
            using var scope = _scopes.CreateScope();
            // Reloaded on every visit so designs printed since last time appear.
            var names = await scope.ServiceProvider.GetRequiredService<IReportService>().GetDesignNamesAsync();
            var selected = SelectedDesign.Name;
            _suppressRun = true;
            while (Designs.Count > 1) Designs.RemoveAt(Designs.Count - 1);
            foreach (var name in names) Designs.Add(new DesignOption(name, name));
            SelectedDesign = Designs.FirstOrDefault(d => string.Equals(d.Name, selected, StringComparison.OrdinalIgnoreCase)) ?? Designs[0];
            _suppressRun = false;

            if (_organizationName is null)
            {
                var license = await scope.ServiceProvider.GetRequiredService<ILicenseService>().GetCurrentStatusAsync();
                _organizationName = string.IsNullOrWhiteSpace(license.OrganizationName) ? null : license.OrganizationName;
            }
        }
        catch (Exception ex)
        {
            _suppressRun = false;
            Log.Warn("Reports page couldn't load the design list", ex);
        }
        await Run();
    }

    [RelayCommand] private void GoHome() => _navigation.NavigateToRoot<WelcomeViewModel>();

    // ---------------------------------------------------------------- filters ----

    partial void OnSelectedReportChanged(ReportOption value) => ScheduleRun();
    partial void OnSelectedDesignChanged(DesignOption value) => ScheduleRun();
    partial void OnSelectedPrintLogFilterChanged(PrintLogFilterOption value) => ScheduleRun();
    partial void OnSelectedLeadTimeChanged(LeadTimeOption value) => ScheduleRun();
    partial void OnIncludeExpiredChanged(bool value) => ScheduleRun();

    partial void OnSelectedPeriodChanged(PeriodOption value)
    {
        if (value.Period != ReportPeriod.Custom) ApplyPeriod(value.Period);
        ScheduleRun();
    }

    /// <summary>Typing/picking a date switches the period to "Custom dates".</summary>
    partial void OnFromDateChanged(DateTime? value) => OnDateEdited();
    partial void OnToDateChanged(DateTime? value) => OnDateEdited();

    private bool _applyingPeriod;

    private void OnDateEdited()
    {
        if (_applyingPeriod) return;
        if (SelectedPeriod.Period != ReportPeriod.Custom)
        {
            _suppressRun = true;
            SelectedPeriod = Periods.First(p => p.Period == ReportPeriod.Custom);
            _suppressRun = false;
        }
        ScheduleRun();
    }

    private void ApplyPeriod(ReportPeriod period)
    {
        var today = DateTime.Today;
        var mondayOffset = ((int)today.DayOfWeek + 6) % 7;
        (DateTime? From, DateTime? To) range = period switch
        {
            ReportPeriod.Today => (today, today),
            ReportPeriod.Yesterday => (today.AddDays(-1), today.AddDays(-1)),
            ReportPeriod.ThisWeek => (today.AddDays(-mondayOffset), today),
            ReportPeriod.Last7Days => (today.AddDays(-6), today),
            ReportPeriod.ThisMonth => (new DateTime(today.Year, today.Month, 1), today),
            ReportPeriod.LastMonth => (new DateTime(today.Year, today.Month, 1).AddMonths(-1), new DateTime(today.Year, today.Month, 1).AddDays(-1)),
            ReportPeriod.Last30Days => (today.AddDays(-29), today),
            ReportPeriod.ThisYear => (new DateTime(today.Year, 1, 1), today),
            ReportPeriod.AllDates => (null, null),
            _ => (FromDate, ToDate),
        };
        _applyingPeriod = true;
        FromDate = range.From;
        ToDate = range.To;
        _applyingPeriod = false;
    }

    /// <summary>Runs the report after a short pause, so several quick filter changes
    /// produce one run.</summary>
    private void ScheduleRun()
    {
        if (_suppressRun || !_initialized) return;
        var version = ++_runVersion;
        _ = Task.Delay(150).ContinueWith(_ =>
        {
            if (version == _runVersion) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = Run());
        }, TaskScheduler.Default);
    }

    // ---------------------------------------------------------------- run ----

    [RelayCommand]
    private async Task Run()
    {
        var version = ++_runVersion;
        if (UsesPeriod && FromDate is { } f && ToDate is { } t && f.Date > t.Date)
        {
            Message = "The start date is after the end date.";
            MessageIsError = true;
            return;
        }

        IsBusy = true;
        Message = null;
        try
        {
            var request = new ReportRequest
            {
                Kind = SelectedReport.Kind,
                FromDate = UsesPeriod ? FromDate : null,
                ToDate = UsesPeriod ? ToDate : null,
                UtcOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now),
                TodayLocalDate = DateTime.Today,
                DesignName = UsesDesign ? SelectedDesign.Name : null,
                PrintLogFilter = SelectedPrintLogFilter.Filter,
                ExpiringWithinDays = SelectedLeadTime.Days,
                IncludeExpired = IncludeExpired,
                OrganizationName = _organizationName,
            };

            ReportTable table;
            using (var scope = _scopes.CreateScope())
            {
                table = await scope.ServiceProvider.GetRequiredService<IReportService>().GenerateAsync(request);
            }
            if (version != _runVersion) return; // a newer run started

            Rows.Clear();
            foreach (var values in table.Rows)
            {
                var cells = new string[table.Columns.Count];
                for (var c = 0; c < cells.Length; c++)
                    cells[c] = ReportTable.FormatCell(c < values.Length ? values[c] : null, table.Columns[c].Type);
                Rows.Add(new ReportGridRow(cells));
            }
            Summary.Clear();
            foreach (var pair in table.Summary) Summary.Add(new SummaryItem(pair.Key, pair.Value));
            Table = table;
        }
        catch (Exception ex)
        {
            Log.Error($"Report \"{SelectedReport.Title}\" failed", ex);
            Message = "Couldn't create the report: " + ex.GetBaseException().Message;
            MessageIsError = true;
        }
        finally
        {
            if (version == _runVersion) IsBusy = false;
        }
    }

    // ---------------------------------------------------------------- export (FR-REP-5) ----

    [RelayCommand] private Task ExportExcel() => ExportAsync("Excel workbook", "xlsx", XlsxReportWriter.Write);
    [RelayCommand] private Task ExportPdf() => ExportAsync("PDF document", "pdf", ReportPdfWriter.Write);
    [RelayCommand] private Task ExportCsv() => ExportAsync("CSV (comma separated)", "csv", CsvReportWriter.Write);

    private async Task ExportAsync(string kindName, string extension, Action<ReportTable, Stream> write)
    {
        if (Table is not { } table)
        {
            Message = "Run a report first.";
            MessageIsError = true;
            return;
        }

        var suggested = SafeFileName(table.FileNameStem) + "." + extension;
        var path = await _filePicker.PickSaveFileAsync($"Export {table.Title}", kindName, new[] { "*." + extension }, suggested, extension);
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!path.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase)) path += "." + extension;

        IsBusy = true;
        try
        {
            // Write to a temp file first so a failure never leaves half a file behind.
            var temp = path + ".tmp";
            await Task.Run(() =>
            {
                using (var stream = File.Create(temp)) write(table, stream);
                File.Move(temp, path, overwrite: true);
            });
            LastExportPath = path;
            Log.Info($"Report exported: \"{table.Title}\", {table.Rows.Count} rows, {extension.ToUpperInvariant()}.");
            // Exports take data (often personal data) out of InfoID, so they are audited.
            InfoID.Desktop.Core.Services.Audit.Record(InfoID.Domain.Common.Interfaces.AuditModules.Reports, "ReportExported",
                $"Report '{table.Title}' exported to {Path.GetFileName(path)} ({table.Rows.Count:N0} rows)",
                after: new { Report = table.Title, Filters = table.Subtitle, Format = extension.ToUpperInvariant(), Rows = table.Rows.Count, File = path });
            Message = $"Saved {Path.GetFileName(path)} ({table.Rows.Count:N0} rows).";
            MessageIsError = false;
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020))
        {
            Message = "The file is open in another program (for example Excel). Close it and export again.";
            MessageIsError = true;
        }
        catch (Exception ex)
        {
            Log.Error($"Report export failed ({extension})", ex);
            Message = "Export failed: " + ex.GetBaseException().Message;
            MessageIsError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

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
            Message = "Couldn't open the file: " + ex.GetBaseException().Message;
            MessageIsError = true;
        }
    }

    [RelayCommand]
    private void ShowLastExportInFolder()
    {
        if (LastExportPath is null) return;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{LastExportPath}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(LastExportPath)!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Message = "Couldn't open the folder: " + ex.GetBaseException().Message;
            MessageIsError = true;
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return clean.Length == 0 ? "InfoID report" : clean;
    }
}
