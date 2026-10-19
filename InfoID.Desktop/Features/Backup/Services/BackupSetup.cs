using System;
using System.IO;
using InfoID.Desktop.Features.Database.Services;
using InfoID.Infrastructure.Backup;
using InfoID.Infrastructure.Persistence;

namespace InfoID.Desktop.Features.Backup.Services;

/// <summary>
/// What an InfoID backup contains, in one place. When a new feature starts saving data
/// in its own file, add it here so it is backed up and restored too.
///
/// Deliberately NOT included:
/// - the license (it lives in the database, but a restore always keeps this PC's own --
///   see RestoreStartup.KeepThisPcsLicense),
/// - account.session and GoogleSheetsTokens (sign-in tokens),
/// - Logs, DigitalCopies (generated output), RestoreStaging, BackupSettings.json
///   (this PC's backup folder and schedule).
/// </summary>
public static class BackupSetup
{
    /// <summary>Documents\InfoID Backups: easy to find, and usually covered by the
    /// user's own PC backup or OneDrive.</summary>
    public static string DefaultBackupFolder
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrEmpty(documents)
                ? Path.Combine(DatabaseLocation.GetApplicationRoot(), "Backups")
                : Path.Combine(documents, "InfoID Backups");
        }
    }

    public static BackupOptions CreateOptions()
    {
        var root = DatabaseLocation.GetApplicationRoot();
        BackupSource Setting(string fileName) =>
            new($"settings/{fileName}", BackupSourceKind.File, Path.Combine(root, fileName), Description: "Settings");

        return new BackupOptions
        {
            AppVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            StagingDirectory = Path.Combine(root, "RestoreStaging"),
            Sources = new[]
            {
                new BackupSource("database/InfoID_DB.db", BackupSourceKind.SqliteDatabase, DatabaseLocation.GetDefaultDatabasePath(),
                    Required: true, Description: "Designs, cardholders, cards and print history"),
                new BackupSource("data/InfoID.db", BackupSourceKind.SqliteDatabase, InternalSqliteDatabaseService.GetDatabasePath(),
                    Description: "Internal Database tables"),
                new BackupSource("assets", BackupSourceKind.Folder, Path.Combine(root, DatabaseLocation.AssetsFolderName),
                    RestoreMode.Merge, Description: "Design images and photos"),
                new BackupSource("thumbnails", BackupSourceKind.Folder, Path.Combine(root, "Thumbnails"),
                    Description: "Design thumbnails"),
                Setting("Preferences.json"),
                Setting("PrintSettings.json"),
                Setting("PrintHistorySettings.json"),
                Setting("PrintStatus.json"),
                Setting("DashboardSettings.json"),
                Setting("CustomCardModels.json"),
                Setting("RecentFiles.json"),
                Setting("RecentFonts.json"),
            },
        };
    }
}
