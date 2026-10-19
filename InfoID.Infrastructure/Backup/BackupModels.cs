using System.Text.Json.Serialization;

namespace InfoID.Infrastructure.Backup;

/// <summary>Why a backup was made. Part of the file name, so the backup list can tell
/// them apart without opening (or decrypting) each file.</summary>
public enum BackupKind
{
    /// <summary>"Back up now" / "Save backup as".</summary>
    Manual,
    /// <summary>Made by the automatic backup schedule; pruned to the "keep last N" setting.</summary>
    Automatic,
    /// <summary>Made just before a restore, so the restore itself can be undone.</summary>
    SafetyBeforeRestore,
}

public enum BackupSourceKind
{
    /// <summary>A SQLite database, copied with SQLite's online backup API, so the copy is
    /// consistent even while InfoID is using the database.</summary>
    SqliteDatabase,
    /// <summary>A folder, recursively (design images, thumbnails).</summary>
    Folder,
    /// <summary>A single file (a settings JSON).</summary>
    File,
}

public enum RestoreMode
{
    /// <summary>The restored copy replaces what's on this PC.</summary>
    Replace,
    /// <summary>Folders only: restored files are added/overwritten, files that exist only
    /// on this PC are kept. Used for design images, whose names are unique (GUIDs), so
    /// merging can never mix up two different images.</summary>
    Merge,
}

/// <summary>
/// One thing that goes into a backup. The Desktop app supplies the list (it knows where
/// its settings files and the Internal Database live); this engine only copies them.
/// </summary>
/// <param name="ArchivePath">Path inside the backup, '/'-separated (e.g. "database/InfoID_DB.db", "assets").</param>
/// <param name="Kind">How it is copied.</param>
/// <param name="LocalPath">Where it lives on this PC.</param>
/// <param name="RestoreMode">Replace or merge on restore.</param>
/// <param name="Required">A backup must contain it to be restorable (the main database).</param>
/// <param name="Description">Shown in the restore summary, e.g. "Design images".</param>
public sealed record BackupSource(
    string ArchivePath,
    BackupSourceKind Kind,
    string LocalPath,
    RestoreMode RestoreMode = RestoreMode.Replace,
    bool Required = false,
    string? Description = null);

/// <summary>Everything the engine needs to know about this installation.</summary>
public sealed class BackupOptions
{
    /// <summary>What a backup contains. The first SqliteDatabase source marked Required is
    /// the main InfoID database.</summary>
    public IReadOnlyList<BackupSource> Sources { get; init; } = Array.Empty<BackupSource>();

    /// <summary>Where the restore is prepared before InfoID restarts
    /// (%AppData%\InfoID\RestoreStaging).</summary>
    public string StagingDirectory { get; init; } = string.Empty;

    /// <summary>The application version written into each backup.</summary>
    public string AppVersion { get; init; } = "0.0.0";

    public BackupSource MainDatabase =>
        Sources.FirstOrDefault(s => s.Kind == BackupSourceKind.SqliteDatabase && s.Required)
        ?? throw new InvalidOperationException("No main database is configured for backups.");
}

/// <summary>Row counts shown in the restore summary, read from the backed-up database
/// itself (not the live one), so they describe exactly what a restore brings back.</summary>
public sealed class BackupCounts
{
    public int Designs { get; set; }
    public int Cardholders { get; set; }
    public int Cards { get; set; }
    public int PrintJobs { get; set; }
}

public sealed class BackupManifestEntry
{
    /// <summary>'/'-separated path inside the backup.</summary>
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    /// <summary>Lower-case hex SHA-256, checked before anything is restored.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>manifest.json at the root of every backup.</summary>
public sealed class BackupManifest
{
    public const int CurrentFormatVersion = 1;
    public const string FileName = "manifest.json";

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Product { get; set; } = "InfoID";
    public string AppVersion { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public string MachineName { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BackupKind Kind { get; set; }
    /// <summary>Newest EF Core migration in the backed-up database. Decides whether this
    /// version of InfoID can restore it (it must know that migration).</summary>
    public string? SchemaVersion { get; set; }
    public BackupCounts Counts { get; set; } = new();
    /// <summary>Top-level parts included (archive paths of the sources that existed).</summary>
    public List<string> Parts { get; set; } = new();
    public List<BackupManifestEntry> Entries { get; set; } = new();
}

/// <summary>A backup file found in the backup folder.</summary>
public sealed class BackupFileInfo
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public DateTime CreatedLocal { get; init; }
    public long SizeBytes { get; init; }
    public BackupKind Kind { get; init; }
    public bool IsEncrypted { get; init; }
}

public sealed class BackupRequest
{
    /// <summary>Full path of the .infoidbak file to create. It is written to a temporary
    /// file first and only renamed into place once complete.</summary>
    public string DestinationPath { get; init; } = string.Empty;
    public BackupKind Kind { get; init; } = BackupKind.Manual;
    /// <summary>Optional: encrypts the backup (AES-256). Null or empty = not encrypted.</summary>
    public string? Password { get; init; }
}

public sealed class BackupResult
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public long SizeBytes { get; init; }
    public BackupManifest? Manifest { get; init; }
    public string? ErrorMessage { get; init; }

    public static BackupResult Failed(string message) => new() { Success = false, ErrorMessage = message };
}

/// <summary>Progress for the UI: a short description and 0..1.</summary>
public readonly record struct BackupProgress(string Step, double Fraction);

/// <summary>Result of checking a backup file before restoring it.</summary>
public sealed class BackupInspection
{
    public string FilePath { get; init; } = string.Empty;
    public bool IsEncrypted { get; init; }
    /// <summary>Encrypted and no password (or the wrong one) given yet.</summary>
    public bool NeedsPassword { get; init; }
    public bool WrongPassword { get; init; }
    public BackupManifest? Manifest { get; init; }
    /// <summary>The file is intact, complete and can be restored by this version.</summary>
    public bool CanRestore { get; init; }
    /// <summary>Why it can't be restored (null when it can).</summary>
    public string? Problem { get; init; }
}

public sealed class RestoreStageResult
{
    public bool Success { get; init; }
    public string? SafetyBackupPath { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>Written to RestoreStaging\pending.json once a restore is fully prepared;
/// picked up on the next start before the database is opened.</summary>
public sealed class PendingRestore
{
    public const string FileName = "pending.json";
    public string SourceFile { get; set; } = string.Empty;
    public string? SafetyBackupPath { get; set; }
    public DateTime StagedUtc { get; set; }
    public DateTime BackupCreatedUtc { get; set; }
    public string? BackupAppVersion { get; set; }
}
