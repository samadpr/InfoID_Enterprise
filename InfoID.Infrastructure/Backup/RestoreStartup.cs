using System.Text.Json;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.SystemModule;
using InfoID.Shared.Logging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Infrastructure.Backup;

/// <summary>
/// Finishes a restore prepared by <see cref="BackupService.StageRestoreAsync"/>. Runs at
/// startup, before anything opens the database or reads a settings file, so there are no
/// open files and no in-memory copies of the old data to get out of step.
///
/// Order (App.axaml.cs):
///   1. <see cref="ApplyPending"/> -- the current files are moved aside (RestoreStaging\rollback)
///      and the restored ones put in place. Any failure puts everything back.
///   2. The normal database initialization runs: it upgrades the restored database to this
///      version's schema (migrations).
///   3a. Success: <see cref="Complete"/> -- this PC's license is copied back in (a restore
///       never changes the license or the cards-issued counter), the restore is recorded,
///       and the temporary files are removed.
///   3b. Migration failed: <see cref="Rollback"/> puts the previous data back and the app
///       initializes that instead; <see cref="RecordFailure"/> records it.
/// </summary>
public static class RestoreStartup
{
    public const string ContentFolderName = "content";
    public const string RollbackFolderName = "rollback";
    private static readonly AppLogger Log = AppLog.For("InfoID.Restore");

    public sealed class Outcome
    {
        /// <summary>A prepared restore was found.</summary>
        public bool Attempted { get; init; }
        /// <summary>The restored files are in place (not yet confirmed by migration).</summary>
        public bool Applied { get; internal set; }
        /// <summary>Why the restore didn't happen (the previous data is in place).</summary>
        public string? Error { get; internal set; }
        /// <summary>Restored, but with something the user should know.</summary>
        public string? Warning { get; internal set; }
        public PendingRestore? Pending { get; init; }
        public bool Succeeded => Applied && Error is null;

        internal List<(string Description, Action Undo)> UndoSteps { get; } = new();
        internal string? PreviousMainDatabase { get; set; }
        internal string StagingDirectory { get; init; } = string.Empty;
    }

    public static Outcome ApplyPending(BackupOptions options)
    {
        var staging = options.StagingDirectory;
        var pendingPath = Path.Combine(staging, PendingRestore.FileName);
        if (!File.Exists(pendingPath)) return new Outcome { Attempted = false, StagingDirectory = staging };

        PendingRestore? pending = null;
        try
        {
            pending = JsonSerializer.Deserialize<PendingRestore>(File.ReadAllText(pendingPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Log.Warn("The prepared restore's pending.json couldn't be read", ex);
        }

        var outcome = new Outcome { Attempted = true, Pending = pending, StagingDirectory = staging };
        var content = Path.Combine(staging, ContentFolderName);
        var rollback = Path.Combine(staging, RollbackFolderName);
        Log.Info($"Applying the prepared restore from {pending?.SourceFile ?? "(unknown file)"}");

        try
        {
            if (pending is null) throw new InvalidDataException("The prepared restore is incomplete.");
            var main = options.MainDatabase;
            if (!File.Exists(StagedPath(content, main)))
                throw new InvalidDataException("The prepared restore is incomplete (the database is missing).");

            DeleteDirectory(rollback);
            Directory.CreateDirectory(rollback);

            foreach (var source in options.Sources)
            {
                var staged = StagedPath(content, source);
                var saved = Path.Combine(rollback, source.ArchivePath.Replace('/', Path.DirectorySeparatorChar));
                var local = source.LocalPath;

                switch (source.Kind)
                {
                    case BackupSourceKind.SqliteDatabase:
                    case BackupSourceKind.File:
                    {
                        // Not in the backup: what's on this PC stays as it is.
                        if (!File.Exists(staged)) continue;
                        var isDatabase = source.Kind == BackupSourceKind.SqliteDatabase;
                        var had = File.Exists(local);
                        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                        if (had) MoveFileWithSidecars(local, saved, isDatabase);
                        else DeleteSidecars(local, isDatabase);
                        if (ReferenceEquals(source, main)) outcome.PreviousMainDatabase = had ? saved : null;

                        outcome.UndoSteps.Add((source.ArchivePath, () =>
                        {
                            DeleteFileWithSidecars(local, isDatabase);
                            if (had) MoveFileWithSidecars(saved, local, isDatabase);
                        }));
                        File.Copy(staged, local, overwrite: true);
                        break;
                    }

                    case BackupSourceKind.Folder:
                    {
                        if (!Directory.Exists(staged)) continue;
                        var had = Directory.Exists(local);
                        if (source.RestoreMode == RestoreMode.Merge)
                        {
                            // Keep an exact copy of the current folder so a rollback is exact too.
                            if (had) CopyDirectory(local, saved);
                        }
                        else if (had)
                        {
                            MoveDirectory(local, saved);
                        }

                        outcome.UndoSteps.Add((source.ArchivePath, () =>
                        {
                            DeleteDirectory(local);
                            if (had) MoveDirectory(saved, local);
                        }));
                        CopyDirectory(staged, local);
                        break;
                    }
                }
            }

            outcome.Applied = true;
            Log.Info("Restored files are in place; the database is upgraded next.");
        }
        catch (Exception ex)
        {
            Log.Error("Applying the restore failed; putting the previous data back", ex);
            RunUndo(outcome);
            outcome.Applied = false;
            outcome.Error = $"The backup couldn't be restored ({BackupService.Friendly(ex)}). Your data was not changed.";
            BackupService.TryDeleteDirectory(staging);
        }

        return outcome;
    }

    /// <summary>The restored database couldn't be upgraded (or opened): put the previous
    /// data back. Call before initializing the database again.</summary>
    public static void Rollback(Outcome outcome, string reason)
    {
        if (!outcome.Applied) return;
        Log.Error($"Rolling back the restore: {reason}");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        RunUndo(outcome);
        outcome.Applied = false;
        outcome.Error = $"The backup couldn't be restored: {reason} Your previous data was put back.";
        BackupService.TryDeleteDirectory(outcome.StagingDirectory);
    }

    /// <summary>After the restored database has been upgraded successfully.</summary>
    public static void Complete(Outcome outcome, InfoIdDbContext context)
    {
        if (!outcome.Succeeded) return;

        try
        {
            KeepThisPcsLicense(context, outcome.PreviousMainDatabase);
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't carry this PC's license over into the restored database", ex);
            outcome.Warning = "This PC's license couldn't be carried over into the restored data. If InfoID asks, activate it again.";
        }

        try
        {
            context.BackupRestoreLogs.Add(new BackupRestoreLog
            {
                OperationType = BackupOperationType.Restore,
                Status = BackupStatus.Success,
                FilePath = Truncate(outcome.Pending?.SourceFile),
                StartedDate = outcome.Pending?.StagedUtc ?? DateTime.UtcNow,
                CompletedDate = DateTime.UtcNow,
            });
            context.SaveChanges();
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't record the restore in the history", ex);
        }

        BackupService.TryDeleteDirectory(outcome.StagingDirectory);
        Log.Info($"Restore completed from {outcome.Pending?.SourceFile} (backup of {outcome.Pending?.BackupCreatedUtc:u}).");
    }

    /// <summary>Records a restore that didn't happen, into the (previous) database.</summary>
    public static void RecordFailure(Outcome outcome, InfoIdDbContext context)
    {
        if (!outcome.Attempted || outcome.Succeeded) return;
        try
        {
            context.BackupRestoreLogs.Add(new BackupRestoreLog
            {
                OperationType = BackupOperationType.Restore,
                Status = BackupStatus.Failed,
                FilePath = Truncate(outcome.Pending?.SourceFile),
                StartedDate = outcome.Pending?.StagedUtc ?? DateTime.UtcNow,
                CompletedDate = DateTime.UtcNow,
            });
            context.SaveChanges();
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't record the failed restore in the history", ex);
        }
    }

    /// <summary>
    /// The license and its cards-issued counter belong to this PC, not to the backup:
    /// restoring an old backup must not bring back an old or deactivated license, reset
    /// the card counter, or copy another computer's license onto this one. So the license
    /// tables of the restored database are replaced with this PC's own (or emptied, on a
    /// PC that had none -- InfoID then asks for activation as usual).
    /// </summary>
    private static void KeepThisPcsLicense(InfoIdDbContext context, string? previousDatabase)
    {
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen) connection.Open();
        var attached = false;
        try
        {
            Execute(connection, null, "PRAGMA foreign_keys = OFF");
            if (previousDatabase is not null && File.Exists(previousDatabase))
            {
                using var attach = connection.CreateCommand();
                attach.CommandText = "ATTACH DATABASE $path AS previous";
                attach.Parameters.AddWithValue("$path", previousDatabase);
                attach.ExecuteNonQuery();
                attached = true;
            }

            var copied = 0;
            using (var transaction = connection.BeginTransaction())
            {
                Execute(connection, transaction, "DELETE FROM \"LicenseValidationLog\"");
                Execute(connection, transaction, "DELETE FROM \"LicenseRecord\"");

                if (attached && SqliteBackupHelpers.TableExists(connection, "LicenseRecord", "previous"))
                {
                    if (SqliteBackupHelpers.TableExists(connection, "Organization", "previous"))
                    {
                        var orgColumns = SharedColumns(connection, "Organization");
                        Execute(connection, transaction,
                            $"INSERT OR IGNORE INTO \"Organization\" ({orgColumns}) SELECT {orgColumns} FROM previous.\"Organization\" " +
                            "WHERE Id IN (SELECT OrganizationId FROM previous.\"LicenseRecord\")");
                    }

                    var licenseColumns = SharedColumns(connection, "LicenseRecord");
                    copied = Execute(connection, transaction,
                        $"INSERT INTO \"LicenseRecord\" ({licenseColumns}) SELECT {licenseColumns} FROM previous.\"LicenseRecord\"");

                    if (SqliteBackupHelpers.TableExists(connection, "LicenseValidationLog", "previous"))
                    {
                        var logColumns = SharedColumns(connection, "LicenseValidationLog");
                        Execute(connection, transaction,
                            $"INSERT INTO \"LicenseValidationLog\" ({logColumns}) SELECT {logColumns} FROM previous.\"LicenseValidationLog\"");
                    }
                }

                transaction.Commit();
            }

            Log.Info(copied > 0
                ? $"Kept this PC's license in the restored data ({copied} license record(s))."
                : "This PC had no license; the restored data has none either (activation is asked for as usual).");
        }
        finally
        {
            if (attached)
            {
                try { Execute(connection, null, "DETACH DATABASE previous"); }
                catch (SqliteException ex) { Log.Warn("Couldn't detach the previous database", ex); }
            }
            try { Execute(connection, null, "PRAGMA foreign_keys = ON"); }
            catch (SqliteException) { }
            if (!wasOpen) connection.Close();
        }
    }

    private static string SharedColumns(SqliteConnection connection, string table)
    {
        var current = SqliteBackupHelpers.Columns(connection, table);
        var previous = SqliteBackupHelpers.Columns(connection, table, "previous").ToHashSet(StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", current.Where(previous.Contains).Select(c => $"\"{c}\""));
    }

    private static int Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    private static void RunUndo(Outcome outcome)
    {
        for (var i = outcome.UndoSteps.Count - 1; i >= 0; i--)
        {
            var (description, undo) = outcome.UndoSteps[i];
            try
            {
                undo();
            }
            catch (Exception ex)
            {
                Log.Error($"Couldn't put back {description}; the safety backup has it", ex);
            }
        }
        outcome.UndoSteps.Clear();
    }

    private static string StagedPath(string content, BackupSource source) =>
        Path.Combine(content, source.ArchivePath.Replace('/', Path.DirectorySeparatorChar));

    private static string? Truncate(string? path) => path is { Length: > 500 } ? path[^500..] : path;

    // ------------------------------------------------------------- file helpers ----

    private static readonly string[] SqliteSidecars = { "-wal", "-shm", "-journal" };

    private static void MoveFileWithSidecars(string from, string to, bool isDatabase)
    {
        File.Move(from, to, overwrite: true);
        if (!isDatabase) return;
        foreach (var suffix in SqliteSidecars)
        {
            if (File.Exists(from + suffix)) File.Move(from + suffix, to + suffix, overwrite: true);
            else if (File.Exists(to + suffix)) File.Delete(to + suffix);
        }
    }

    private static void DeleteFileWithSidecars(string path, bool isDatabase)
    {
        if (File.Exists(path)) File.Delete(path);
        DeleteSidecars(path, isDatabase);
    }

    private static void DeleteSidecars(string path, bool isDatabase)
    {
        if (!isDatabase) return;
        foreach (var suffix in SqliteSidecars)
            if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }

    private static void MoveDirectory(string from, string to)
    {
        if (Directory.Exists(to)) Directory.Delete(to, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        try
        {
            Directory.Move(from, to);
        }
        catch (IOException)
        {
            // Different drive (Directory.Move can't cross volumes): copy, then delete.
            CopyDirectory(from, to);
            Directory.Delete(from, recursive: true);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
    }
}
