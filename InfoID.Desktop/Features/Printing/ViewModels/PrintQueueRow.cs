using System;
using CommunityToolkit.Mvvm.ComponentModel;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.Features.Printing.Models;

namespace InfoID.Desktop.Features.Printing.ViewModels;

/// <summary>One row of the bulk-print queue -- a sample record paired with its
/// persisted print status (Advanced Print Operations tab). A thin observable wrapper
/// rather than exposing PrintRecordStatus directly, so status changes made while the
/// Print dialog is open update the visible list immediately.</summary>
public sealed partial class PrintQueueRow : ObservableObject
{
    public PreviewRecord Record { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrinted))]
    private PrintRecordState _state;

    [ObservableProperty]
    private DateTime? _lastPrintedAtUtc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrintCount))]
    private int _printCount;

    public bool HasPrintCount => PrintCount > 0;

    public PrintQueueRow(PreviewRecord record, PrintRecordStatus? status)
    {
        Record = record;
        _state = status?.State ?? PrintRecordState.NotPrinted;
        _lastPrintedAtUtc = status?.LastPrintedAtUtc;
        _printCount = status?.PrintCount ?? 0;
    }

    /// <summary>Two-way bindable simplification of State for the Records list's toggle
    /// switch -- a real 3-state row (NotPrinted/Printed/Failed) collapsed to on/off,
    /// same as flipping it any other way: toggling always lands on Printed or
    /// NotPrinted, since a switch has no third position for Failed.</summary>
    public bool IsPrinted
    {
        get => State == PrintRecordState.Printed;
        set
        {
            State = value ? PrintRecordState.Printed : PrintRecordState.NotPrinted;
            LastPrintedAtUtc = value ? DateTime.UtcNow : null;
        }
    }

    public PrintRecordStatus ToStatus() => new()
    {
        RecordLabel = Record.Label,
        State = State,
        LastPrintedAtUtc = LastPrintedAtUtc,
        PrintCount = PrintCount,
    };
}
