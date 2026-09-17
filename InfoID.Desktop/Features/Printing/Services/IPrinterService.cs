using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Desktop.Features.Printing.Models;

namespace InfoID.Desktop.Features.Printing.Services;

/// <summary>
/// Real OS printer access -- enumeration, submitting an actual print job, and opening
/// the printer's own native preferences/properties surface. Deliberately no dependency
/// on System.Drawing/System.Drawing.Printing (Windows-only, and this project's own
/// InfoID.Desktop.csproj already documents choosing ZXing.Net specifically to avoid that
/// exact dependency): every implementation here talks to the OS's own printing system
/// through the command-line tools each platform already ships with (PowerShell + WMI on
/// Windows, CUPS's lp/lpstat on macOS/Linux), so the app itself stays dependency-free and
/// genuinely cross-platform. See WindowsPrinterService / CupsPrinterService.
/// </summary>
public interface IPrinterService
{
    /// <summary>True on the current OS -- false only means no known real print
    /// mechanism exists on this platform (never used to fake success elsewhere).</summary>
    bool IsSupported { get; }

    Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken ct = default);

    /// <summary>Submits one real print job: <paramref name="pageImagePaths"/> are
    /// already-rasterized PNG files on disk, in the exact order they should print
    /// (e.g. card 1 front, card 1 back, card 2 front, ...); <paramref name="copies"/>
    /// repeats that whole page sequence. Returns false (never throws for an ordinary
    /// failure) if the OS reports the job could not be submitted.</summary>
    Task<bool> PrintPagesAsync(string printerName, IReadOnlyList<string> pageImagePaths, int copies, CancellationToken ct = default);

    /// <summary>Opens the OS's own real printer-properties/preferences surface for the
    /// named printer -- never a custom-built settings screen standing in for it.</summary>
    Task OpenPrinterPreferencesAsync(string printerName);

    /// <summary>Opens the OS's own printer management page (add/remove a printer) --
    /// used by the dialog's "Add printer" action. There is deliberately no matching
    /// "delete this printer" action: automating printer removal (often needing
    /// elevation, and destructive) isn't something this offers -- it opens the same
    /// real OS surface the user would use to remove one themselves.</summary>
    Task OpenPrinterManagementAsync();
}
