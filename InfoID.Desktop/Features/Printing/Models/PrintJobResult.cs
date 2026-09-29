namespace InfoID.Desktop.Features.Printing.Models;

/// <summary>Outcome of one print submission. <see cref="Cancelled"/> means the user
/// closed the Windows print dialog without printing, which is not a failure.
/// <see cref="PrinterName"/> is the printer the job actually went to (the user can pick
/// a different one in the Windows print dialog). <see cref="Error"/> is the OS's own
/// message when submission failed, shown to the user as-is.</summary>
public sealed record PrintJobResult(bool Succeeded, bool Cancelled, string? PrinterName, string? Error)
{
    public static PrintJobResult Success(string? printerName) => new(true, false, printerName, null);
    public static PrintJobResult Canceled() => new(false, true, null, null);
    public static PrintJobResult Failure(string? error) => new(false, false, null, error);
}
