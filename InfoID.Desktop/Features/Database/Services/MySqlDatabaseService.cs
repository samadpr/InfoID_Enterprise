using InfoID.Desktop.Features.Database.Models;
using MySqlConnector;
using System;
using System.Collections.ObjectModel;
using System.Data;

namespace InfoID.Desktop.Features.Database.Services;

public sealed class MySqlDatabaseService
{
    public string BuildConnectionString(
     string server,
     string port,
     string database,
     string username,
     string password,
     bool useSsl = false)
    {
        if (string.IsNullOrWhiteSpace(server))
            throw new ArgumentException("Server is required.");

        if (string.IsNullOrWhiteSpace(database))
            throw new ArgumentException("Database name is required.");

        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.");

        if (string.IsNullOrWhiteSpace(port))
            port = "3306";

        var builder = new MySqlConnectionStringBuilder
        {
            Server = server,
            Port = uint.Parse(port),
            Database = database,
            UserID = username,
            Password = password
        };

        if (useSsl)
        {
            builder.SslMode =
                MySqlSslMode.Required;
        }

        return builder.ConnectionString;
    }

    public string TestConnection(string connectionString)
    {
        using var connection =
            new MySqlConnection(connectionString);

        connection.Open();

        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "MySQL database connection could not be opened.");
        }

        using var command =
            connection.CreateCommand();

        command.CommandText =
            "SHOW SESSION STATUS LIKE 'Ssl_cipher';";

        using var reader =
            command.ExecuteReader();

        if (!reader.Read())
            return string.Empty;

        return reader["Value"]?.ToString() ?? string.Empty;
    }

    public ObservableCollection<string> GetDatabases(
    string server,
    string port,
    string username,
    string password,
    bool useSsl = false)
    {
        var databases =
            new ObservableCollection<string>();

        var connectionString =
            BuildConnectionString(
                server,
                port,
                "mysql",
                username,
                password,
                useSsl);

        using var connection =
            new MySqlConnection(connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = """
        SELECT SCHEMA_NAME
        FROM information_schema.SCHEMATA
        ORDER BY SCHEMA_NAME;
        """;

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            var databaseName =
                reader["SCHEMA_NAME"]?.ToString();

            if (string.IsNullOrWhiteSpace(databaseName))
                continue;

            databases.Add(databaseName);
        }

        return databases;
    }
    public ObservableCollection<string> GetTables(
        string connectionString)
    {
        var tables =
            new ObservableCollection<string>();

        using var connection =
            new MySqlConnection(connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT TABLE_NAME
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
            AND TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME;
            """;

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            var tableName =
                reader["TABLE_NAME"]?.ToString();

            if (string.IsNullOrWhiteSpace(tableName))
                continue;

            tables.Add(tableName);
        }

        return tables;
    }

    public ObservableCollection<DatabaseColumn> GetColumnDefinitions(
        string connectionString,
        string tableName)
    {
        var columns =
            new ObservableCollection<DatabaseColumn>();

        using var connection =
            new MySqlConnection(connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT
                COLUMN_NAME,
                DATA_TYPE,
                COLUMN_KEY
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
            AND TABLE_NAME = @tableName
            ORDER BY ORDINAL_POSITION;
            """;

        command.Parameters.AddWithValue(
            "@tableName",
            tableName);

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            var columnName =
                reader["COLUMN_NAME"]?.ToString();

            if (string.IsNullOrWhiteSpace(columnName))
                continue;

            var dataType =
                reader["DATA_TYPE"]?.ToString()
                ?? "Unknown";

            var columnKey =
                reader["COLUMN_KEY"]?.ToString();

            columns.Add(
                new DatabaseColumn
                {
                    Name = columnName,
                    DataType = dataType,
                    IsPrimaryKey =
                        columnKey == "PRI",
                    IsSelected = true
                });
        }

        return columns;
    }

    public DataTable GetData(
        string connectionString,
        string tableName)
    {
        using var connection =
            new MySqlConnection(connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            $"SELECT * FROM `{tableName.Replace("`", "``")}`;";

        using var reader =
            command.ExecuteReader();

        var table =
            new DataTable();

        table.Load(reader);

        return table;
    }
}
