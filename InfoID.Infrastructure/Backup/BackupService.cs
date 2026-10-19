using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using InfoID.Domain.Common.Enums;
using InfoID.Domain.Entities.SystemModule;
using InfoID.Shared.Logging;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Infrastructure.Backup;

/// <summary>Backup and restore of the local InfoID data (FR-ENT-4).</summary>
public interface IBackupService
{
    /// <summary>Creates a complete backup file. Never throws: failures come back in the
    /// result (and are written to the log and the BackupRestoreLog table).</summary>
    Task<BackupResult> CreateBackupAsync(BackupRequest request, IProgress<BackupProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Checks a backup file without changing anything: password, completeness,
    /// checksums and whether this version of InfoID can restore it.</summary>
    Task<BackupInspection> InspectAsync(string path, string? password, IProgress<BackupProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Validates the backup, makes a safety backup of the current data into
    /// <paramref name="safetyBackupFolder"/>, and prepares the restore. Nothing on this PC
    /// changes until InfoID restarts (see <see cref="RestoreStartup"/>).</summary>
    Task<RestoreStageResult> StageRestoreAsync(string path, string? password, string safetyBackupFolder,
        IProgress<BackupProgress>? progress = null, CancellationToken ct = default);

    /// <summary>A prepared restore is waiting for the next start.</summary>
    bool IsRestorePending { get; }

    /// <summary>Discards a prepared restore (nothing was changed yet).</summary>
    void CancelPendingRestore();

    /// <summary>Backup files in a folder, newest first.</summary>
    IReadOnlyList<BackupFileInfo> ListBackups(string folder);

    /// <summary>Deletes automatic backups beyond the newest <paramref name="keepAutomatic"/>
    /// and safety backups beyond the newest <paramref name="keepSafety"/>. Manual backups
    /// are never deleted. Returns how many files were removed.</summary>
    int PruneBackups(string folder, int keepAutomatic, int keepSafety = 5);

    /// <summary>Recent rows of BackupRestoreLog, newest first.</summary>
    Task<IReadOnlyList<BackupRestoreLog>> GetHistoryAsync(int take = 50, CancellationToken ct = default);
}

public sealed class BackupService : IBackupService
{
    private static readonly AppLogger Log = AppLog.For<BackupService>();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>One backup or restore at a time across the whole app (manual, automatic
    /// and safety backups share it), so two can never write the same files at once.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Images and archives are already compressed; zipping them again only costs time.</summary>
    private static readonly HashSet<string> StoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".zip", ".pdf", ".heic",
    };

    private readonly InfoIdDbContext _context;
    private readonly BackupOptions _options;

    private readonly InfoID.Domain.Common.Interfaces.IAuditTrail? _audit;

    public BackupService(InfoIdDbContext context, BackupOptions options, InfoID.Domain.Common.Interfaces.IAuditTrail? audit = null)
    {
        _context = context;
        _options = options;
        _audit = audit;
    }

    private Task AuditAsync(string action, string description, object? after) =>
        _audit is null
            ? Task.CompletedTask
            : _audit.RecordAsync(new InfoID.Domain.Common.Interfaces.AuditEntry(InfoID.Domain.Common.Interfaces.AuditModules.Backup, action, description)
            {
                EntityName = "Backup",
                After = after,
            });

    private Task AuditBackupAsync(BackupRequest request, BackupResult result)
    {
        var kind = request.Kind switch
        {
            BackupKind.Automatic => "Automatic backup",
            BackupKind.SafetyBeforeRestore => "Safety backup before a restore",
            _ => "Backup",
        };
        return result.Success
            ? AuditAsync("BackupCreated",
                $"{kind} saved: {Path.GetFileName(result.FilePath)}{(string.IsNullOrEmpty(request.Password) ? "" : " (password protected)")}",
                new
                {
                    File = result.FilePath,
                    Kind = request.Kind.ToString(),
                    result.SizeBytes,
                    PasswordProtected = !string.IsNullOrEmpty(request.Password),
                    Files = result.Manifest?.Entries.Count,
                    result.Manifest?.Counts.Designs,
                    result.Manifest?.Counts.Cardholders,
                    result.Manifest?.Counts.Cards,
                })
            : AuditAsync("BackupFailed", $"{kind} failed: {result.ErrorMessage}",
                new { File = request.DestinationPath, Kind = request.Kind.ToString(), Error = result.ErrorMessage });
    }

    // ------------------------------------------------------------------ backup ----

    public async Task<BackupResult> CreateBackupAsync(BackupRequest request, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DestinationPath))
            return BackupResult.Failed("No backup file was chosen.");

        var started = DateTime.UtcNow;
        BackupResult result;
        await Gate.WaitAsync(ct);
        try
        {
            result = await Task.Run(() => CreateBackupCore(request, progress, ct), ct);
        }
        catch (OperationCanceledException)
        {
            result = BackupResult.Failed("The backup was cancelled.");
        }
        catch (Exception ex)
        {
            Log.Error($"Backup failed ({request.Kind})", ex);
            result = BackupResult.Failed(Friendly(ex));
        }
        finally
        {
            Gate.Release();
        }

        await WriteLogAsync(BackupOperationType.Backup, result.Success, result.FilePath ?? request.DestinationPath, started);
        await AuditBackupAsync(request, result);
        return result;
    }

    private BackupResult CreateBackupCore(BackupRequest request, IProgress<BackupProgress>? progress, CancellationToken ct)
    {
        var destination = Path.GetFullPath(request.DestinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".partial";
        var work = Directory.CreateTempSubdirectory("InfoID-backup-").FullName;
        try
        {
            progress?.Report(new BackupProgress("Copying the database", 0.02));
            var manifest = new BackupManifest
            {
                AppVersion = _options.AppVersion,
                CreatedUtc = DateTime.UtcNow,
                MachineName = Environment.MachineName,
                Kind = request.Kind,
            };

            // 1) Collect what goes in. Databases are snapshotted first (online backup API);
            //    folders and settings files are streamed straight from disk into the ZIP.
            var items = new List<(string ArchivePath, string FilePath)>();
            var main = _options.MainDatabase;
            foreach (var source in _options.Sources)
            {
                ct.ThrowIfCancellationRequested();
                switch (source.Kind)
                {
                    case BackupSourceKind.SqliteDatabase:
                        if (!File.Exists(source.LocalPath))
                        {
                            if (source.Required)
                                throw new FileNotFoundException($"InfoID's database wasn't found at {source.LocalPath}.");
                            continue;
                        }
                        var snapshot = Path.Combine(work, "db", source.ArchivePath.Replace('/', '_'));
                        Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
                        SqliteBackupHelpers.Snapshot(source.LocalPath, snapshot);
                        if (ReferenceEquals(source, main))
                        {
                            manifest.SchemaVersion = SqliteBackupHelpers.ReadSchemaVersion(snapshot);
                            manifest.Counts = SqliteBackupHelpers.ReadCounts(snapshot);
                        }
                        items.Add((source.ArchivePath, snapshot));
                        manifest.Parts.Add(source.ArchivePath);
                        break;

                    case BackupSourceKind.Folder:
                        if (!Directory.Exists(source.LocalPath)) continue;
                        foreach (var file in Directory.EnumerateFiles(source.LocalPath, "*", SearchOption.AllDirectories))
                        {
                            if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                            var relative = Path.GetRelativePath(source.LocalPath, file).Replace('\\', '/');
                            items.Add((source.ArchivePath + "/" + relative, file));
                        }
                        manifest.Parts.Add(source.ArchivePath);
                        break;

                    case BackupSourceKind.File:
                        if (!File.Exists(source.LocalPath)) continue;
                        items.Add((source.ArchivePath, source.LocalPath));
                        manifest.Parts.Add(source.ArchivePath);
                        break;
                }
            }

            // 2) Write the ZIP, hashing every file as it is written (the hash describes
            //    exactly the bytes in the backup, even if a file changed meanwhile).
            var zipPath = Path.Combine(work, "backup.zip");
            long totalBytes = items.Sum(i => SafeLength(i.FilePath)), doneBytes = 0;
            var buffer = new byte[1 << 16];
            using (var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                foreach (var (archivePath, filePath) in items)
                {
                    ct.ThrowIfCancellationRequested();
                    FileStream input;
                    try
                    {
                        input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    }
                    catch (FileNotFoundException)
                    {
                        continue; // removed while the backup was running (e.g. a thumbnail being regenerated)
                    }
                    catch (DirectoryNotFoundException)
                    {
                        continue;
                    }

                    using (input)
                    {
                        var level = StoredExtensions.Contains(Path.GetExtension(filePath)) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                        var entry = zip.CreateEntry(archivePath, level);
                        using var output = entry.Open();
                        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        long size = 0;
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            sha.AppendData(buffer, 0, read);
                            output.Write(buffer, 0, read);
                            size += read;
                            doneBytes += read;
                        }
                        manifest.Entries.Add(new BackupManifestEntry
                        {
                            Path = archivePath,
                            Size = size,
                            Sha256 = Convert.ToHexStringLower(sha.GetHashAndReset()),
                        });
                    }

                    progress?.Report(new BackupProgress("Saving designs and settings",
                        0.1 + 0.8 * (totalBytes == 0 ? 1 : Math.Min(1, doneBytes / (double)totalBytes))));
                }

                var manifestEntry = zip.CreateEntry(BackupManifest.FileName, CompressionLevel.Optimal);
                using var manifestStream = manifestEntry.Open();
                JsonSerializer.Serialize(manifestStream, manifest, Json);
            }

            // 3) Encrypt if asked, then move into place in one step: a crash or a full disk
            //    never leaves a half-written file with the real backup name.
            progress?.Report(new BackupProgress(string.IsNullOrEmpty(request.Password) ? "Finishing" : "Encrypting", 0.92));
            if (!string.IsNullOrEmpty(request.Password))
                BackupCrypto.Encrypt(zipPath, partial, request.Password);
            else
                File.Copy(zipPath, partial, overwrite: true);
            File.Move(partial, destination, overwrite: true);

            var fileSize = new FileInfo(destination).Length;
            Log.Info($"Backup created ({request.Kind}): {destination} ({fileSize:N0} bytes, {manifest.Entries.Count} files" +
                     $"{(string.IsNullOrEmpty(request.Password) ? "" : ", password protected")})");
            progress?.Report(new BackupProgress("Done", 1));
            return new BackupResult { Success = true, FilePath = destination, SizeBytes = fileSize, Manifest = manifest };
        }
        finally
        {
            TryDeleteFile(partial);
            TryDeleteDirectory(work);
        }
    }

    // ----------------------------------------------------------------- inspect ----

    public async Task<BackupInspection> InspectAsync(string path, string? password, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        var known = _context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        try
        {
            return await Task.Run(() =>
            {
                using var opened = OpenBackup(path, password, known, progress, ct);
                return opened.Inspection;
            }, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't check backup {path}", ex);
            return new BackupInspection { FilePath = path, Problem = "The file couldn't be read: " + Friendly(ex) };
        }
    }

    /// <summary>Opens (decrypting if needed) and fully verifies a backup. The returned
    /// object owns the open ZIP and any temporary decrypted copy.</summary>
    private OpenedBackup OpenBackup(string path, string? password, IReadOnlySet<string> knownMigrations,
        IProgress<BackupProgress>? progress, CancellationToken ct)
    {
        if (!File.Exists(path))
            return OpenedBackup.Fail(path, "The file doesn't exist.");

        var encrypted = BackupCrypto.IsEncrypted(path);
        var zipPath = path;
        string? decrypted = null;
        if (encrypted)
        {
            if (string.IsNullOrEmpty(password))
                return OpenedBackup.Fail(path, "This backup is password protected. Enter its password.", encrypted: true, needsPassword: true);

            progress?.Report(new BackupProgress("Checking the password", 0.05));
            decrypted = Path.Combine(Path.GetTempPath(), $"InfoID-restore-{Guid.NewGuid():N}.zip");
            if (!BackupCrypto.TryDecrypt(path, decrypted, password))
            {
                TryDeleteFile(decrypted);
                return OpenedBackup.Fail(path, "The password is wrong, or the file is damaged.", encrypted: true, needsPassword: true, wrongPassword: true);
            }
            zipPath = decrypted;
        }

        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException)
        {
            TryDeleteFile(decrypted);
            return OpenedBackup.Fail(path, "This isn't an InfoID backup file, or it is damaged.", encrypted);
        }

        var opened = new OpenedBackup(path, zip, decrypted, encrypted);
        try
        {
            var manifestEntry = zip.GetEntry(BackupManifest.FileName);
            if (manifestEntry is null)
                return opened.WithProblem("This isn't an InfoID backup file (it has no manifest).");

            BackupManifest? manifest;
            try
            {
                using var stream = manifestEntry.Open();
                manifest = JsonSerializer.Deserialize<BackupManifest>(stream, Json);
            }
            catch (JsonException)
            {
                manifest = null;
            }
            if (manifest is null || !string.Equals(manifest.Product, "InfoID", StringComparison.Ordinal))
                return opened.WithProblem("This isn't an InfoID backup file, or it is damaged.");
            opened.Manifest = manifest;

            if (manifest.FormatVersion > BackupManifest.CurrentFormatVersion)
                return opened.WithProblem($"This backup was made by a newer version of InfoID ({manifest.AppVersion}). Update InfoID, then restore it.");

            if (!manifest.Entries.Any(e => e.Path == _options.MainDatabase.ArchivePath))
                return opened.WithProblem("This backup doesn't contain InfoID's database.");

            if (manifest.SchemaVersion is null || !knownMigrations.Contains(manifest.SchemaVersion))
                return opened.WithProblem($"This backup was made by a newer version of InfoID ({manifest.AppVersion}). Update InfoID, then restore it.");

            // Every file must be present and match its checksum before anything is restored.
            long total = manifest.Entries.Sum(e => e.Size), done = 0;
            var buffer = new byte[1 << 16];
            foreach (var item in manifest.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (!IsSafeArchivePath(item.Path))
                    return opened.WithProblem("The backup contains an invalid file path and can't be restored.");
                var entry = zip.GetEntry(item.Path);
                if (entry is null)
                    return opened.WithProblem($"The backup is incomplete: {item.Path} is missing.");

                using var stream = entry.Open();
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long size = 0;
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    size += read;
                    done += read;
                }
                if (size != item.Size || !string.Equals(Convert.ToHexStringLower(sha.GetHashAndReset()), item.Sha256, StringComparison.OrdinalIgnoreCase))
                    return opened.WithProblem($"The backup is damaged: {item.Path} failed its check.");

                progress?.Report(new BackupProgress("Checking the backup", 0.1 + 0.3 * (total == 0 ? 1 : done / (double)total)));
            }

            opened.Inspection = new BackupInspection
            {
                FilePath = path,
                IsEncrypted = encrypted,
                Manifest = manifest,
                CanRestore = true,
            };
            return opened;
        }
        catch
        {
            opened.Dispose();
            throw;
        }
    }

    // ----------------------------------------------------------------- restore ----

    public bool IsRestorePending =>
        File.Exists(Path.Combine(_options.StagingDirectory, PendingRestore.FileName));

    public void CancelPendingRestore()
    {
        var wasPending = IsRestorePending;
        TryDeleteDirectory(_options.StagingDirectory);
        if (wasPending) _ = AuditAsync("RestoreCancelled", "A prepared restore was cancelled before it was applied", null);
        Log.Info("Prepared restore cancelled.");
    }

    public async Task<RestoreStageResult> StageRestoreAsync(string path, string? password, string safetyBackupFolder,
        IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        var known = _context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        RestoreStageResult result;
        BackupResult? safety = null;
        await Gate.WaitAsync(ct);
        try
        {
            (result, safety) = await Task.Run(() => StageCore(path, password, safetyBackupFolder, known, progress, ct), ct);
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(_options.StagingDirectory);
            result = new RestoreStageResult { Success = false, ErrorMessage = "The restore was cancelled. Nothing was changed." };
        }
        catch (Exception ex)
        {
            Log.Error($"Preparing the restore from {path} failed", ex);
            TryDeleteDirectory(_options.StagingDirectory);
            result = new RestoreStageResult { Success = false, ErrorMessage = Friendly(ex) + " Nothing was changed." };
        }
        finally
        {
            Gate.Release();
        }

        if (safety is not null)
        {
            await WriteLogAsync(BackupOperationType.Backup, safety.Success, safety.FilePath, started);
            await AuditBackupAsync(new BackupRequest { DestinationPath = safety.FilePath ?? "", Kind = BackupKind.SafetyBeforeRestore }, safety);
        }
        // A successful restore is logged after the restart, into the restored database.
        if (!result.Success)
        {
            await WriteLogAsync(BackupOperationType.Restore, false, path, started);
            await AuditAsync("RestoreFailed", $"Restore from {Path.GetFileName(path)} failed: {result.ErrorMessage}", new { File = path, Error = result.ErrorMessage });
        }
        else
        {
            await AuditAsync("RestorePrepared",
                $"Restore from {Path.GetFileName(path)} prepared; InfoID restarts to apply it",
                new { File = path, SafetyBackup = result.SafetyBackupPath });
        }
        return result;
    }

    private (RestoreStageResult Result, BackupResult? Safety) StageCore(string path, string? password, string safetyFolder,
        IReadOnlySet<string> knownMigrations, IProgress<BackupProgress>? progress, CancellationToken ct)
    {
        using var opened = OpenBackup(path, password, knownMigrations, progress, ct);
        if (!opened.Inspection.CanRestore)
            return (new RestoreStageResult { Success = false, ErrorMessage = opened.Inspection.Problem }, null);
        var manifest = opened.Manifest!;

        // A safety backup of what's on this PC now, so the restore can be undone.
        progress?.Report(new BackupProgress("Making a safety backup of your current data", 0.45));
        var safetyPath = BackupFileNames.CreateUniquePath(safetyFolder, DateTime.Now, BackupKind.SafetyBeforeRestore);
        BackupResult safety;
        try
        {
            safety = CreateBackupCore(new BackupRequest { DestinationPath = safetyPath, Kind = BackupKind.SafetyBeforeRestore }, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error("Safety backup before restore failed", ex);
            safety = BackupResult.Failed(Friendly(ex));
        }
        if (!safety.Success)
            return (new RestoreStageResult
            {
                Success = false,
                ErrorMessage = "InfoID couldn't make a safety backup of your current data, so nothing was changed. " + safety.ErrorMessage,
            }, safety);

        // Unpack into the staging folder (inside InfoID's data folder only).
        progress?.Report(new BackupProgress("Preparing the restore", 0.75));
        var staging = _options.StagingDirectory;
        TryDeleteDirectory(staging);
        var content = Path.Combine(staging, RestoreStartup.ContentFolderName);
        Directory.CreateDirectory(content);
        var contentRoot = Path.GetFullPath(content) + Path.DirectorySeparatorChar;
        try
        {
            foreach (var item in manifest.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(content, item.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The backup contains an invalid file path.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                opened.Zip!.GetEntry(item.Path)!.ExtractToFile(target, overwrite: true);
            }

            // The databases themselves must be healthy, not just the bytes intact.
            progress?.Report(new BackupProgress("Checking the database", 0.9));
            foreach (var source in _options.Sources.Where(s => s.Kind == BackupSourceKind.SqliteDatabase))
            {
                var staged = Path.Combine(content, source.ArchivePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(staged)) continue;
                var problem = SqliteBackupHelpers.CheckIntegrity(staged, requireEfHistory: source.Required);
                if (problem is not null)
                    throw new InvalidDataException($"The database in this backup is damaged ({problem}).");
            }

            var pending = new PendingRestore
            {
                SourceFile = path,
                SafetyBackupPath = safety.FilePath,
                StagedUtc = DateTime.UtcNow,
                BackupCreatedUtc = manifest.CreatedUtc,
                BackupAppVersion = manifest.AppVersion,
            };
            var pendingPath = Path.Combine(staging, PendingRestore.FileName);
            File.WriteAllText(pendingPath + ".tmp", JsonSerializer.Serialize(pending, Json));
            File.Move(pendingPath + ".tmp", pendingPath, overwrite: true);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }

        Log.Info($"Restore prepared from {path} (backup of {manifest.CreatedUtc:u}); safety backup {safety.FilePath}. Applies on restart.");
        progress?.Report(new BackupProgress("Ready", 1));
        return (new RestoreStageResult { Success = true, SafetyBackupPath = safety.FilePath }, safety);
    }

    // -------------------------------------------------------------- list/prune ----

    public IReadOnlyList<BackupFileInfo> ListBackups(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return Array.Empty<BackupFileInfo>();
        var list = new List<BackupFileInfo>();
        foreach (var file in Directory.EnumerateFiles(folder, BackupFileNames.Pattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                var info = new FileInfo(file);
                var (stamp, kind) = BackupFileNames.Parse(info.Name);
                list.Add(new BackupFileInfo
                {
                    FilePath = info.FullName,
                    CreatedLocal = stamp ?? info.LastWriteTime,
                    SizeBytes = info.Length,
                    Kind = kind,
                    IsEncrypted = BackupCrypto.IsEncrypted(info.FullName),
                });
            }
            catch (IOException)
            {
                // Being written or removed right now; it shows up on the next refresh.
            }
        }
        return list.OrderByDescending(b => b.CreatedLocal).ToList();
    }

    public int PruneBackups(string folder, int keepAutomatic, int keepSafety = 5)
    {
        var removed = 0;
        var all = ListBackups(folder);
        foreach (var (kind, keep) in new[] { (BackupKind.Automatic, keepAutomatic), (BackupKind.SafetyBeforeRestore, keepSafety) })
        {
            foreach (var old in all.Where(b => b.Kind == kind).Skip(Math.Max(1, keep)))
            {
                try
                {
                    File.Delete(old.FilePath);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Warn($"Couldn't remove old backup {old.FileName}", ex);
                }
            }
        }
        if (removed > 0) Log.Info($"Removed {removed} old backup(s) from {folder}");
        return removed;
    }

    public async Task<IReadOnlyList<BackupRestoreLog>> GetHistoryAsync(int take = 50, CancellationToken ct = default) =>
        await _context.BackupRestoreLogs.AsNoTracking()
            .Where(x => !x.Cancelled)
            .OrderByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct);

    // ----------------------------------------------------------------- helpers ----

    private async Task WriteLogAsync(BackupOperationType operation, bool success, string? filePath, DateTime startedUtc)
    {
        try
        {
            _context.BackupRestoreLogs.Add(new BackupRestoreLog
            {
                OperationType = operation,
                Status = success ? BackupStatus.Success : BackupStatus.Failed,
                FilePath = filePath is { Length: > 500 } ? filePath[^500..] : filePath,
                StartedDate = startedUtc,
                CompletedDate = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't record the backup in the history", ex);
        }
    }

    internal static bool IsSafeArchivePath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && !path.Contains('\\') && !path.Contains(':') && !path.StartsWith('/')
        && path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");

    internal static string Friendly(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "InfoID isn't allowed to write to that folder. Choose another folder.",
        IOException io when (io.HResult & 0xFFFF) is 0x70 or 0x27 => "There isn't enough free disk space.",
        FileNotFoundException f => f.Message,
        InvalidDataException d => d.Message,
        _ => ex.GetBaseException().Message,
    };

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    internal static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't remove temporary folder {path}", ex);
        }
    }

    private sealed class OpenedBackup : IDisposable
    {
        public OpenedBackup(string path, ZipArchive? zip, string? decryptedTemp, bool encrypted)
        {
            Zip = zip;
            DecryptedTemp = decryptedTemp;
            Inspection = new BackupInspection { FilePath = path, IsEncrypted = encrypted };
        }

        public ZipArchive? Zip { get; }
        public string? DecryptedTemp { get; }
        public BackupManifest? Manifest { get; set; }
        public BackupInspection Inspection { get; set; }

        public static OpenedBackup Fail(string path, string problem, bool encrypted = false, bool needsPassword = false, bool wrongPassword = false) =>
            new(path, null, null, encrypted)
            {
                Inspection = new BackupInspection
                {
                    FilePath = path, IsEncrypted = encrypted, NeedsPassword = needsPassword, WrongPassword = wrongPassword, Problem = problem,
                },
            };

        public OpenedBackup WithProblem(string problem)
        {
            Inspection = new BackupInspection
            {
                FilePath = Inspection.FilePath, IsEncrypted = Inspection.IsEncrypted, Manifest = Manifest, Problem = problem,
            };
            return this;
        }

        public void Dispose()
        {
            Zip?.Dispose();
            TryDeleteFile(DecryptedTemp);
        }
    }
}
