using InfoID.Application.Dtos.PrintingModule;

namespace InfoID.Application.Interfaces;

/// <summary>
/// Print history (FR-PRN-4, FR-PRN-6): records every print run in the local database --
/// PrintJob, PrinterProfile, Cardholder, Card and one PrintJobItem per card -- and
/// answers history queries for the Print History page, Dashboard and Reports.
/// </summary>
public interface IPrintHistoryService
{
    /// <summary>Cards already issued (Issued/Active) for these Person IDs with this
    /// design, so the Print window can ask for a reprint reason first.</summary>
    Task<IReadOnlyList<ExistingCardDto>> FindExistingCardsAsync(long? templateId, IReadOnlyCollection<string> personKeys, CancellationToken ct = default);

    /// <summary>Records one finished (or failed) print run. Never throws for data
    /// problems the user can't fix; callers still wrap it so a history problem never
    /// turns a successful print into an error.</summary>
    Task<RecordPrintJobResult> RecordJobAsync(RecordPrintJobRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<PrintJobSummaryDto>> GetJobsAsync(PrintJobFilter filter, CancellationToken ct = default);

    Task<IReadOnlyList<PrintJobItemDto>> GetJobItemsAsync(long printJobId, CancellationToken ct = default);
}
