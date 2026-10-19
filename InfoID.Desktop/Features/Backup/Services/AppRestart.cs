using System;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;

namespace InfoID.Desktop.Features.Backup.Services;

/// <summary>
/// Restarts InfoID (to finish a restore). The new instance is told the old one's process
/// id and waits for it to exit before touching any file, so the database is never opened
/// by two processes during the switch.
/// </summary>
public static class AppRestart
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For("InfoID.Restart");
    public const string WaitForProcessArgument = "--wait-for-process";

    /// <summary>Starts a new instance and closes this one. Returns false (and leaves this
    /// instance running) if the new one couldn't be started.</summary>
    public static bool RestartNow()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath)) return false;

            var start = new ProcessStartInfo(processPath) { UseShellExecute = false };
            // Started as "dotnet InfoID.Desktop.dll" (some dev setups): pass the dll again.
            if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(App).Assembly.Location);
            start.ArgumentList.Add(WaitForProcessArgument);
            start.ArgumentList.Add(Environment.ProcessId.ToString());

            Process.Start(start);
            Log.Info("Restarting InfoID to finish the restore.");
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't restart InfoID", ex);
            return false;
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        return true;
    }

    /// <summary>At start-up: if started by <see cref="RestartNow"/>, wait (up to 30 s) for
    /// the previous instance to close.</summary>
    public static void WaitForPreviousInstance(string[] args)
    {
        var index = Array.IndexOf(args, WaitForProcessArgument);
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var pid)) return;
        try
        {
            using var previous = Process.GetProcessById(pid);
            if (!previous.WaitForExit(30_000))
                Log.Warn("The previous InfoID instance is still running after 30 seconds; continuing.");
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (InvalidOperationException)
        {
        }
    }

    public static bool WasRestarted(string[] args) => args.Contains(WaitForProcessArgument);
}
