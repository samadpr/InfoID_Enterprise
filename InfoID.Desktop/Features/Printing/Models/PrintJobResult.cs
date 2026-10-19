namespace InfoID.Desktop.Features.Printing.Models;

/// <summary>Outcome of one print submission. <see cref="Cancelled"/> means the user
/// closed the Windows print dialog without printing, which is not a failure.
/// <see cref="PrinterName"/> is the printer the job actually went to (the user can pick
/// a different one in the Windows print dialog). <see cref="Error"/> is the OS's own
/// message when submission failed, shown to the user as-is. A cancelled job may carry
/// an <see cref="Error"/> too: the reason InfoID stopped it before anything printed.
/// <see cref="CopiesPrinted"/> is the number of copies actually printed when the Windows
/// print dialog was used (the user can change it there); null = the copies requested.</summary>
public sealed record PrintJobResult(bool Succeeded, bool Cancelled, string? PrinterName, string? Error)
{
    public int? CopiesPrinted { get; init; }

    public static PrintJobResult Success(string? printerName, int? copiesPrinted = null) =>
        new(true, false, printerName, null) { CopiesPrinted = copiesPrinted };
    public static PrintJobResult Canceled(string? reason = null) => new(false, true, null, reason);
    public static PrintJobResult Failure(string? error) => new(false, false, null, error);
}
