using System.Diagnostics;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.Printing.Services;

/// <summary>Tiny shared helper for running an external command and capturing its output
/// -- both platform printer services shell out to OS-provided tools (PowerShell/WMI,
/// CUPS) rather than a native binding, so this one helper is the single place that
/// actually spawns a process.</summary>
internal static class ProcessRunner
{
    public readonly record struct Result(int ExitCode, string StdOut, string StdErr);

    public static async Task<Result> RunAsync(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new Result(process.ExitCode, await stdOutTask, await stdErrTask);
        }
        catch (System.Exception ex)
        {
            // Most commonly: the tool itself isn't installed on this machine (e.g. no
            // PowerShell, no CUPS) -- reported as a failed result, not a crash.
            return new Result(-1, string.Empty, ex.Message);
        }
    }
}
