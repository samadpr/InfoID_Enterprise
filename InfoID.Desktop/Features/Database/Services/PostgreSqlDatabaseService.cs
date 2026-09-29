using Npgsql;
using System;
using System.Data;
using System.Collections.ObjectModel;
using InfoID.Desktop.Features.Database.Models;
namespace InfoID.Desktop.Features.Database.Services;

public sealed class PostgreSqlDatabaseService
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
            port = "5432";

        var builder =
            new NpgsqlConnectionStringBuilder
            {
                Host = server,
                Port = int.Parse(port),
                Database = database,
                Username = username,
                Password = password
            };

        if (useSsl)
        {
            builder.SslMode =
                SslMode.Require;
        }

        return builder.ConnectionString;
    }
    public void TestConnection(string connectionString)
    {
        using var connection =
            new NpgsqlConnection(connectionString);

        connection.Open();

        if (connection.State !=
            System.Data.ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "PostgreSQL database connection could not be opened.");
        }
    }
    public ObservableCollection<string> GetTables(
    string connectionString)
    {
        var tables =
            new ObservableCollection<string>();

        using var connection =
            new NpgsqlConnection(connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = """
        SELECT table_name
        FROM information_schema.tables
        WHERE table_schema = 'public'
        AND table_type = 'BASE TABLE'
        ORDER BY table_name;
        """;

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            var tableName =
                reader["table_name"]?.ToString();

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
            new NpgsqlConnection(connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = """
        SELECT
            c.column_name,
            c.data_type,
            CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM information_schema.table_constraints tc
                    INNER JOIN information_schema.key_column_usage kcu
                        ON tc.constraint_name = kcu.constraint_name
                        AND tc.table_schema = kcu.table_schema
                    WHERE tc.constraint_type = 'PRIMARY KEY'
                    AND tc.table_schema = c.table_schema
                    AND tc.table_name = c.table_name
                    AND kcu.column_name = c.column_name
                )
                THEN TRUE
                ELSE FALSE
            END AS is_primary_key
        FROM information_schema.columns c
        WHERE c.table_schema = 'public'
        AND c.table_name = @tableName
        ORDER BY c.ordinal_position;
        """;

        command.Parameters.AddWithValue("@tableName", tableName);

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            var columnName =
                reader["column_name"]?.ToString();

            if (string.IsNullOrWhiteSpace(columnName))
                continue;

            columns.Add(
                new DatabaseColumn
                {
                    Name = columnName,
                    DataType =
                        reader["data_type"]?.ToString()
                        ?? "Unknown",
                    IsPrimaryKey =
                        Convert.ToBoolean(
                            reader["is_primary_key"]),
                    IsSelected = true
                });
        }

        return columns;
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
                "postgres",
                username,
                password,
                useSsl);

        using var connection =
            new NpgsqlConnection(connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = """
        SELECT datname
        FROM pg_database
        WHERE datistemplate = FALSE
        ORDER BY datname;
        """;

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            var databaseName =
                reader["datname"]?.ToString();

            if (string.IsNullOrWhiteSpace(databaseName))
                continue;

            databases.Add(databaseName);
        }

        return databases;
    }
    public DataTable GetData(string connectionString,string tableName)
    {
        using var connection =
            new NpgsqlConnection(connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        var safeTableName =
            tableName.Replace("\"", "\"\"");

        command.CommandText =
            $"SELECT * FROM \"{safeTableName}\";";

        using var reader =
            command.ExecuteReader();

        var table =
            new DataTable();

        table.Load(reader);

        return table;
    }
}
