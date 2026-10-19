using System;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Infrastructure.Backup;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Backup.Services;

/// <summary>
/// Runs backups -- the automatic schedule, and manual "Back up now" through the same
/// path -- and keeps <see cref="BackupSettingsStore"/> up to date (last backup, last
/// failure), so the Backup page and the Dashboard always agree.
///
/// Automatic backups: checked two minutes after start-up and then every 30 minutes while
/// InfoID is open. A backup is due when the last successful one (manual or automatic) is
/// older than the chosen interval. The backup itself runs on a background thread
/// (SQLite's online backup API), so InfoID stays usable, including printing.
/// </summary>
public sealed class AutoBackupScheduler : IDisposable
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<AutoBackupScheduler>();

    private readonly IServiceScopeFactory _scopes;
    private readonly BackupSettingsStore _settings;
    private Timer? _timer;
    private int _autoRunning;

    public AutoBackupScheduler(IServiceScopeFactory scopes, BackupSettingsStore settings)
    {
        _scopes = scopes;
        _settings = settings;
    }

    /// <summary>True while any backup started through this class is running.</summary>
    public bool IsRunning => Volatile.Read(ref _running) > 0;
    private int _running;

    /// <summary>Raised (on a background thread) when a backup finishes, either way.</summary>
    public event EventHandler<BackupResult>? BackupFinished;

    public void Start()
    {
        _timer ??= new Timer(_ => _ = RunIfDueAsync(), null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(30));
        Log.Info("Automatic backup schedule started.");
    }

    public bool IsDue(DateTime utcNow)
    {
        var s = _settings.Current;
        if (!s.AutoBackupEnabled) return false;
        if (s.LastBackupUtc is not { } last) return true;
        // An hour of slack, so "daily" doesn't slip a day when InfoID is opened a little
        // earlier than yesterday's backup time.
        return utcNow - last >= TimeSpan.FromDays(s.IntervalDays) - TimeSpan.FromHours(1);
    }

    private async Task RunIfDueAsync()
    {
        if (Interlocked.Exchange(ref _autoRunning, 1) == 1) return;
        try
        {
            if (!IsDue(DateTime.UtcNow)) return;
            using (var scope = _scopes.CreateScope())
            {
                if (scope.ServiceProvider.GetRequiredService<IBackupService>().IsRestorePending)
                    return; // the data is about to be replaced; back up after the restore instead
            }

            var folder = _settings.EffectiveFolder;
            var path = BackupFileNames.CreateUniquePath(EnsureFolder(folder), DateTime.Now, BackupKind.Automatic);
            Log.Info("Automatic backup is due; starting.");
            await RunAsync(new BackupRequest { DestinationPath = path, Kind = BackupKind.Automatic });
        }
        catch (Exception ex)
        {
            Log.Error("Automatic backup failed to start", ex);
            _settings.RecordFailure(ex.GetBaseException().Message);
        }
        finally
        {
            Interlocked.Exchange(ref _autoRunning, 0);
        }
    }

    /// <summary>Runs one backup, records the outcome, and prunes old automatic backups
    /// in the backup folder.</summary>
    public async Task<BackupResult> RunAsync(BackupRequest request, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _running);
        BackupResult result;
        try
        {
            using var scope = _scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
            result = await service.CreateBackupAsync(request, progress, ct);

            if (result.Success)
            {
                _settings.RecordSuccess(result.FilePath!);
                var settings = _settings.Current;
                var folder = _settings.EffectiveFolder;
                // Only the backup folder is pruned, and only its automatic/safety backups.
                if (string.Equals(System.IO.Path.GetDirectoryName(result.FilePath), System.IO.Path.GetFullPath(folder).TrimEnd('\\', '/'),
                        StringComparison.OrdinalIgnoreCase))
                    service.PruneBackups(folder, settings.KeepCount);
            }
            else
            {
                _settings.RecordFailure(result.ErrorMessage ?? "The backup failed.");
            }
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }

        try { BackupFinished?.Invoke(this, result); }
        catch (Exception ex) { Log.Warn("A backup listener failed", ex); }
        return result;
    }

    private static string EnsureFolder(string folder)
    {
        System.IO.Directory.CreateDirectory(folder);
        return folder;
    }

    public void Dispose() => _timer?.Dispose();
}
