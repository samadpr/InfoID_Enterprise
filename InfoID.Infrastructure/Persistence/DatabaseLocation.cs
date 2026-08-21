using System;
using System.IO;

namespace InfoID.Infrastructure.Persistence;

/// <summary>
/// Single source of truth for every filesystem location InfoID's desktop app writes to
/// (database, logs, future asset/backup folders). Centralizing this here satisfies the
/// requirement that the database path be configurable through application infrastructure
/// instead of hardcoded ad hoc in DependencyInjection.cs, InfoIdDbContextFactory, and
/// anywhere else that used to duplicate the AppData\InfoID\Database logic.
///
/// Cross-platform via SpecialFolder.ApplicationData:
///   Windows: %AppData%\InfoID\Database\InfoID_DB.db  (C:\Users\{user}\AppData\Roaming\InfoID\Database)
///   Linux:   ~/.config/InfoID/Database/InfoID_DB.db
///   macOS:   ~/Library/Application Support/InfoID/Database/InfoID_DB.db
/// </summary>
public static class DatabaseLocation
{
    public const string ApplicationFolderName = "InfoID";
    public const string DatabaseFolderName = "Database";
    public const string DatabaseFileName = "InfoID_DB.db";
    public const string LogsFolderName = "Logs";

    /// <summary>Root %AppData%\InfoID (or OS equivalent) folder. Created if missing.</summary>
    public static string GetApplicationRoot()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = Path.Combine(appData, ApplicationFolderName);
        Directory.CreateDirectory(root);
        return root;
    }

    public static string GetDatabaseDirectory()
    {
        var directory = Path.Combine(GetApplicationRoot(), DatabaseFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetDefaultDatabasePath() =>
        Path.Combine(GetDatabaseDirectory(), DatabaseFileName);

    public static string GetLogsDirectory()
    {
        var directory = Path.Combine(GetApplicationRoot(), LogsFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }
}