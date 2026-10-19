using Microsoft.Data.Sqlite;

namespace InfoID.Infrastructure.Backup;

/// <summary>SQLite operations used by backup and restore. Every connection here is opened
/// with Pooling=false, so no file handle lingers after the call -- the restore step has to
/// move these files, which Windows refuses while a handle is open.</summary>
internal static class SqliteBackupHelpers
{
    public static SqliteConnection Open(string path, SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>Consistent copy of a live database using SQLite's online backup API: safe
    /// while InfoID is reading or writing it (unlike a plain file copy, which can catch a
    /// half-written page).</summary>
    public static void Snapshot(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        using var source = Open(sourcePath);
        using var destination = Open(destinationPath, SqliteOpenMode.ReadWriteCreate);
        source.BackupDatabase(destination);
    }

    /// <summary>Newest applied EF Core migration, or null if the table is missing.</summary>
    public static string? ReadSchemaVersion(string databasePath)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadOnly);
        if (!TableExists(connection, "__EFMigrationsHistory")) return null;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId DESC LIMIT 1";
        return command.ExecuteScalar() as string;
    }

    public static BackupCounts ReadCounts(string databasePath)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadOnly);
        return new BackupCounts
        {
            Designs = Count(connection, "SELECT COUNT(*) FROM \"Template\" WHERE Cancelled = 0"),
            Cardholders = Count(connection,
                "SELECT COUNT(*) FROM \"Cardholder\" WHERE Cancelled = 0 AND (ExternalRefId IS NULL OR ExternalRefId <> '__GENERIC_CARD__')"),
            Cards = Count(connection, "SELECT COUNT(*) FROM \"Card\" WHERE Cancelled = 0"),
            PrintJobs = Count(connection, "SELECT COUNT(*) FROM \"PrintJob\" WHERE Cancelled = 0"),
        };
    }

    /// <summary>PRAGMA integrity_check; returns null when the database is healthy, else the
    /// first problem SQLite reports.</summary>
    public static string? CheckIntegrity(string databasePath, bool requireEfHistory)
    {
        try
        {
            using var connection = Open(databasePath, SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check";
            var result = command.ExecuteScalar() as string;
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                return result ?? "integrity check failed";
            if (requireEfHistory && !TableExists(connection, "__EFMigrationsHistory"))
                return "it isn't an InfoID database";
            return null;
        }
        catch (SqliteException ex)
        {
            return ex.Message;
        }
    }

    public static bool TableExists(SqliteConnection connection, string table, string schema = "main")
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{schema}\".sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    public static IReadOnlyList<string> Columns(SqliteConnection connection, string table, string schema = "main")
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA \"{schema}\".table_info(\"{table}\")";
        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) columns.Add(reader.GetString(1));
        return columns;
    }

    private static int Count(SqliteConnection connection, string sql)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
        }
        catch (SqliteException)
        {
            // Table or column missing in an older schema: the summary just shows 0.
            return 0;
        }
    }
}
