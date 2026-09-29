using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Desktop.Features.Printing.Models;

namespace InfoID.Desktop.Features.Printing.Services;

/// <summary>
/// Real Windows printing with zero extra NuGet dependency -- everything here shells out
/// to PowerShell/WMI, which is always present on Windows 10/11, instead of referencing
/// System.Drawing.Printing from this assembly (which this project's csproj already
/// documents avoiding).
///
/// Printing runs a small Windows PowerShell script that uses the OS's own .NET
/// Framework System.Drawing.Printing.PrintDocument to send ALL pages to the printer as
/// ONE print job. <see cref="PrintPagesWithSystemDialogAsync"/> first shows the standard
/// Windows print dialog (printer, Preferences, copies); <see cref="PrintPagesAsync"/>
/// prints straight to the given printer. The default printer is never changed, and the
/// call only returns once the job has been spooled, with a real result.
///
/// Bug fix: this previously invoked the shell's "print" verb on every page PNG, which
/// opens Windows' "Print Pictures" window (Photos) for each page, showing every card
/// and waiting for the user, and it temporarily switched the system default printer.
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

    public bool SupportsSystemPrintDialog => IsSupported;

    public async Task<bool> PrintPagesAsync(string printerName, IReadOnlyList<string> pageImagePaths, int copies, CancellationToken ct = default) =>
        (await RunPrintJobAsync(printerName, pageImagePaths, copies, showDialog: false, ct)).Succeeded;

    public Task<PrintJobResult> PrintPagesWithSystemDialogAsync(
        string printerName, IReadOnlyList<string> pageImagePaths, int copies, CancellationToken ct = default) =>
        RunPrintJobAsync(printerName, pageImagePaths, copies, showDialog: true, ct);

    private async Task<PrintJobResult> RunPrintJobAsync(
        string printerName, IReadOnlyList<string> pageImagePaths, int copies, bool showDialog, CancellationToken ct)
    {
        if (!IsSupported) return PrintJobResult.Failure("Printing isn't supported on this system.");
        if (pageImagePaths.Count == 0) return PrintJobResult.Failure("There are no pages to print.");
        if (copies < 1) copies = 1;

        var workDir = Path.Combine(Path.GetTempPath(), "InfoID_PrintJob_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            var scriptPath = Path.Combine(workDir, "print.ps1");
            var jobPath = Path.Combine(workDir, "job.txt");

            // Everything variable (printer name, file paths) goes through a plain text
            // file instead of the command line, so names with spaces, quotes or
            // non-ASCII characters can never break PowerShell argument quoting.
            var jobLines = new List<string>
            {
                printerName ?? string.Empty,
                copies.ToString(CultureInfo.InvariantCulture),
                CardRasterizer.DefaultPrintDpi.ToString(CultureInfo.InvariantCulture),
                "InfoID Cards",
                showDialog ? "dialog" : "direct",
            };
            jobLines.AddRange(pageImagePaths);

            await File.WriteAllTextAsync(scriptPath, PrintScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
            await File.WriteAllLinesAsync(jobPath, jobLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);

            ct.ThrowIfCancellationRequested();

            // -STA: the Windows print dialog (WinForms) requires a single-threaded apartment.
            var result = await ProcessRunner.RunAsync("powershell.exe",
                $"-NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File \"{scriptPath}\" -JobFile \"{jobPath}\"");

            if (result.ExitCode == CancelledExitCode)
                return PrintJobResult.Canceled();

            if (result.ExitCode != 0)
            {
                var error = result.StdErr.Trim();
                Debug.WriteLine($"PRINT FAILED (exit {result.ExitCode}): {error} {result.StdOut.Trim()}");
                return PrintJobResult.Failure(string.IsNullOrWhiteSpace(error) ? null : error);
            }

            // The script reports which printer the job really went to (the user may
            // have picked another one in the Windows dialog).
            var usedPrinter = result.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith("PRINTER:", StringComparison.Ordinal))
                .Select(line => line["PRINTER:".Length..].Trim())
                .LastOrDefault();

            return PrintJobResult.Success(string.IsNullOrWhiteSpace(usedPrinter) ? printerName : usedPrinter);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort cleanup only */ }
        }
    }

    /// <summary>Exit code the script uses when the user closes the Windows print dialog
    /// without printing.</summary>
    private const int CancelledExitCode = 4;

    /// <summary>
    /// Windows PowerShell 5.1 script (always present on Windows 10/11) that prints the
    /// page images as one silent job through System.Drawing.Printing.PrintDocument.
    ///
    /// Page placement:
    ///  - Card printers (paper size roughly the card size, e.g. CR-80): the card fills
    ///    the whole page edge to edge, and page orientation follows the card.
    ///  - Ordinary printers (A4/Letter): the card prints at its true physical size,
    ///    centered, so it can be cut out or used as a proof. Physical size is derived
    ///    from the page image's pixels at the DPI CardRasterizer renders at.
    /// Copies are collated (card 1 front/back, card 2 front/back, ..., then repeat).
    /// </summary>
    private const string PrintScript = """
        param([Parameter(Mandatory = $true)][string]$JobFile)
        $ErrorActionPreference = 'Stop'

        try {
            Add-Type -AssemblyName System.Drawing

            $lines = [System.IO.File]::ReadAllLines($JobFile, [System.Text.Encoding]::UTF8)
            $printerName = $lines[0]
            $copies = [int]$lines[1]
            $dpi = [double]::Parse($lines[2], [System.Globalization.CultureInfo]::InvariantCulture)
            $docName = $lines[3]
            $showDialog = ($lines[4] -eq 'dialog')
            $pages = @($lines | Select-Object -Skip 5 | Where-Object { $_ -and $_.Trim().Length -gt 0 })

            if ($pages.Count -eq 0) { [Console]::Error.WriteLine('No pages to print.'); exit 3 }

            $doc = New-Object System.Drawing.Printing.PrintDocument
            $doc.DocumentName = $docName
            if ($printerName) { $doc.PrinterSettings.PrinterName = $printerName }

            if ($showDialog) {
                # The standard Windows print dialog: printer choice, Preferences button
                # (the driver's own settings), copies. Pre-selected to InfoID's choice.
                Add-Type -AssemblyName System.Windows.Forms
                [System.Windows.Forms.Application]::EnableVisualStyles()

                if ($doc.PrinterSettings.IsValid) { $doc.PrinterSettings.Copies = [int16][Math]::Min($copies, 999) }

                $dlg = New-Object System.Windows.Forms.PrintDialog
                $dlg.Document = $doc
                $dlg.UseEXDialog = $true
                # Page range ("Pages: 1-4") in the Windows dialog, counted over the
                # pages InfoID generated for this run (card 1 front = page 1, ...).
                $dlg.AllowSomePages = $true
                $doc.PrinterSettings.MinimumPage = 1
                $doc.PrinterSettings.MaximumPage = $pages.Count
                $doc.PrinterSettings.FromPage = 1
                $doc.PrinterSettings.ToPage = $pages.Count
                $dlg.AllowSelection = $false
                $dlg.AllowCurrentPage = $false

                # Invisible topmost owner so the dialog opens in front of InfoID
                # instead of behind it (this script runs in a background process).
                $owner = New-Object System.Windows.Forms.Form
                $owner.TopMost = $true
                $owner.ShowInTaskbar = $false
                $owner.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
                $owner.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
                $owner.Size = New-Object System.Drawing.Size(1, 1)
                $owner.Opacity = 0
                $owner.Show()
                $owner.Activate()

                $answer = $dlg.ShowDialog($owner)
                $owner.Close()
                $owner.Dispose()

                if ($answer -ne [System.Windows.Forms.DialogResult]::OK) { exit 4 }

                # Copies are produced by repeating the pages below (collated), so the
                # driver itself must print each page once.
                $copies = [Math]::Max(1, [int]$doc.PrinterSettings.Copies)
                $doc.PrinterSettings.Copies = 1

                if ($doc.PrinterSettings.PrintRange -eq [System.Drawing.Printing.PrintRange]::SomePages) {
                    $from = [Math]::Max(1, $doc.PrinterSettings.FromPage)
                    $to = [Math]::Min($pages.Count, $doc.PrinterSettings.ToPage)
                    if ($to -lt $from) { [Console]::Error.WriteLine('The page range is empty.'); exit 3 }
                    $pages = @($pages[($from - 1)..($to - 1)])
                    $doc.PrinterSettings.PrintRange = [System.Drawing.Printing.PrintRange]::AllPages
                }
            }

            if (-not $doc.PrinterSettings.IsValid) {
                [Console]::Error.WriteLine("Printer not found: $($doc.PrinterSettings.PrinterName)")
                exit 2
            }

            $script:queue = New-Object 'System.Collections.Generic.List[string]'
            for ($c = 0; $c -lt $copies; $c++) { foreach ($p in $pages) { $script:queue.Add($p) } }
            $script:index = 0
            $script:dpi = $dpi

            # No "Printing page x of y" progress window.
            $doc.PrintController = New-Object System.Drawing.Printing.StandardPrintController
            $doc.OriginAtMargins = $false

            # Card size in 1/100 inch (the unit PrintDocument uses), from the first page.
            $first = [System.Drawing.Image]::FromFile($pages[0])
            try {
                $cardW = $first.Width / $dpi * 100.0
                $cardH = $first.Height / $dpi * 100.0
            } finally { $first.Dispose() }

            $paper = $doc.DefaultPageSettings.PaperSize
            $paperShort = [Math]::Min($paper.Width, $paper.Height)
            $paperLong = [Math]::Max($paper.Width, $paper.Height)
            $cardShort = [Math]::Min($cardW, $cardH)
            $cardLong = [Math]::Max($cardW, $cardH)

            # Within 15% on both sides = the printer's paper IS a card (ID card printer).
            $script:fillPage = ($paperShort -gt 0) -and
                ([Math]::Abs($paperShort - $cardShort) / $cardShort -lt 0.15) -and
                ([Math]::Abs($paperLong - $cardLong) / $cardLong -lt 0.15)

            if ($script:fillPage) {
                $doc.DefaultPageSettings.Landscape = ($cardW -gt $cardH)
                $doc.DefaultPageSettings.Margins = New-Object System.Drawing.Printing.Margins(0, 0, 0, 0)
            }

            $doc.add_PrintPage({
                param($sender, $e)
                $img = [System.Drawing.Image]::FromFile($script:queue[$script:index])
                try {
                    $g = $e.Graphics
                    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

                    $page = $e.PageBounds
                    # Graphics origin is the printable-area corner; shift back by the
                    # hard margins so coordinates are relative to the physical page.
                    $hx = $e.PageSettings.HardMarginX
                    $hy = $e.PageSettings.HardMarginY

                    if ($script:fillPage) {
                        $rect = New-Object System.Drawing.RectangleF((-$hx), (-$hy), $page.Width, $page.Height)
                    } else {
                        $w = $img.Width / $script:dpi * 100.0
                        $h = $img.Height / $script:dpi * 100.0
                        $scale = [Math]::Min(1.0, [Math]::Min($page.Width / $w, $page.Height / $h))
                        $w = $w * $scale
                        $h = $h * $scale
                        $x = ($page.Width - $w) / 2.0 - $hx
                        $y = ($page.Height - $h) / 2.0 - $hy
                        $rect = New-Object System.Drawing.RectangleF($x, $y, $w, $h)
                    }

                    $g.DrawImage($img, $rect)
                } finally {
                    $img.Dispose()
                }

                $script:index++
                $e.HasMorePages = ($script:index -lt $script:queue.Count)
            })

            $doc.Print()
            [Console]::Out.WriteLine("PRINTER:" + $doc.PrinterSettings.PrinterName)
            $doc.Dispose()
            exit 0
        }
        catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 1
        }
        """;

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

}
