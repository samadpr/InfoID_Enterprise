using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Dtos.PrintingModule;
using InfoID.Application.Interfaces;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using InfoID.Domain.Common.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.PrintHistory.ViewModels;

public enum HistoryPeriod { Today, Last7Days, Last30Days, ThisYear, All }

public sealed record HistoryPeriodOption(HistoryPeriod Period, string Label);

/// <summary>One print run as a row in the jobs grid.</summary>
public sealed class PrintJobRow
{
    public PrintJobRow(PrintJobSummaryDto dto)
    {
        Dto = dto;
        var local = DateTime.SpecifyKind(dto.CreatedDate, DateTimeKind.Utc).ToLocalTime();
        When = local;
        WhenText = local.ToString("g");
        TypeText = dto.JobType switch
        {
            PrintJobType.Single => "Single",
            PrintJobType.Batch => "Batch",
            PrintJobType.Reprint => "Reprint",
            _ => dto.JobType.ToString(),
        };
        StatusText = dto.Status switch
        {
            PrintJobStatus.Completed => "Printed",
            PrintJobStatus.Failed => "Failed",
            PrintJobStatus.Cancelled => "Cancelled",
            _ => dto.Status.ToString(),
        };
        IsFailed = dto.Status == PrintJobStatus.Failed;
        ReprintText = dto.ReprintCount > 0 ? dto.ReprintCount.ToString() : "";
    }

    public PrintJobSummaryDto Dto { get; }
    public long Id => Dto.Id;
    public DateTime When { get; }
    public string WhenText { get; }
    public string DesignName => Dto.DesignName;
    public string PrinterName => Dto.PrinterName;
    public string TypeText { get; }
    public string StatusText { get; }
    public bool IsFailed { get; }
    public int Records => Dto.Records;
    public int TotalCards => Dto.TotalCards;
    public string ReprintText { get; }
}

/// <summary>One printed card in the selected job.</summary>
public sealed class PrintJobItemRow
{
    public PrintJobItemRow(PrintJobItemDto dto)
    {
        Dto = dto;
        ReprintText = dto.ReprintReason switch
        {
            null => "",
            ReprintReasonCode.Renewed => "Reprint: renewed",
            { } reason => "Reprint: " + reason.ToString().ToLowerInvariant(),
        };
        ExpiryText = dto.ExpiryDate is { } expiry ? expiry.ToString("d") : "No expiry";
        CardStatusText = dto.CardStatus.ToString();
    }

    public PrintJobItemDto Dto { get; }
    public int Sequence => Dto.Sequence;
    public string CardNumber => Dto.CardNumber;
    public string FullName => Dto.FullName;
    public string PersonKey => Dto.PersonKey ?? "";
    public string ReprintText { get; }
    public string ExpiryText { get; }
    public string CardStatusText { get; }
}

/// <summary>
/// Print history page (FR-PRN-6): every print run recorded by the Print dialog, newest
/// first, filterable by period and searchable by design, printer, person name or ID.
/// Selecting a run lists its cards (card number, person, reprint reason, status, expiry).
/// Reads through IPrintHistoryService in a fresh DI scope per load, like the License page.
/// </summary>
public sealed partial class PrintHistoryViewModel : ViewModelBase
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<PrintHistoryViewModel>();

    private readonly INavigationService _navigation;
    private readonly IServiceScopeFactory _scopes;
    private CancellationTokenSource? _searchDelay;
    private int _loadVersion;

    public PrintHistoryViewModel(INavigationService navigation, IServiceScopeFactory scopes)
    {
        _navigation = navigation;
        _scopes = scopes;
#pragma warning disable MVVMTK0034 // set the default without triggering a load from the constructor
        _selectedPeriod = Periods[2];
#pragma warning restore MVVMTK0034
    }

    public IReadOnlyList<HistoryPeriodOption> Periods { get; } = new[]
    {
        new HistoryPeriodOption(HistoryPeriod.Today, "Today"),
        new HistoryPeriodOption(HistoryPeriod.Last7Days, "Last 7 days"),
        new HistoryPeriodOption(HistoryPeriod.Last30Days, "Last 30 days"),
        new HistoryPeriodOption(HistoryPeriod.ThisYear, "This year"),
        new HistoryPeriodOption(HistoryPeriod.All, "All time"),
    };

    public ObservableCollection<PrintJobRow> Jobs { get; } = new();
    public ObservableCollection<PrintJobItemRow> Items { get; } = new();

    [ObservableProperty]
    private HistoryPeriodOption _selectedPeriod;

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionTitle))]
    private PrintJobRow? _selectedJob;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _message;

    [ObservableProperty] private int _jobCount;
    [ObservableProperty] private int _cardCount;
    [ObservableProperty] private int _reprintCount;
    [ObservableProperty] private int _failedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _hasLoaded;

    public bool HasSelection => SelectedJob is not null;
    public bool IsEmpty => HasLoaded && Jobs.Count == 0;

    public string SelectionTitle => SelectedJob is null
        ? "Select a print run to see its cards"
        : $"{SelectedJob.DesignName} · {SelectedJob.WhenText} · {SelectedJob.PrinterName}";

    /// <summary>Print run to select after the next load (opened from the Dashboard).</summary>
    private long? _selectAfterLoad;

    /// <summary>A long parameter is a PrintJob id to show (e.g. a Dashboard "recent
    /// activity" click): the period widens to All time so the run is in the list.</summary>
    public override void OnNavigatedTo(object? parameter)
    {
        if (parameter is long jobId)
        {
            _selectAfterLoad = jobId;
            if (SelectedPeriod.Period != HistoryPeriod.All)
            {
                SelectedPeriod = Periods.First(p => p.Period == HistoryPeriod.All); // triggers a load
                return;
            }
        }
        _ = LoadAsync();
    }

    partial void OnSelectedPeriodChanged(HistoryPeriodOption value) => _ = LoadAsync();

    partial void OnSearchTextChanged(string? value)
    {
        // Wait for a pause in typing so each keystroke doesn't re-query the database.
        _searchDelay?.Cancel();
        var cts = _searchDelay = new CancellationTokenSource();
        _ = Task.Delay(350, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadAsync());
        }, TaskScheduler.Default);
    }

    partial void OnSelectedJobChanged(PrintJobRow? value) => _ = LoadItemsAsync(value);

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    [RelayCommand]
    private void ClearSearch() => SearchText = null;

    [RelayCommand]
    private void GoHome() => _navigation.NavigateToRoot<WelcomeViewModel>();

    private (DateTime? From, DateTime? To) PeriodRange()
    {
        var today = DateTime.Today;
        DateTime? fromLocal = SelectedPeriod.Period switch
        {
            HistoryPeriod.Today => today,
            HistoryPeriod.Last7Days => today.AddDays(-6),
            HistoryPeriod.Last30Days => today.AddDays(-29),
            HistoryPeriod.ThisYear => new DateTime(today.Year, 1, 1),
            _ => null,
        };
        return (fromLocal?.ToUniversalTime(), null);
    }

    private async Task LoadAsync()
    {
        var version = Interlocked.Increment(ref _loadVersion);
        var previousId = SelectedJob?.Id;
        IsLoading = true;
        Message = null;
        try
        {
            var (from, to) = PeriodRange();
            IReadOnlyList<PrintJobSummaryDto> jobs;
            using (var scope = _scopes.CreateScope())
            {
                jobs = await scope.ServiceProvider.GetRequiredService<IPrintHistoryService>().GetJobsAsync(new PrintJobFilter
                {
                    FromUtc = from,
                    ToUtc = to,
                    SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                    MaxResults = 1000,
                });
            }
            if (version != _loadVersion) return; // a newer search/period already started

            Jobs.Clear();
            foreach (var job in jobs) Jobs.Add(new PrintJobRow(job));

            JobCount = jobs.Count;
            CardCount = jobs.Where(j => j.Status == PrintJobStatus.Completed).Sum(j => j.TotalCards);
            ReprintCount = jobs.Sum(j => j.ReprintCount);
            FailedCount = jobs.Count(j => j.Status == PrintJobStatus.Failed);

            var wanted = _selectAfterLoad ?? previousId;
            _selectAfterLoad = null;
            SelectedJob = Jobs.FirstOrDefault(j => j.Id == wanted) ?? Jobs.FirstOrDefault();
            if (jobs.Count >= 1000) Message = "Showing the newest 1,000 print runs. Narrow the period or search to see others.";
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't load print history", ex);
            Message = "Couldn't load print history: " + ex.GetBaseException().Message;
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
                HasLoaded = true;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    private async Task LoadItemsAsync(PrintJobRow? job)
    {
        Items.Clear();
        if (job is null) return;
        try
        {
            using var scope = _scopes.CreateScope();
            var items = await scope.ServiceProvider.GetRequiredService<IPrintHistoryService>().GetJobItemsAsync(job.Id);
            if (SelectedJob != job) return;
            foreach (var item in items) Items.Add(new PrintJobItemRow(item));
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't load the cards for a print run", ex);
            Message = "Couldn't load the cards for this print run: " + ex.GetBaseException().Message;
        }
    }
}
