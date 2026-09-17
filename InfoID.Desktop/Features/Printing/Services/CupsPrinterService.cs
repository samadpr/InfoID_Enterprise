using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Desktop.Features.Printing.Models;

namespace InfoID.Desktop.Features.Printing.Services;

/// <summary>
/// Real macOS/Linux printing via CUPS's own command-line tools (lpstat/lp) -- both ship
/// with every mainstream desktop Linux distribution and are the actual printing system
/// underneath macOS itself, so this needs no extra install and no NuGet dependency,
/// matching WindowsPrinterService's own zero-dependency approach. Unlike Windows'
/// shell-verb-per-page fallback, CUPS natively understands "one job, multiple pages,
/// N copies" (lp -n &lt;copies&gt; file1 file2 ...), so this is a single real job
/// submission rather than a loop of best-effort shell verbs.
/// </summary>
public sealed class CupsPrinterService : IPrinterService
{
    public bool IsSupported => OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

    public async Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return Array.Empty<PrinterInfo>();

        var printersResult = await ProcessRunner.RunAsync("lpstat", "-p");
        var defaultResult = await ProcessRunner.RunAsync("lpstat", "-d");

        if (printersResult.ExitCode != 0)
        {
            // Most common real cause: CUPS/cups-client isn't installed on this machine.
            return Array.Empty<PrinterInfo>();
        }

        var defaultMatch = Regex.Match(defaultResult.StdOut, @"destination:\s*(\S+)");
        var defaultName = defaultMatch.Success ? defaultMatch.Groups[1].Value : null;

        var printers = new List<PrinterInfo>();
        foreach (var line in printersResult.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Typical lpstat -p line: "printer HP_LaserJet is idle.  enabled since ..."
            // or "printer HP_LaserJet disabled since ... -"
            var match = Regex.Match(line, @"^printer\s+(\S+)\s+(.*)$");
            if (!match.Success) continue;

            var name = match.Groups[1].Value;
            var statusText = match.Groups[2].Value;
            var isOnline = !statusText.Contains("disabled", StringComparison.OrdinalIgnoreCase);

            printers.Add(new PrinterInfo(name, string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase), isOnline));
        }

        return printers;
    }

    public async Task<bool> PrintPagesAsync(string printerName, IReadOnlyList<string> pageImagePaths, int copies, CancellationToken ct = default)
    {
        if (!IsSupported || pageImagePaths.Count == 0 || copies < 1) return false;

        var args = new StringBuilder();
        args.Append("-d ").Append(Quote(printerName));
        args.Append(" -n ").Append(copies);
        foreach (var path in pageImagePaths)
        {
            args.Append(' ').Append(Quote(path));
        }

        var result = await ProcessRunner.RunAsync("lp", args.ToString());
        return result.ExitCode == 0;
    }

    public Task OpenPrinterPreferencesAsync(string printerName)
    {
        if (!IsSupported) return Task.CompletedTask;

        try
        {
            if (OperatingSystem.IsMacOS())
            {
                // macOS's own "Printers & Scanners" settings pane -- CUPS's web admin
                // UI is disabled by default on macOS, so this is the reliable native
                // route rather than assuming it's been turned on.
                Process.Start(new ProcessStartInfo("open", "x-apple.systempreferences:com.apple.preference.printfax") { UseShellExecute = true });
            }
            else
            {
                // CUPS's own real web admin page for this exact printer -- enabled by
                // default on virtually every desktop Linux distribution's CUPS install.
                Process.Start(new ProcessStartInfo("xdg-open", $"http://localhost:631/printers/{Uri.EscapeDataString(printerName)}") { UseShellExecute = true });
            }
        }
        catch
        {
            // Best-effort -- a headless/minimal install may have neither tool.
        }

        return Task.CompletedTask;
    }

    public Task OpenPrinterManagementAsync()
    {
        if (!IsSupported) return Task.CompletedTask;

        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open", "x-apple.systempreferences:com.apple.preference.printfax") { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("xdg-open", "http://localhost:631/admin") { UseShellExecute = true });
            }
        }
        catch
        {
            // Best-effort.
        }

        return Task.CompletedTask;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
