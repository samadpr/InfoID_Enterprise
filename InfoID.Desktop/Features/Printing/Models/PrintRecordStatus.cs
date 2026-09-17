using System;

namespace InfoID.Desktop.Features.Printing.Models;

/// <summary>Whether a given cardholder record has been printed yet -- the "Advanced
/// Print Operations" tab's whole reason for existing (a real print run needs to know
/// what's left to do, and to survive the app being closed mid-batch).</summary>
public enum PrintRecordState { NotPrinted, Printed, Failed }

/// <summary>One row of persisted print-status state, keyed by PreviewRecord.Label (the
/// only stable identity available without a real Cardholder Management module -- see
/// SamplePreviewDataProvider's own doc comment. Once that module exists, this should key
/// off the real cardholder id instead, but the status-tracking mechanics here don't
/// change.</summary>
public sealed class PrintRecordStatus
{
    public required string RecordLabel { get; init; }
    public PrintRecordState State { get; set; } = PrintRecordState.NotPrinted;
    public DateTime? LastPrintedAtUtc { get; set; }

    /// <summary>Running count of successful prints for this record -- only incremented
    /// when an Advanced Print Operations mapping row opts a record into a PrintCounter/
    /// PrintStatusAndCounter/PrintStatusAndCounterAndDate marker (see
    /// PrintStatusMarker); zero otherwise, never a stand-in for State.</summary>
    public int PrintCount { get; set; }
}
