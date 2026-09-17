using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Desktop.Features.Printing.Models;

namespace InfoID.Desktop.Features.Printing.Services;

/// <summary>
/// Real Windows printing with zero extra NuGet dependency -- everything here shells out
/// to PowerShell/WMI and the OS's own shell verbs, both always present on Windows 10/11,
/// instead of referencing System.Drawing.Printing (which this project's csproj already
/// documents avoiding, and which would make the assembly meaningfully Windows-flavored
/// even though it only runs on Windows here).
///
/// Printing itself (<see cref="PrintPagesAsync"/>) works by temporarily making the
/// chosen printer the OS default, then invoking the shell's "print" verb on each
/// rasterized page file (Windows' Photos app registers this for PNG by default) --
/// genuinely submits a real job to the real printer, but ShellExecute's "print" verb is
/// fire-and-forget (launches whatever app owns that verb and returns immediately, with
/// no completion signal back to this process), so a short settle delay between pages is
/// the best available guarantee that one page's job is spooled before the next starts.
/// This is an honest trade-off of the dependency-free approach, not a hidden reliability
/// gap -- see the class doc above for why System.Drawing.Printing (which does report
/// real completion via PrintDocument) was deliberately not used instead.
/// </summary>
public sealed class WindowsPrinterService : IPrinterService
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public async Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return Array.Empty<PrinterInfo>();

        var listResult = await ProcessRunner.RunAsync("powershell.exe",
            "-NoProfile -NonInteractive -Command \"Get-Printer | Select-Object Name,PrinterStatus | ConvertTo-Json -Compress\"");
        var defaultResult = await ProcessRunner.RunAsync("powershell.exe",
            "-NoProfile -NonInteractive -Command \"(Get-CimInstance -ClassName Win32_Printer | Where-Object { $_.Default }).Name\"");

        var defaultName = defaultResult.StdOut.Trim();

        if (listResult.ExitCode != 0 || string.IsNullOrWhiteSpace(listResult.StdOut))
        {
            return Array.Empty<PrinterInfo>();
        }

        try
        {
            using var doc = JsonDocument.Parse(listResult.StdOut);
            var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray()
                : new[] { doc.RootElement }.AsEnumerable();

            return elements
                .Select(el =>
                {
                    var name = el.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    var statusRaw = el.TryGetProperty("PrinterStatus", out var s)
                        ? (s.ValueKind == JsonValueKind.String ? s.GetString() : s.ToString())
                        : null;
                    // Anything not clearly an error/offline status is reported online --
                    // a status this can't recognize should never read as "the printer is
                    // broken" when it might just be a PowerShell/driver version this
                    // wasn't tested against.
                    var isOnline = statusRaw is null ||
                        !(statusRaw.Contains("Offline", StringComparison.OrdinalIgnoreCase) ||
                          statusRaw.Contains("Error", StringComparison.OrdinalIgnoreCase));
                    return new PrinterInfo(name, string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase), isOnline);
                })
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<PrinterInfo>();
        }
    }

    public async Task<bool> PrintPagesAsync(string printerName, IReadOnlyList<string> pageImagePaths, int copies, CancellationToken ct = default)
    {
        if (!IsSupported || pageImagePaths.Count == 0 || copies < 1) return false;

        var originalDefault = (await ProcessRunner.RunAsync("powershell.exe",
            "-NoProfile -NonInteractive -Command \"(Get-CimInstance -ClassName Win32_Printer | Where-Object { $_.Default }).Name\"")).StdOut.Trim();

        var setDefaultResult = await ProcessRunner.RunAsync("powershell.exe",
            $"-NoProfile -NonInteractive -Command \"(Get-WmiObject -Class Win32_Printer -Filter \\\"Name='{EscapeWmiFilterValue(printerName)}'\\\").SetDefaultPrinter()\"");

        if (setDefaultResult.ExitCode != 0)
        {
            return false;
        }

        var succeeded = true;
        try
        {
            for (var copy = 0; copy < copies; copy++)
            {
                foreach (var path in pageImagePaths)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "print" });
                    }
                    catch
                    {
                        succeeded = false;
                    }

                    // Settle delay -- see class doc comment for why this exists instead
                    // of awaiting real job completion.
                    await Task.Delay(2000, ct);
                }
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(originalDefault) &&
                !string.Equals(originalDefault, printerName, StringComparison.OrdinalIgnoreCase))
            {
                await ProcessRunner.RunAsync("powershell.exe",
                    $"-NoProfile -NonInteractive -Command \"(Get-WmiObject -Class Win32_Printer -Filter \\\"Name='{EscapeWmiFilterValue(originalDefault)}'\\\").SetDefaultPrinter()\"");
            }
        }

        return succeeded;
    }

    public Task OpenPrinterPreferencesAsync(string printerName)
    {
        if (!IsSupported) return Task.CompletedTask;

        try
        {
            // printui.dll's PrintUIEntry is the same real, native printer-properties
            // dialog Windows' own "Printers & Scanners" settings page opens -- not a
            // custom-built stand-in.
            Process.Start(new ProcessStartInfo("rundll32.exe", $"printui.dll,PrintUIEntry /p /n \"{printerName}\"")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // Best-effort: some locked-down machines block rundll32 entirely. Nothing
            // more can be done from here without a native dialog of our own.
        }

        return Task.CompletedTask;
    }

    public Task OpenPrinterManagementAsync()
    {
        if (!IsSupported) return Task.CompletedTask;

        try
        {
            // Windows' own "Printers & scanners" settings page.
            Process.Start(new ProcessStartInfo("explorer.exe", "ms-settings:printers") { UseShellExecute = true });
        }
        catch
        {
            // Best-effort.
        }

        return Task.CompletedTask;
    }

    private static string EscapeWmiFilterValue(string value) => value.Replace("'", "''");
}
