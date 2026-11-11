using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Dtos.Encoding;
using InfoID.Application.Interfaces;
using InfoID.CardEncoding.Contactless;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.Printing.Models;
using InfoID.Desktop.Features.Printing.Services;
using InfoID.Desktop.Features.Rfid.Services;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.Rfid.ViewModels;

/// <summary>One record to encode. Row is set for grid data (needed for UID write-back).</summary>
public sealed record NfcWriteRecord(string Label, IReadOnlyDictionary<string, string> Fields, System.Data.DataRow? Row);

public sealed class NfcWriteJob
{
    public required string DesignName { get; init; }
    public long? TemplateId { get; init; }
    /// <summary>PrintHistoryPlanner.BuildDesignKey: shares the Person ID column with Print.</summary>
    public required string HistoryDesignKey { get; init; }
    public required NfcEncodingSettings Settings { get; init; }
    public required IReadOnlyList<NfcWriteRecord> Records { get; init; }
    public bool IsDataBound { get; init; }
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    /// <summary>Writable text columns (Internal Database only); empty = write-back unavailable.</summary>
    public IReadOnlyList<string> UidColumns { get; init; } = Array.Empty<string>();
    /// <summary>(record, column, uid) → error message or null.</summary>
    public Func<NfcWriteRecord, string, string, string?>? SaveUidToData { get; init; }
    /// <summary>(record, column, uid) → label of ANOTHER row already holding this UID, or null.</summary>
    public Func<NfcWriteRecord, string, string, string?>? FindUidInData { get; init; }
    public Action<bool, string?>? RememberUidOption { get; init; }
}

public enum NfcWriteStage { Ready, WaitingForCard, Working, ConfirmOverwrite, Problem, Finished }
public enum NfcWriteItemState { Pending, Invalid, Written, Skipped, Failed }

public sealed partial class NfcWriteItem : ObservableObject
{
    public NfcWriteItem(NfcWriteRecord record)
    {
        Record = record;
        _displayName = record.Label;
    }

    public NfcWriteRecord Record { get; }
    public string Label => Record.Label;
    public NfcContent? Content { get; set; }
    public string? PersonKey { get; set; }
    public string? Uid { get; set; }

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(IsWritten), nameof(IsSkipped), nameof(IsFailed))]
    private NfcWriteItemState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    private bool _isCurrent;

    public bool IsPending => State == NfcWriteItemState.Pending && !IsCurrent;
    public bool IsWritten => State == NfcWriteItemState.Written;
    public bool IsSkipped => State is NfcWriteItemState.Skipped or NfcWriteItemState.Invalid;
    public bool IsFailed => State == NfcWriteItemState.Failed;

    public void Set(NfcWriteItemState state, string text)
    {
        State = state;
        StatusText = text;
    }
}

/// <summary>
/// Guided "Write NFC tags": one record at a time. Place a tag → it is checked (format, lock,
/// real capacity, already linked to someone, UID already in the data) → existing content is shown
/// and must be approved (Overwrite / Overwrite all for this batch) → written → read back and
/// verified → linked to the record (Card.RFIDUid) → optional UID write-back → next record.
/// </summary>
public sealed partial class NfcWriteDialogViewModel : DialogViewModelBase<bool>
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<NfcWriteDialogViewModel>();

    private readonly NfcWriteJob _job;
    private readonly NfcCardWriter _writer;
    private readonly IContactlessReaderService _readers;
    private readonly NfcContentBuilder _builder;
    private readonly IServiceScopeFactory? _scopes;

    private PrintHistorySettings? _historySettings;
    private bool _historyLoaded;
    private string? _nameColumn;
    private NfcTagInfo? _pendingTag;
    private string? _lastWrittenUid;
    private string? _lastProblem;
    private bool _overwriteAll;
    private bool _busy;
    private bool _subscribed;
    private bool _audited;
    private int _startCount;
    private int _uidsSaved;

    public NfcWriteDialogViewModel(NfcWriteJob job, NfcCardWriter writer, IContactlessReaderService readers,
        NfcContentBuilder builder, IServiceScopeFactory? scopes)
    {
        _job = job;
        _writer = writer;
        _readers = readers;
        _builder = builder;
        _scopes = scopes;

        foreach (var record in job.Records) Items.Add(new NfcWriteItem(record));
        UidColumn = job.UidColumns.Contains(job.Settings.UidColumn ?? "")
            ? job.Settings.UidColumn
            : job.UidColumns.FirstOrDefault(c => c.Contains("uid", StringComparison.OrdinalIgnoreCase) || c.Contains("nfc", StringComparison.OrdinalIgnoreCase));
        SaveUidToData = job.Settings.SaveUidToData && CanSaveUidToData;

        Validate();
        _ = LoadPersonColumnAsync();
    }

    public override string Title => "InfoID - Write NFC tags";
    public override bool CanResize => true;
    public override (double Width, double Height)? PreferredSize => (660, 700);

    public ObservableCollection<NfcWriteItem> Items { get; } = new();
    public ObservableCollection<string> ExistingContent { get; } = new();
    public IReadOnlyList<string> PersonColumns => _job.Columns;
    public IReadOnlyList<string> UidColumns => _job.UidColumns;
    public bool IsDataBound => _job.IsDataBound;
    public bool CanSaveUidToData => _job.UidColumns.Count > 0 && _job.SaveUidToData is not null;
    public bool HasReader => !string.IsNullOrEmpty(_readers.SelectedReader);

    public string SubtitleText => $"{_job.DesignName} · {Items.Count} record{(Items.Count == 1 ? "" : "s")}";
    public string ReaderText => HasReader
        ? "Reader: " + _readers.SelectedReader
        : "No card reader selected. Choose one in File › Card Encoding (RFID / NFC).";
    public string UidOptionNote => CanSaveUidToData
        ? "InfoID always remembers which tag belongs to which record. Saving the UID into the data as well is optional."
        : IsDataBound
            ? "InfoID remembers which tag belongs to which record. Saving the UID into the data is available for the InfoID Internal Database (add a text column such as NfcUid)."
            : "This design has no data connected, so tags aren't linked to records.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsRunning), nameof(IsWorking), nameof(IsConfirming), nameof(IsFinished), nameof(CanWriteNow))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    private NfcWriteStage _stage = NfcWriteStage.Ready;

    public bool IsReady => Stage == NfcWriteStage.Ready;
    public bool IsRunning => Stage is NfcWriteStage.WaitingForCard or NfcWriteStage.Working or NfcWriteStage.ConfirmOverwrite or NfcWriteStage.Problem;
    public bool IsWorking => Stage == NfcWriteStage.Working;
    public bool IsConfirming => Stage == NfcWriteStage.ConfirmOverwrite;
    public bool IsFinished => Stage == NfcWriteStage.Finished;
    public bool CanWriteNow => Stage is NfcWriteStage.WaitingForCard or NfcWriteStage.Problem;

    [ObservableProperty] private string _heading = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasValidationText))] private string _validationText = "";
    public bool HasValidationText => ValidationText.Length > 0;

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(StartCommand))] private int _readyCount;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CurrentContentText))] private NfcWriteItem? _current;
    [ObservableProperty] private string? _personIdColumn;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(StartCommand))] private bool _saveUidToData;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(StartCommand))] private string? _uidColumn;

    public string CurrentContentText
    {
        get
        {
            if (Current?.Content is not { } c) return "";
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(c.Url)) parts.Add("Link: " + c.Url);
            if (!string.IsNullOrEmpty(c.Text)) parts.Add("Text: " + c.Text);
            parts.Add($"{c.SizeBytes} bytes");
            return string.Join("   ·   ", parts);
        }
    }

    private int WrittenCount => Items.Count(i => i.State == NfcWriteItemState.Written);
    private int SkippedCount => Items.Count(i => i.State is NfcWriteItemState.Skipped or NfcWriteItemState.Invalid);
    private int FailedCount => Items.Count(i => i.State == NfcWriteItemState.Failed);

    public double ProgressValue => _startCount == 0 ? 0 :
        100.0 * Items.Count(i => i.State is NfcWriteItemState.Written or NfcWriteItemState.Skipped or NfcWriteItemState.Failed && i.Content is not null && !i.IsSkippedBeforeStart()) / _startCount;
    public string ProgressText
    {
        get
        {
            var remaining = Items.Count(i => i.State == NfcWriteItemState.Pending);
            return $"{WrittenCount} written · {Items.Count(i => i.State == NfcWriteItemState.Skipped)} skipped · {FailedCount} not written · {remaining} to go";
        }
    }

    // ------------------------------------------------------------------ setup ----

    private async Task LoadPersonColumnAsync()
    {
        try { _historySettings = await PrintHistorySettingsStore.Shared.LoadAsync(_job.HistoryDesignKey); }
        catch (Exception ex) { Log.Warn("Print history settings couldn't be read", ex); }

        var columns = _job.Columns;
        string? Pick(string? remembered, Func<IEnumerable<string>, string?> detect) =>
            remembered is not null && columns.Contains(remembered) ? remembered : detect(columns);

        _nameColumn = Pick(_historySettings?.NameColumn, PrintHistoryPlanner.DetectNameColumn);
        PersonIdColumn = IsDataBound ? Pick(_historySettings?.PersonIdColumn, PrintHistoryPlanner.DetectPersonIdColumn) : null;
        _historyLoaded = true;
        Validate();
    }

    partial void OnPersonIdColumnChanged(string? value)
    {
        if (!_historyLoaded) return;
        Validate();
        if (value is null) return;
        // Same setting the Print dialog uses, so Print history finds these cards.
        _historySettings ??= new PrintHistorySettings();
        _historySettings.PersonIdColumn = value;
        _ = PrintHistorySettingsStore.Shared.SaveAsync(_job.HistoryDesignKey, _historySettings);
    }

    partial void OnSaveUidToDataChanged(bool value) => Validate();
    partial void OnUidColumnChanged(string? value) => Validate();

    partial void OnCurrentChanged(NfcWriteItem? oldValue, NfcWriteItem? newValue)
    {
        if (oldValue is not null) oldValue.IsCurrent = false;
        if (newValue is not null) newValue.IsCurrent = true;
    }

    /// <summary>Builds every record's content up front; records that can't be written are skipped.</summary>
    private void Validate()
    {
        if (Stage != NfcWriteStage.Ready) return;

        var column = IsDataBound ? PersonIdColumn : null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            var fields = item.Record.Fields;
            item.Content = _builder.Build(_job.Settings, fields);
            item.PersonKey = column is not null && fields.TryGetValue(column, out var k) && !string.IsNullOrWhiteSpace(k) ? k.Trim() : null;
            item.DisplayName = _nameColumn is not null && fields.TryGetValue(_nameColumn, out var n) && !string.IsNullOrWhiteSpace(n)
                ? n.Trim()
                : item.PersonKey ?? item.Label;

            string? problem = item.Content.Issues.FirstOrDefault(i => i.Severity == NfcIssueSeverity.Error)?.Message;
            if (problem is null && IsDataBound && column is null) problem = "No Person ID column chosen";
            else if (problem is null && IsDataBound && item.PersonKey is null) problem = $"No value in \"{column}\"";
            else if (problem is null && item.PersonKey is not null && !seen.Add(item.PersonKey)) problem = $"Same \"{column}\" as another record in this list";

            item.Set(problem is null ? NfcWriteItemState.Pending : NfcWriteItemState.Invalid, problem ?? "Ready");
        }

        ReadyCount = Items.Count(i => i.State == NfcWriteItemState.Pending);
        var invalid = Items.Count - ReadyCount;
        ValidationText =
            IsDataBound && column is null ? "Choose the column that identifies each person (for example EmployeeNo), so every tag is linked to the right record. Print uses the same setting."
            : SaveUidToData && UidColumn is null ? "Choose the column to save the UID into, or untick that option."
            : Items.Count == 0 ? "There are no records to write. Connect data or tick some rows."
            : invalid > 0 ? $"{invalid} of {Items.Count} record{(Items.Count == 1 ? "" : "s")} can't be written and will be skipped (see the list)."
            : "";
    }

    // ------------------------------------------------------------------ commands ----

    private bool CanStart() => Stage == NfcWriteStage.Ready && ReadyCount > 0 && HasReader &&
                               (!IsDataBound || PersonIdColumn is not null) && (!SaveUidToData || UidColumn is not null);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (CanSaveUidToData) _job.RememberUidOption?.Invoke(SaveUidToData, UidColumn);
        _startCount = ReadyCount;
        Subscribe();
        MoveToNext();
        _ = ProcessCardAsync(auto: true); // a tag may already be on the reader
    }

    [RelayCommand]
    private Task WriteNow() => ProcessCardAsync(auto: false);

    [RelayCommand]
    private Task Overwrite() => WritePendingAsync();

    [RelayCommand]
    private Task OverwriteAllRemaining()
    {
        _overwriteAll = true; // only for this batch, after the user has seen existing content
        return WritePendingAsync();
    }

    [RelayCommand]
    private void UseAnotherTag()
    {
        if (Stage == NfcWriteStage.ConfirmOverwrite) ShowWaiting("Kept that tag as it is. Place another tag.");
    }

    [RelayCommand]
    private void SkipRecord()
    {
        if (_busy || Current is not { } item) return;
        if (_lastProblem is not null) item.Set(NfcWriteItemState.Failed, "Not written: " + _lastProblem);
        else item.Set(NfcWriteItemState.Skipped, "Skipped");
        MoveToNext();
    }

    private bool CanStop() => IsRunning && Stage != NfcWriteStage.Working;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => Finish(stopped: true);

    [RelayCommand]
    private void Close() => RequestClose(WrittenCount > 0);

    [RelayCommand]
    private void Cancel() => RequestClose(false);

    /// <summary>Called by the view when the window closes (also via the title-bar X).</summary>
    public void OnViewClosed()
    {
        Unsubscribe();
        if (Stage is NfcWriteStage.Ready or NfcWriteStage.Finished) return;
        foreach (var item in Items.Where(i => i.State == NfcWriteItemState.Pending))
            item.Set(NfcWriteItemState.Skipped, "Not written (closed)");
        _ = RecordBatchAsync(stopped: true);
    }

    // ------------------------------------------------------------------ flow ----

    private async Task ProcessCardAsync(bool auto)
    {
        if (_busy || Current is not { } item || item.Content is not { } content) return;
        if (Stage is not (NfcWriteStage.WaitingForCard or NfcWriteStage.Problem)) return;
        _busy = true;
        Stage = NfcWriteStage.Working;
        Heading = "Checking the tag…";
        Message = "Keep the tag on the reader.";
        try
        {
            var tag = await Task.Run(() => _writer.Inspect());

            if (_lastWrittenUid is not null && string.Equals(tag.Uid, _lastWrittenUid, StringComparison.OrdinalIgnoreCase))
            {
                ShowWaiting("That tag was just written. Remove it and place the next tag.");
                return;
            }

            var problem = await CheckTagAsync(item, content, tag);
            if (problem is not null)
            {
                ShowProblem(problem);
                return;
            }

            if (tag.HasData && !tag.Holds(content.Records) && !_overwriteAll)
            {
                ShowConfirm(item, tag);
                return;
            }

            await WriteAsync(item, content, tag);
        }
        catch (ContactlessException ex) when (auto && ex.Error == ContactlessError.NoCard)
        {
            ShowWaiting(null);
        }
        catch (ContactlessException ex)
        {
            ShowProblem(Describe(ex));
        }
        catch (Exception ex)
        {
            Log.Error("NFC tag check failed", ex);
            ShowProblem("The tag couldn't be checked: " + ex.GetBaseException().Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<string?> CheckTagAsync(NfcWriteItem item, NfcContent content, NfcTagInfo tag)
    {
        if (!tag.IsFormatted)
            return "This tag isn't formatted for NFC (NDEF), so InfoID won't write it. Use an NFC Forum Type 2 tag (NTAG213/215/216).";
        if (tag.IsReadOnly)
            return "This tag is locked (read-only) and can't be written. Use another tag.";
        if (content.SizeBytes > tag.CapacityBytes)
            return $"The content needs {content.SizeBytes} bytes but this {tag.ModelName} holds {tag.CapacityBytes}. Shorten the link or text, or use a larger tag (NTAG216 holds 872 bytes).";

        // One tag, one record. A failed lookup stops the write rather than risk a duplicate.
        var owner = await WithServiceAsync(s => s.FindTagOwnerAsync(tag.Uid));
        if (owner is not null && !IsSameRecord(owner, item))
            return $"This tag is already linked to {(string.IsNullOrWhiteSpace(owner.FullName) ? "another record" : owner.FullName)} (card {owner.CardNumber}). Use a different tag.";

        if (SaveUidToData && UidColumn is { } column && _job.FindUidInData?.Invoke(item.Record, column, tag.Uid) is { } other)
            return $"{other} already has this tag's UID in \"{column}\". Use a different tag.";

        return null;
    }

    private bool IsSameRecord(NfcTagOwnerDto owner, NfcWriteItem item) =>
        IsDataBound && item.PersonKey is not null && owner.TemplateId == _job.TemplateId &&
        string.Equals(owner.PersonKey, item.PersonKey, StringComparison.OrdinalIgnoreCase);

    private async Task WritePendingAsync()
    {
        if (_busy || Stage != NfcWriteStage.ConfirmOverwrite || _pendingTag is not { } tag ||
            Current is not { } item || item.Content is not { } content) return;
        _busy = true;
        try { await WriteAsync(item, content, tag); }
        finally { _busy = false; }
    }

    /// <summary>Never throws: problems go to the Problem stage.</summary>
    private async Task WriteAsync(NfcWriteItem item, NfcContent content, NfcTagInfo tag)
    {
        Stage = NfcWriteStage.Working;
        Heading = "Writing…";
        Message = "Keep the tag still on the reader until it's verified.";
        try
        {
            await Task.Run(() => _writer.Write(tag.Uid, content.Records));
        }
        catch (Exception ex)
        {
            var reason = ex is ContactlessException ce ? Describe(ce) : "The tag couldn't be written: " + ex.GetBaseException().Message;
            if (ex is not ContactlessException) Log.Error("NFC write failed", ex);
            await RecordFailureAsync(item, reason);
            ShowProblem(reason + " Nothing was linked. Place the tag again to retry.");
            return;
        }

        _lastWrittenUid = tag.Uid;
        item.Uid = tag.Uid;
        var notes = new List<string>();

        if (IsDataBound && item.PersonKey is { } key)
        {
            try
            {
                var linked = await WithServiceAsync(s => s.AssignTagAsync(new NfcTagAssignment
                {
                    TemplateId = _job.TemplateId,
                    PersonKey = key,
                    DisplayName = item.DisplayName,
                    Uid = tag.Uid,
                }));
                if (linked is not null)
                    notes.Add(linked.CreatedCard ? $"new card {linked.CardNumber}" : $"card {linked.CardNumber}" +
                              (linked.PreviousUid is null ? "" : " (old tag unlinked)"));
            }
            catch (Exception ex)
            {
                Log.Error("NFC tag couldn't be linked to its record", ex);
                notes.Add("not linked in InfoID: " + ex.GetBaseException().Message);
            }
        }

        if (SaveUidToData && UidColumn is { } column && _job.SaveUidToData is { } save)
        {
            var error = save(item.Record, column, tag.Uid);
            if (error is null) { _uidsSaved++; notes.Add($"UID saved to {column}"); }
            else notes.Add("UID not saved to the data: " + error);
        }

        item.Set(NfcWriteItemState.Written, "Written and verified" + (notes.Count > 0 ? " · " + string.Join(" · ", notes) : ""));
        MoveToNext("Written and verified. Remove the tag and place the next one.");
    }

    private void MoveToNext(string? note = null)
    {
        _lastProblem = null;
        Current = Items.FirstOrDefault(i => i.State == NfcWriteItemState.Pending);
        RaiseCounts();
        if (Current is null)
        {
            Finish(stopped: false);
            return;
        }
        ShowWaiting(note);
    }

    private void ShowWaiting(string? note)
    {
        _pendingTag = null;
        ExistingContent.Clear();
        Stage = NfcWriteStage.WaitingForCard;
        var position = Items.Where(i => i.State != NfcWriteItemState.Invalid).ToList().IndexOf(Current!) + 1;
        Heading = $"Place the tag for {Current!.DisplayName}";
        Message = (note is null ? "" : note + " ") + $"Record {position} of {_startCount}. The tag is written and checked automatically.";
    }

    private void ShowConfirm(NfcWriteItem item, NfcTagInfo tag)
    {
        _pendingTag = tag;
        ExistingContent.Clear();
        foreach (var line in tag.DescribeContent()) ExistingContent.Add(line);
        Stage = NfcWriteStage.ConfirmOverwrite;
        Heading = "This tag already has content";
        Message = $"{tag.ModelName} · UID {tag.Uid}. Replace it with the content for {item.DisplayName}?";
    }

    private void ShowProblem(string message)
    {
        _lastProblem = message;
        _pendingTag = null;
        ExistingContent.Clear();
        Stage = NfcWriteStage.Problem;
        Heading = "This tag can't be written";
        Message = message;
    }

    private void Finish(bool stopped)
    {
        if (Stage == NfcWriteStage.Finished) return;
        Unsubscribe();
        foreach (var item in Items.Where(i => i.State == NfcWriteItemState.Pending))
            item.Set(NfcWriteItemState.Skipped, "Not written (stopped)");
        Current = null;
        Stage = NfcWriteStage.Finished;
        Heading = stopped ? "Stopped" : "Finished";
        Message = $"{WrittenCount} tag{(WrittenCount == 1 ? "" : "s")} written and verified, {SkippedCount} skipped, {FailedCount} not written." +
                  (_uidsSaved > 0 ? $" UID saved to the data for {_uidsSaved}." : "");
        RaiseCounts();
        _ = RecordBatchAsync(stopped);
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressValue));
    }

    private static string Describe(ContactlessException ex) => ex.Error switch
    {
        ContactlessError.NoCard => "No tag on the reader. Place the tag flat on the reader.",
        ContactlessError.CardRemoved => "The tag moved or was removed. Place it again and keep it still.",
        ContactlessError.NoReader or ContactlessError.ReaderUnavailable =>
            "The card reader isn't available. Check it's plugged in, then choose it in File › Card Encoding.",
        ContactlessError.ReaderBusy => "The card reader is being used by another program. Close that program and retry.",
        ContactlessError.VerifyFailed => "The tag didn't read back correctly after writing.",
        _ => ex.Message,
    };

    // ------------------------------------------------------------------ reader events ----

    private void Subscribe()
    {
        if (_subscribed) return;
        _readers.CardInserted += OnCardInserted;
        _readers.CardRemoved += OnCardRemoved;
        _readers.ReaderError += OnReaderError;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _readers.CardInserted -= OnCardInserted;
        _readers.CardRemoved -= OnCardRemoved;
        _readers.ReaderError -= OnReaderError;
        _subscribed = false;
    }

    private bool IsSelectedReader(CardPresenceEventArgs e) =>
        _readers.SelectedReader is null || string.Equals(e.ReaderName, _readers.SelectedReader, StringComparison.Ordinal);

    private void OnCardInserted(object? sender, CardPresenceEventArgs e)
    {
        if (!IsSelectedReader(e)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (Stage is NfcWriteStage.WaitingForCard or NfcWriteStage.Problem) _ = ProcessCardAsync(auto: true);
        });
    }

    private void OnCardRemoved(object? sender, CardPresenceEventArgs e)
    {
        if (!IsSelectedReader(e)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (Stage == NfcWriteStage.ConfirmOverwrite) ShowWaiting("The tag was removed before you chose.");
        });
    }

    private void OnReaderError(object? sender, ContactlessException ex) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (Stage is NfcWriteStage.WaitingForCard or NfcWriteStage.ConfirmOverwrite) ShowProblem(Describe(ex));
        });

    // ------------------------------------------------------------------ services ----

    private async Task<T?> WithServiceAsync<T>(Func<INfcEncodingService, Task<T>> action)
    {
        if (_scopes is null) return default;
        using var scope = _scopes.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<INfcEncodingService>());
    }

    private async Task RecordFailureAsync(NfcWriteItem item, string reason)
    {
        if (!IsDataBound || item.PersonKey is null) return;
        try { await WithServiceAsync(async s => { await s.RecordFailureAsync(_job.TemplateId, item.PersonKey, reason); return true; }); }
        catch (Exception ex) { Log.Warn("NFC failure couldn't be logged", ex); }
    }

    private async Task RecordBatchAsync(bool stopped)
    {
        if (_audited || _startCount == 0) return;
        _audited = true;
        var summary = new NfcEncodingBatchSummary
        {
            DesignName = _job.DesignName,
            TemplateId = _job.TemplateId,
            Records = _startCount,
            Written = WrittenCount,
            Skipped = Items.Count(i => i.State == NfcWriteItemState.Skipped),
            Failed = FailedCount,
            UidsSavedToData = _uidsSaved,
            Stopped = stopped,
        };
        try { await WithServiceAsync(async s => { await s.RecordBatchAsync(summary); return true; }); }
        catch (Exception ex) { Log.Warn("NFC batch couldn't be audited", ex); }
    }
}

internal static class NfcWriteItemExtensions
{
    /// <summary>Records that failed validation never count toward progress.</summary>
    public static bool IsSkippedBeforeStart(this NfcWriteItem item) => item.State == NfcWriteItemState.Invalid;
}
