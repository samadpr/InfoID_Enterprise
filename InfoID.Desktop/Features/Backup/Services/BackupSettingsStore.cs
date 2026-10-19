using System;
using System.IO;
using System.Text.Json;
using InfoID.Infrastructure.Persistence;

namespace InfoID.Desktop.Features.Backup.Services;

/// <summary>This PC's backup schedule and status. Not part of a backup itself: the
/// folder and schedule belong to the PC, not to the data.</summary>
public sealed class BackupSettings
{
    public bool AutoBackupEnabled { get; set; } = true;
    /// <summary>Days between automatic backups (1 = daily).</summary>
    public int IntervalDays { get; set; } = 1;
    /// <summary>Automatic backups kept; older ones are deleted. Manual backups are never deleted.</summary>
    public int KeepCount { get; set; } = 10;
    /// <summary>Null = <see cref="BackupSetup.DefaultBackupFolder"/>.</summary>
    public string? Folder { get; set; }

    /// <summary>Last successful backup of any kind except safety backups.</summary>
    public DateTime? LastBackupUtc { get; set; }
    public string? LastBackupPath { get; set; }
    public DateTime? LastFailureUtc { get; set; }
    public string? LastFailureMessage { get; set; }

    public BackupSettings Clone() => (BackupSettings)MemberwiseClone();
}

/// <summary>
/// BackupSettings.json in InfoID's data folder. Same local-JSON idiom as the other
/// settings stores, plus a Changed event so the Backup page and the Dashboard update when
/// an automatic backup finishes in the background.
/// </summary>
public sealed class BackupSettingsStore
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<BackupSettingsStore>();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly object _gate = new();
    private BackupSettings? _current;

    public BackupSettingsStore()
        : this(Path.Combine(DatabaseLocation.GetApplicationRoot(), "BackupSettings.json"))
    {
    }

    public BackupSettingsStore(string filePath) => _filePath = filePath;

    /// <summary>Raised after every save (on the thread that saved).</summary>
    public event EventHandler? Changed;

    /// <summary>A copy of the current settings.</summary>
    public BackupSettings Current
    {
        get
        {
            lock (_gate)
            {
                _current ??= Load();
                return _current.Clone();
            }
        }
    }

    public string EffectiveFolder
    {
        get
        {
            var folder = Current.Folder;
            return string.IsNullOrWhiteSpace(folder) ? BackupSetup.DefaultBackupFolder : folder;
        }
    }

    /// <summary>Changes the settings and saves them.</summary>
    public void Update(Action<BackupSettings> change)
    {
        lock (_gate)
        {
            var next = (_current ??= Load()).Clone();
            change(next);
            next.IntervalDays = Math.Clamp(next.IntervalDays, 1, 30);
            next.KeepCount = Math.Clamp(next.KeepCount, 1, 100);
            _current = next;
            Save(next);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RecordSuccess(string filePath) => Update(s =>
    {
        s.LastBackupUtc = DateTime.UtcNow;
        s.LastBackupPath = filePath;
        s.LastFailureUtc = null;
        s.LastFailureMessage = null;
    });

    public void RecordFailure(string message) => Update(s =>
    {
        s.LastFailureUtc = DateTime.UtcNow;
        s.LastFailureMessage = message;
    });

    private BackupSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
                return JsonSerializer.Deserialize<BackupSettings>(File.ReadAllText(_filePath), Options) ?? new BackupSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("Backup settings couldn't be read; using defaults", ex);
        }
        return new BackupSettings();
    }

    private void Save(BackupSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Backup settings couldn't be saved", ex);
        }
    }
}
