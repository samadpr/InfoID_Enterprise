using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Desktop.Features.Printing.Models;

namespace InfoID.Desktop.Features.Printing.Services;

/// <summary>Fallback for any OS that isn't Windows/macOS/Linux -- reports honestly that
/// printing isn't available here rather than silently no-op-"succeeding". In practice
/// this should never be selected (PrinterServiceFactory only reaches it if
/// OperatingSystem.IsWindows/IsMacOS/IsLinux are all false), but a fallback that fails
/// loudly and clearly is safer than assuming one of the three always matches.</summary>
public sealed class UnsupportedPrinterService : IPrinterService
{
    public bool IsSupported => false;

    public Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PrinterInfo>>(Array.Empty<PrinterInfo>());

    public Task<bool> PrintPagesAsync(string printerName, IReadOnlyList<string> pageImagePaths, int copies, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task OpenPrinterPreferencesAsync(string printerName) => Task.CompletedTask;

    public Task OpenPrinterManagementAsync() => Task.CompletedTask;
}
